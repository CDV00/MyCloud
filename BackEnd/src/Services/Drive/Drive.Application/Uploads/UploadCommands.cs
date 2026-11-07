using Drive.Application.Abstractions;
using Drive.Application.Items;
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
/// </summary>
public sealed record StartUploadCommand(Guid ParentId, string Name, long SizeBytes, string? MimeType, string? PublicBaseUrl)
    : ICommand<UploadDto>;

internal sealed class StartUploadCommandValidator : AbstractValidator<StartUploadCommand>
{
    public StartUploadCommandValidator()
    {
        RuleFor(x => x.ParentId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ItemName.MaxLength);
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
        DriveItem parent = await access.GetFolderAsync(request.ParentId, ItemPermission.Edit, cancellationToken);
        ItemName name = ItemName.Create(System.IO.Path.GetFileName(request.Name));
        string mimeType = string.IsNullOrWhiteSpace(request.MimeType) ? "application/octet-stream" : request.MimeType.Trim();

        StorageUploadSession session = await storage.CreateUploadSessionAsync(
            access.UserId, request.SizeBytes, mimeType, request.PublicBaseUrl, cancellationToken);

        uploads.Add(PendingUpload.Start(
            session.SessionId, access.UserId, parent, name, request.SizeBytes, mimeType, timeProvider.GetUtcNow(), session.ExpiresAt));

        return new UploadDto(session.SessionId, session.PartSizeBytes, session.PartCount, session.Parts, session.ExpiresAt);
    }
}

/// <summary>
/// Hoàn tất upload: Storage ghép part + kiểm tra kích thước, Drive tạo file (tự đổi tên nếu trùng) và phát
/// <c>FileVersionAdded</c> để Storage "claim" blob (ADR-006). Gọi lặp sau khi thành công trả lại cùng file.
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
    UniqueNames uniqueNames) : ICommandHandler<CompleteUploadCommand, ItemDto>
{
    public async Task<Result<ItemDto>> Handle(CompleteUploadCommand request, CancellationToken cancellationToken)
    {
        PendingUpload upload = await FindOwnedAsync(request.UploadId, cancellationToken);
        if (upload.CompletedItemId is { } completedId)
        {
            return ItemDto.From(await access.GetAsync(completedId, ItemPermission.View, cancellationToken));
        }

        DriveItem parent = await access.GetFolderAsync(upload.ParentId, ItemPermission.Edit, cancellationToken);
        CompletedBlob blob = await storage.CompleteUploadAsync(access.UserId, upload.Id, request.Parts, cancellationToken);

        ItemName name = await uniqueNames.ResolveAsync(parent.Id, ItemName.Create(upload.Name), isFile: true, cancellationToken);
        var file = DriveItem.CreateFile(Guid.CreateVersion7(), parent, name, Guid.CreateVersion7(), blob.BlobId, blob.SizeBytes, upload.MimeType);
        items.Add(file);
        upload.MarkCompleted(file.Id);

        return ItemDto.From(file);
    }

    private async Task<PendingUpload> FindOwnedAsync(Guid uploadId, CancellationToken ct)
    {
        PendingUpload? upload = await uploads.FindAsync(uploadId, ct);
        return upload is not null && upload.OwnerId == access.UserId ? upload : throw new DriveException.UploadNotFound(uploadId);
    }
}

public sealed record AbortUploadCommand(Guid UploadId) : ICommand;

internal sealed class AbortUploadCommandHandler(ItemAccess access, IPendingUploadRepository uploads, IStorageClient storage)
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
            await storage.AbortUploadAsync(access.UserId, upload.Id, cancellationToken);
        }

        return Result.Success();
    }
}

public sealed record DownloadLinkDto(string Url, DateTimeOffset ExpiresAt);

/// <summary>
/// URL tải (presigned, 5 phút). Trả JSON thay vì redirect vì trình duyệt không gắn được header Authorization khi điều
/// hướng; frontend lấy URL rồi mở (docs/03 §3).
/// </summary>
public sealed record GetDownloadUrlQuery(Guid ItemId, bool Inline, string? PublicBaseUrl) : IQuery<DownloadLinkDto>;

internal sealed class GetDownloadUrlQueryHandler(ItemAccess access, IFileVersionReader versions, IStorageClient storage)
    : IQueryHandler<GetDownloadUrlQuery, DownloadLinkDto>
{
    public async Task<Result<DownloadLinkDto>> Handle(GetDownloadUrlQuery request, CancellationToken cancellationToken)
    {
        DriveItem item = await access.GetAsync(request.ItemId, ItemPermission.View, cancellationToken);
        if (item.IsFolder || item.CurrentVersionId is not { } versionId)
        {
            throw new DriveException.NotAFile(item.Id);
        }

        FileVersion version = await versions.GetAsync(versionId, cancellationToken);
        DownloadLink link = await storage.GetDownloadUrlAsync(
            item.OwnerId, version.BlobId, item.Name, version.MimeType, request.Inline, request.PublicBaseUrl, cancellationToken);
        return new DownloadLinkDto(link.Url, link.ExpiresAt);
    }
}

public interface IFileVersionReader
{
    Task<FileVersion> GetAsync(Guid versionId, CancellationToken ct);
}
