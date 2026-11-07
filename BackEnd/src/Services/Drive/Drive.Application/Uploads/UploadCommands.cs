using Drive.Application.Abstractions;
using Drive.Application.Items;
using Drive.Application.Versions;
using Drive.Domain;
using Drive.Domain.Items;
using Drive.Domain.Uploads;
using FluentValidation;
using MyCloud.BuildingBlocks.Application.Messaging;
using MyCloud.BuildingBlocks.Domain.Results;

namespace Drive.Application.Uploads;

public sealed record UploadDto(
    Guid UploadId,
    long PartSize,
    int PartCount,
    IReadOnlyList<PresignedPart> Parts,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Bắt đầu upload (docs/05): Storage giữ chỗ quota + mở multipart, trả URL ký sẵn; trình duyệt PUT thẳng từng part
/// lên object storage — byte của file không đi qua Gateway hay service .NET (ADR-002).
/// <paramref name="TargetItemId"/> có giá trị ⇒ nội dung mới cho file đó (version kế tiếp), bỏ qua parent/name.
/// </summary>
public sealed record StartUploadCommand(
    Guid? ParentId, string? Name, long SizeBytes, string? MimeType, string? PublicBaseUrl, Guid? TargetItemId = null)
    : ICommand<UploadDto>;

internal sealed class StartUploadCommandValidator : AbstractValidator<StartUploadCommand>
{
    public StartUploadCommandValidator()
    {
        When(x => x.TargetItemId is null, () =>
        {
            RuleFor(x => x.ParentId).NotEmpty();
            RuleFor(x => x.Name).NotEmpty().MaximumLength(ItemName.MaxLength);
        });
        RuleFor(x => x.SizeBytes).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MimeType).MaximumLength(255);
    }
}

internal sealed class StartUploadCommandHandler(
    ItemAccess access,
    IPendingUploadRepository uploads,
    IStorageClient storage,
    TimeProvider timeProvider) : ICommandHandler<StartUploadCommand, UploadDto>
{
    public async Task<Result<UploadDto>> Handle(StartUploadCommand request, CancellationToken cancellationToken)
    {
        // Kiểm tra quyền + đích TRƯỚC khi Storage giữ chỗ quota.
        DriveItem destination = request.TargetItemId is { } targetId
            ? await access.GetFileAsync(targetId, ItemPermission.Edit, cancellationToken)
            : await access.GetFolderAsync(request.ParentId!.Value, ItemPermission.Edit, cancellationToken);
        ItemName? name = request.TargetItemId is null ? ItemName.Create(System.IO.Path.GetFileName(request.Name)) : null;
        string mimeType = string.IsNullOrWhiteSpace(request.MimeType) ? "application/octet-stream" : request.MimeType.Trim();

        // Dung lượng tính cho chủ sở hữu thư mục / file (có thể khác người upload khi thư mục được chia sẻ quyền sửa).
        StorageUploadSession session = await storage.CreateUploadSessionAsync(
            destination.OwnerId, access.UserId, request.SizeBytes, mimeType, request.PublicBaseUrl, cancellationToken);

        DateTimeOffset now = timeProvider.GetUtcNow();
        uploads.Add(name is { } fileName
            ? PendingUpload.Start(session.SessionId, access.UserId, destination, fileName, request.SizeBytes, mimeType, now, session.ExpiresAt)
            : PendingUpload.StartNewVersion(session.SessionId, access.UserId, destination, request.SizeBytes, mimeType, now, session.ExpiresAt));

        return new UploadDto(session.SessionId, session.PartSizeBytes, session.PartCount, session.Parts, session.ExpiresAt);
    }
}

/// <summary>
/// Hoàn tất upload: Storage ghép part + kiểm tra kích thước, Drive tạo file (tự đổi tên nếu trùng) hoặc thêm version mới,
/// và phát <c>FileVersionAdded</c> để Storage "claim" blob (ADR-006). Gọi lặp sau khi thành công trả lại cùng file.
/// </summary>
public sealed record CompleteUploadCommand(Guid UploadId, IReadOnlyList<CompletedPart> Parts) : ICommand<ItemDto>;

internal sealed class CompleteUploadCommandValidator : AbstractValidator<CompleteUploadCommand>
{
    public CompleteUploadCommandValidator()
    {
        RuleFor(x => x.Parts).NotEmpty().Must(p => p.Count <= 10_000);
        RuleForEach(x => x.Parts).Must(p => p.PartNumber >= 1 && !string.IsNullOrWhiteSpace(p.ETag));
    }
}

