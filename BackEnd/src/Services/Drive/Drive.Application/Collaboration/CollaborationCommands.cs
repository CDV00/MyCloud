using Drive.Application.Abstractions;
using Drive.Domain;
using Drive.Domain.Comments;
using Drive.Domain.Items;
using FluentValidation;
using MyCloud.BuildingBlocks.Application.Messaging;
using MyCloud.BuildingBlocks.Domain.Results;

namespace Drive.Application.Collaboration;

/// <param name="VersionNo">Số phiên bản được góp ý (null nếu trao đổi chung hoặc phiên bản đã bị xoá).</param>
/// <param name="Body">Null nếu tin nhắn đã bị xoá.</param>
public sealed record CommentDto(
    Guid Id,
    Guid ItemId,
    Guid? VersionId,
    int? VersionNo,
    Guid? ParentId,
    Guid AuthorId,
    string? Body,
    IReadOnlyList<Guid> Mentions,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    bool Deleted,
    DateTimeOffset? ResolvedAt,
    Guid? ResolvedBy)
{
    public static CommentDto From(ItemComment c, IReadOnlyDictionary<Guid, int> versionNumbers) =>
        new(
            c.Id,
            c.ItemId,
            c.VersionId,
            c.VersionId is { } v && versionNumbers.TryGetValue(v, out int no) ? no : null,
            c.ParentId,
            c.AuthorId,
            c.IsDeleted ? null : c.Body,
            c.Mentions,
            c.CreatedAt,
            c.EditedAt,
            c.IsDeleted,
            c.ResolvedAt,
            c.ResolvedBy);
}

internal static class Kinds
{
    public const string Comment = "comment";
    public const string VersionNote = "version_note";
}

/// <summary>Toàn bộ trao đổi về tệp (tối đa 1000 tin gần nhất), cũ trước. Ai xem được tệp thì đọc được.</summary>
public sealed record ListCommentsQuery(Guid ItemId) : IQuery<IReadOnlyList<CommentDto>>;

internal sealed class ListCommentsQueryHandler(ItemAccess access, IItemCommentRepository comments, IFileVersionRepository versions)
    : IQueryHandler<ListCommentsQuery, IReadOnlyList<CommentDto>>
{
    public async Task<Result<IReadOnlyList<CommentDto>>> Handle(ListCommentsQuery request, CancellationToken cancellationToken)
    {
        DriveItem item = await access.GetAsync(request.ItemId, ItemPermission.View, cancellationToken);
        IReadOnlyList<ItemComment> list = await comments.ListAsync(item.Id, 1000, cancellationToken);
        Dictionary<Guid, int> numbers = (await versions.ListAsync(item.Id, cancellationToken)).ToDictionary(v => v.Id, v => v.VersionNo);
        return list.Select(c => CommentDto.From(c, numbers)).ToArray();
    }
}

/// <summary>
/// Gửi tin nhắn (người nhận xét trở lên): trao đổi chung, góp ý một phiên bản (<paramref name="VersionId"/>), hoặc trả lời
/// (<paramref name="ParentId"/>). Người được nhắc tên phải có quyền với tệp (chủ sở hữu / được chia sẻ) — tên khác bị bỏ qua.
/// </summary>
public sealed record PostCommentCommand(Guid ItemId, string Body, Guid? VersionId, Guid? ParentId, IReadOnlyList<Guid> Mentions)
    : ICommand<CommentDto>;

internal sealed class PostCommentCommandValidator : AbstractValidator<PostCommentCommand>
{
    public PostCommentCommandValidator()
    {
        RuleFor(x => x.Body).NotEmpty().MaximumLength(ItemComment.MaxLength);
        RuleFor(x => x.Mentions).NotNull().Must(m => m.Count <= 20);
    }
}

