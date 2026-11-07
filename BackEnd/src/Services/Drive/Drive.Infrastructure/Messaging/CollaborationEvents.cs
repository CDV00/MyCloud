using Drive.Application.Abstractions;
using MassTransit;
using Microsoft.Extensions.Options;
using MyCloud.BuildingBlocks.Authentication;
using MyCloud.Contracts.Events.Drive;

namespace Drive.Infrastructure.Messaging;

/// <summary>Bản tin trao đổi đi qua outbox (lưu cùng transaction với tin nhắn — UnitOfWork của command).</summary>
internal sealed class CollaborationEvents(IPublishEndpoint publishEndpoint) : ICollaborationEvents
{
    public Task CommentPostedAsync(CommentPostedNotice n, CancellationToken ct) =>
        publishEndpoint.Publish(
            new ItemCommentPosted(
                n.CommentId, n.ItemId, n.OwnerId, n.ItemName, n.VersionId, n.VersionNo, n.ParentId, n.AuthorId, n.AuthorName,
                n.Excerpt, n.Recipients, n.Mentions),
            ct);

    public Task HistoryChangedAsync(Guid itemId, Guid ownerId, string kind, CancellationToken ct) =>
        publishEndpoint.Publish(new ItemHistoryChanged(itemId, ownerId, kind), ct);
}

/// <summary>Ký vé vào phòng realtime (secret dùng chung với Notification — section <c>Collaboration</c>).</summary>
internal sealed class LiveRoomTokens(IOptions<CollaborationOptions> options, TimeProvider timeProvider) : ILiveRoomTokens
{
    public LiveRoomTicket Issue(Guid itemId, Guid userId)
    {
        DateTimeOffset expiresAt = timeProvider.GetUtcNow() + options.Value.TokenLifetime;
        return new LiveRoomTicket(LiveRoomToken.Create(options.Value.Secret, itemId, userId, expiresAt), expiresAt);
    }
}
