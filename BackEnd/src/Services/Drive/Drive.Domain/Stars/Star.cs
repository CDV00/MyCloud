namespace Drive.Domain.Stars;

/// <summary>Người dùng gắn dấu sao một item (riêng từng người — Phase 4 người được chia sẻ cũng gắn sao được).</summary>
public sealed class Star
{
    private Star()
    {
    }

    public Guid UserId { get; private set; }

    public Guid ItemId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Star Create(Guid userId, Guid itemId, DateTimeOffset now) =>
        new() { UserId = userId, ItemId = itemId, CreatedAt = now };
}