internal sealed class CompleteUploadCommandHandler(
    ItemAccess access,
    IPendingUploadRepository uploads,
    IDriveItemRepository items,
    IStorageClient storage,
    UniqueNames uniqueNames,
    VersionRetention retention,
    ItemDtos dtos) : ICommandHandler<CompleteUploadCommand, ItemDto>
{
    public async Task<Result<ItemDto>> Handle(CompleteUploadCommand request, CancellationToken cancellationToken)
    {
        PendingUpload upload = await FindOwnedAsync(request.UploadId, cancellationToken);
        if (upload.CompletedItemId is { } completedId)
        {
            return await dtos.ForAsync(await access.GetAsync(completedId, ItemPermission.View, cancellationToken), cancellationToken);
        }

        if (upload.TargetItemId is { } targetId)
        {
            DriveItem file = await access.GetFileAsync(targetId, ItemPermission.Edit, cancellationToken);
            CompletedBlob content = await storage.CompleteUploadAsync(file.OwnerId, upload.Id, request.Parts, cancellationToken);
            file.AddVersion(Guid.CreateVersion7(), content.BlobId, content.SizeBytes, upload.MimeType);
            await retention.ApplyAsync(file, cancellationToken);
            upload.MarkCompleted(file.Id);
            return await dtos.ForAsync(file, cancellationToken);
        }

        DriveItem parent = await access.GetFolderAsync(upload.ParentId, ItemPermission.Edit, cancellationToken);
        CompletedBlob blob = await storage.CompleteUploadAsync(parent.OwnerId, upload.Id, request.Parts, cancellationToken);

        ItemName name = await uniqueNames.ResolveAsync(parent.Id, ItemName.Create(upload.Name), isFile: true, cancellationToken);
        var created = DriveItem.CreateFile(Guid.CreateVersion7(), parent, name, Guid.CreateVersion7(), blob.BlobId, blob.SizeBytes, upload.MimeType);
        items.Add(created);
        upload.MarkCompleted(created.Id);

        return await dtos.ForAsync(created, cancellationToken);
    }

    private async Task<PendingUpload> FindOwnedAsync(Guid uploadId, CancellationToken ct)
    {
        PendingUpload? upload = await uploads.FindAsync(uploadId, ct);
        return upload is not null && upload.OwnerId == access.UserId ? upload : throw new DriveException.UploadNotFound(uploadId);
    }
}

public sealed record AbortUploadCommand(Guid UploadId) : ICommand;

internal sealed class AbortUploadCommandHandler(
    ItemAccess access, IPendingUploadRepository uploads, IDriveItemRepository items, IStorageClient storage)
    : ICommandHandler<AbortUploadCommand>
{
    public async Task<Result> Handle(AbortUploadCommand request, CancellationToken cancellationToken)
    {
        PendingUpload? upload = await uploads.FindAsync(request.UploadId, cancellationToken);
        if (upload is null || upload.OwnerId != access.UserId)
        {
            throw new DriveException.UploadNotFound(request.UploadId);
        }

        if (upload.CompletedItemId is null)
        {
            // Session của Storage thuộc chủ sở hữu thư mục đích (người được giữ chỗ dung lượng).
            Guid owner = (await items.FindAsync(upload.ParentId, cancellationToken))?.OwnerId ?? upload.OwnerId;
            await storage.AbortUploadAsync(owner, upload.Id, cancellationToken);
        }

        return Result.Success();
    }
}

public sealed record DownloadLinkDto(string Url, DateTimeOffset ExpiresAt);

/// <summary>
/// URL tải (presigned, 5 phút) của version hiện tại hoặc <paramref name="VersionId"/>. Trả JSON thay vì redirect vì
/// trình duyệt không gắn được header Authorization khi điều hướng (docs/03 §3). Là command vì ghi nhận lượt mở cho
/// "Gần đây" (<c>ItemAccessed</c>, đi qua outbox).
/// </summary>
public sealed record CreateDownloadLinkCommand(Guid ItemId, Guid? VersionId, bool Inline, string? PublicBaseUrl)
    : ICommand<DownloadLinkDto>;

internal sealed class CreateDownloadLinkCommandHandler(
    ItemAccess access,
    IFileVersionRepository versions,
    IStorageClient storage,
    IItemAccessLog accessLog) : ICommandHandler<CreateDownloadLinkCommand, DownloadLinkDto>
{
    public async Task<Result<DownloadLinkDto>> Handle(CreateDownloadLinkCommand request, CancellationToken cancellationToken)
    {
        DriveItem item = await access.GetAsync(request.ItemId, ItemPermission.View, cancellationToken);
        if (item.IsFolder || item.CurrentVersionId is not { } currentId)
        {
            throw new DriveException.NotAFile(item.Id);
        }

        Guid versionId = request.VersionId ?? currentId;
        FileVersion version = await versions.FindAsync(versionId, cancellationToken) is { } v && v.ItemId == item.Id
            ? v
            : throw new DriveException.VersionNotFound(versionId);

        DownloadLink link = await storage.GetDownloadUrlAsync(
            item.OwnerId, version.BlobId, item.Name, version.MimeType, request.Inline, request.PublicBaseUrl, cancellationToken);
        if (access.OptionalUserId is { } userId) // khách mở link (ẩn danh) không có "Gần đây"
        {
            await accessLog.RecordAsync(item, userId, request.Inline ? AccessActions.Open : AccessActions.Download, cancellationToken);
        }
        return new DownloadLinkDto(link.Url, link.ExpiresAt);
    }
}