internal sealed class PostCommentCommandHandler(
    ItemAccess access,
    IItemCommentRepository comments,
    IFileVersionRepository versions,
    ISharingClient sharing,
    IUserDirectory users,
    ICollaborationEvents events,
    TimeProvider timeProvider) : ICommandHandler<PostCommentCommand, CommentDto>
{
    public async Task<Result<CommentDto>> Handle(PostCommentCommand request, CancellationToken cancellationToken)
    {
        DriveItem item = await access.GetAsync(request.ItemId, ItemPermission.Comment, cancellationToken);
        Guid author = access.UserId;

        ItemComment? parent = null;
        if (request.ParentId is { } parentId)
        {
            parent = await comments.FindAsync(parentId, cancellationToken) is { } p && p.ItemId == item.Id && !p.IsDeleted
                ? p
                : throw new CommentNotFound(parentId);
        }

        FileVersion? version = null;
        Guid? versionId = parent?.VersionId ?? request.VersionId;
        if (versionId is { } vid)
        {
            version = await versions.FindAsync(vid, cancellationToken);
            if (version is null || version.ItemId != item.Id)
            {
                throw new DriveException.VersionNotFound(vid);
            }
        }

        Guid[] mentions = await AllowedMentionsAsync(item, request.Mentions, cancellationToken);
        var comment = ItemComment.Post(Guid.CreateVersion7(), item.Id, versionId, parent, author, request.Body, mentions, timeProvider.GetUtcNow());
        comments.Add(comment);

        // Người nhận: chủ tệp, người tạo phiên bản được góp ý, người trong luồng, người được nhắc tên — trừ chính người viết.
        var recipients = new HashSet<Guid> { item.OwnerId };
        if (version is not null)
        {
            recipients.Add(version.CreatedBy);
        }

        if (comment.ParentId is { } root)
        {
            recipients.UnionWith(await comments.ThreadParticipantsAsync(root, cancellationToken));
        }

        recipients.UnionWith(comment.Mentions);
        recipients.Remove(author);

        string authorName = (await users.GetManyAsync([author], cancellationToken)).GetValueOrDefault(author)?.DisplayName ?? "Một người dùng";
        string excerpt = comment.Body.Length > 200 ? comment.Body[..200] + "…" : comment.Body;
        await events.CommentPostedAsync(
            new CommentPostedNotice(
                comment.Id, item.Id, item.OwnerId, item.Name, versionId, version?.VersionNo, comment.ParentId, author, authorName, excerpt,
                recipients.ToArray(), comment.Mentions),
            cancellationToken);

        return CommentDto.From(comment, version is null ? new Dictionary<Guid, int>() : new() { [version.Id] = version.VersionNo });
    }

    private async Task<Guid[]> AllowedMentionsAsync(DriveItem item, IReadOnlyList<Guid> requested, CancellationToken ct)
    {
        if (requested.Count == 0)
        {
            return [];
        }

        var allowed = new HashSet<Guid> { item.OwnerId };
        allowed.UnionWith((await sharing.ListPermissionsAsync(item, ct)).Select(p => p.UserId));
        return requested.Where(allowed.Contains).Distinct().ToArray();
    }
}

public sealed record EditCommentCommand(Guid ItemId, Guid CommentId, string Body) : ICommand<CommentDto>;

internal sealed class EditCommentCommandHandler(
    ItemAccess access, IItemCommentRepository comments, ICollaborationEvents events, TimeProvider timeProvider)
    : ICommandHandler<EditCommentCommand, CommentDto>
{
    public async Task<Result<CommentDto>> Handle(EditCommentCommand request, CancellationToken cancellationToken)
    {
        DriveItem item = await access.GetAsync(request.ItemId, ItemPermission.Comment, cancellationToken);
        ItemComment comment = await FindAsync(comments, item, request.CommentId, cancellationToken);
        comment.Edit(access.UserId, request.Body, timeProvider.GetUtcNow());
        await events.HistoryChangedAsync(item.Id, item.OwnerId, Kinds.Comment, cancellationToken);
        return CommentDto.From(comment, new Dictionary<Guid, int>());
    }

    internal static async Task<ItemComment> FindAsync(IItemCommentRepository comments, DriveItem item, Guid commentId, CancellationToken ct) =>
        await comments.FindAsync(commentId, ct) is { } c && c.ItemId == item.Id ? c : throw new CommentNotFound(commentId);
}

