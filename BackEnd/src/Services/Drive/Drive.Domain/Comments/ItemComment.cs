using MyCloud.BuildingBlocks.Domain.Exceptions;
using MyCloud.BuildingBlocks.Domain.Primitives;

namespace Drive.Domain.Comments;

public sealed class CommentNotFound(Guid commentId) : NotFoundException($"Comment '{commentId}' was not found.")
{
    public override string Code => "DRIVE.COMMENT_NOT_FOUND";
}

public sealed class CommentNotAllowed(string reason) : ForbiddenException(reason)
{
    public override string Code => "DRIVE.COMMENT_NOT_ALLOWED";
}

public sealed class InvalidComment(string reason) : BadRequestException(reason)
{
    public override string Code => "DRIVE.INVALID_COMMENT";
}

/// <summary>
/// Trao đổi về một tệp (ADR-017): gắn với cả tệp hoặc với một phiên bản cụ thể ("góp ý bản sửa trước"). Một cấp trả lời:
/// trả lời một câu trả lời được gắn vào luồng gốc. Luồng gốc đánh dấu "đã xử lý" được. Xoá là xoá mềm (giữ chỗ trong
/// luồng: "Tin nhắn đã bị xoá").
/// </summary>
public sealed class ItemComment : Entity<Guid>
{
    public const int MaxLength = 4000;

    private ItemComment()
    {
    }

    public Guid ItemId { get; private set; }

    /// <summary>Phiên bản được góp ý; null = trao đổi chung về tệp.</summary>
    public Guid? VersionId { get; private set; }

    /// <summary>Luồng gốc (null nếu chính nó là gốc).</summary>
    public Guid? ParentId { get; private set; }

    public Guid AuthorId { get; private set; }

    public string Body { get; private set; } = string.Empty;

    /// <summary>Người được nhắc tên (@) — nhận thông báo.</summary>
    public List<Guid> Mentions { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? EditedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public Guid? ResolvedBy { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public static ItemComment Post(
        Guid id, Guid itemId, Guid? versionId, ItemComment? parent, Guid authorId, string body, IEnumerable<Guid> mentions, DateTimeOffset now)
    {
        if (parent is not null && parent.ItemId != itemId)
        {
            throw new InvalidComment("The parent comment belongs to another item.");
        }

        return new ItemComment
        {
            Id = id,
            ItemId = itemId,
            // Trả lời thuộc về luồng gốc và cùng phiên bản với luồng đó.
            ParentId = parent is null ? null : parent.ParentId ?? parent.Id,
            VersionId = parent is null ? versionId : parent.VersionId,
            AuthorId = authorId,
            Body = Normalize(body),
            Mentions = mentions.Distinct().Where(m => m != authorId).Take(20).ToList(),
            CreatedAt = now,
        };
    }

    public void Edit(Guid by, string body, DateTimeOffset now)
    {
        if (by != AuthorId)
        {
            throw new CommentNotAllowed("Only the author can edit a comment.");
        }

        if (IsDeleted)
        {
            throw new CommentNotFound(Id);
        }

        Body = Normalize(body);
        EditedAt = now;
    }

    /// <param name="canModerate">Chủ sở hữu tệp xoá được mọi tin nhắn.</param>
    public void Delete(Guid by, bool canModerate, DateTimeOffset now)
    {
        if (by != AuthorId && !canModerate)
        {
            throw new CommentNotAllowed("Only the author or the file owner can delete a comment.");
        }

        if (!IsDeleted)
        {
            DeletedAt = now;
            Body = string.Empty;
            Mentions = [];
        }
    }

    public void SetResolved(bool resolved, Guid by, DateTimeOffset now)
    {
        if (ParentId is not null)
        {
            throw new InvalidComment("Only a thread (not a reply) can be resolved.");
        }

        ResolvedAt = resolved ? ResolvedAt ?? now : null;
        ResolvedBy = resolved ? ResolvedBy ?? by : null;
    }

    private static string Normalize(string body)
    {
        string text = (body ?? string.Empty).Trim();
        if (text.Length == 0 || text.Length > MaxLength)
        {
            throw new InvalidComment($"A comment must have between 1 and {MaxLength} characters.");
        }

        return text;
    }
}