/// <summary>Xoá tin nhắn: người viết, hoặc chủ sở hữu tệp (kiểm duyệt).</summary>
public sealed record DeleteCommentCommand(Guid ItemId, Guid CommentId) : ICommand;

internal sealed class DeleteCommentCommandHandler(
    ItemAccess access, IItemCommentRepository comments, ICollaborationEvents events, TimeProvider timeProvider)
    : ICommandHandler<DeleteCommentCommand>
{
    public async Task<Result> Handle(DeleteCommentCommand request, CancellationToken cancellationToken)
    {
        DriveItem item = await access.GetAsync(request.ItemId, ItemPermission.View, cancellationToken);
        ItemComment comment = await EditCommentCommandHandler.FindAsync(comments, item, request.CommentId, cancellationToken);
        comment.Delete(access.UserId, canModerate: item.OwnerId == access.UserId, timeProvider.GetUtcNow());
        await events.HistoryChangedAsync(item.Id, item.OwnerId, Kinds.Comment, cancellationToken);
        return Result.Success();
    }
}

/// <summary>Đánh dấu luồng góp ý đã xử lý / mở lại (người nhận xét trở lên).</summary>
public sealed record ResolveCommentCommand(Guid ItemId, Guid CommentId, bool Resolved) : ICommand<CommentDto>;

internal sealed class ResolveCommentCommandHandler(
    ItemAccess access, IItemCommentRepository comments, ICollaborationEvents events, TimeProvider timeProvider)
    : ICommandHandler<ResolveCommentCommand, CommentDto>
{
    public async Task<Result<CommentDto>> Handle(ResolveCommentCommand request, CancellationToken cancellationToken)
    {
        DriveItem item = await access.GetAsync(request.ItemId, ItemPermission.Comment, cancellationToken);
        ItemComment comment = await EditCommentCommandHandler.FindAsync(comments, item, request.CommentId, cancellationToken);
        comment.SetResolved(request.Resolved, access.UserId, timeProvider.GetUtcNow());
        await events.HistoryChangedAsync(item.Id, item.OwnerId, Kinds.Comment, cancellationToken);
        return CommentDto.From(comment, new Dictionary<Guid, int>());
    }
}

/// <summary>Sửa ghi chú thay đổi của một phiên bản: người tạo phiên bản hoặc chủ sở hữu tệp.</summary>
public sealed record UpdateVersionNoteCommand(Guid ItemId, Guid VersionId, string? Note) : ICommand;

internal sealed class UpdateVersionNoteCommandHandler(ItemAccess access, IFileVersionRepository versions, ICollaborationEvents events)
    : ICommandHandler<UpdateVersionNoteCommand>
{
    public async Task<Result> Handle(UpdateVersionNoteCommand request, CancellationToken cancellationToken)
    {
        DriveItem item = await access.GetFileAsync(request.ItemId, ItemPermission.View, cancellationToken);
        FileVersion version = await versions.FindAsync(request.VersionId, cancellationToken) is { } v && v.ItemId == item.Id
            ? v
            : throw new DriveException.VersionNotFound(request.VersionId);
        if (version.CreatedBy != access.UserId && item.OwnerId != access.UserId)
        {
            throw new DriveException.AccessDenied(item.Id);
        }

        version.SetNote(request.Note);
        await events.HistoryChangedAsync(item.Id, item.OwnerId, Kinds.VersionNote, cancellationToken);
        return Result.Success();
    }
}

/// <summary>Vé vào phòng realtime của tệp (ai xem được tệp thì vào được) — Notification kiểm tra chữ ký.</summary>
public sealed record IssueLiveTicketQuery(Guid ItemId) : IQuery<LiveRoomTicket>;

internal sealed class IssueLiveTicketQueryHandler(ItemAccess access, ILiveRoomTokens tokens) : IQueryHandler<IssueLiveTicketQuery, LiveRoomTicket>
{
    public async Task<Result<LiveRoomTicket>> Handle(IssueLiveTicketQuery request, CancellationToken cancellationToken)
    {
        DriveItem item = await access.GetAsync(request.ItemId, ItemPermission.View, cancellationToken);
        return tokens.Issue(item.Id, access.UserId);
    }
}
