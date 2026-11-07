using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using MassTransit.Testing;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using MyCloud.BuildingBlocks.Authentication;
using MyCloud.Contracts.Events.Drive;
using MyCloud.Testing;
using Notification.Api.Delivery;

namespace Notification.IntegrationTests;

// Phòng realtime của tệp (ADR-017): vé vào phòng, hoạt động mới, ai đang xem; thông báo khi có tin nhắn.
public sealed class ItemRoomTests(NotificationApiFactory factory) : IClassFixture<NotificationApiFactory>
{
    private sealed record Item(Guid Id, string Type, JsonElement Payload, bool Read);

    private sealed record ListResponse(Item[] Items, int UnreadCount);

    [Fact]
    public async Task CommentPosted_NotifiesEachRecipient_ButNotTheAuthor_FlaggingMentions()
    {
        Guid author = Guid.NewGuid(), owner = Guid.NewGuid(), mentioned = Guid.NewGuid(), item = Guid.NewGuid();
        var message = new ItemCommentPosted(
            Guid.NewGuid(), item, owner, "báo cáo.docx", Guid.NewGuid(), 3, null, author, "Lan", "Dòng 3 sai số liệu",
            [owner, mentioned, author], [mentioned]);
        ITestHarness harness = factory.Services.GetRequiredService<ITestHarness>();
        await harness.Bus.Publish(message);
        await WaitAsync(async () => (await ListAsync(owner)).Items.Length == 1 && (await ListAsync(mentioned)).Items.Length == 1);

        Item forOwner = (await ListAsync(owner)).Items.Single();
        forOwner.Type.ShouldBe("comment");
        forOwner.Payload.GetProperty("versionNo").GetInt32().ShouldBe(3);
        forOwner.Payload.GetProperty("mentioned").GetBoolean().ShouldBeFalse();
        (await ListAsync(mentioned)).Items.Single().Payload.GetProperty("mentioned").GetBoolean().ShouldBeTrue();
        (await ListAsync(author)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Watch_RequiresAValidTicket_ThenDeliversActivityAndPresenceOfOthers()
    {
        Guid alice = Guid.NewGuid(), bob = Guid.NewGuid(), item = Guid.NewGuid();
        await using HubConnection aliceHub = await ConnectAsync(alice);
        await using HubConnection bobHub = await ConnectAsync(bob);
        var activity = new ConcurrentQueue<ItemActivity>();
        var presence = new ConcurrentQueue<PresenceUpdate>();
        aliceHub.On<ItemActivity>(ItemRooms.ActivityMethod, a => activity.Enqueue(a));
        aliceHub.On<PresenceUpdate>(ItemRooms.PresenceMethod, p => presence.Enqueue(p));

        // Vé của người khác / ký bằng secret khác / hết hạn ⇒ từ chối.
        (await aliceHub.InvokeAsync<bool>("Watch", Ticket(bob, item))).ShouldBeFalse();
        (await aliceHub.InvokeAsync<bool>("Watch", LiveRoomToken.Create("wrong", item, alice, DateTimeOffset.UtcNow.AddHours(1)))).ShouldBeFalse();
        (await aliceHub.InvokeAsync<bool>("Watch", Ticket(alice, item, DateTimeOffset.UtcNow.AddMinutes(-1)))).ShouldBeFalse();

        (await aliceHub.InvokeAsync<bool>("Watch", Ticket(alice, item))).ShouldBeTrue();
        (await bobHub.InvokeAsync<bool>("Watch", Ticket(bob, item))).ShouldBeTrue();
        await bobHub.InvokeAsync("Presence", item.ToString(), "editing");
        await factory.Services.GetRequiredService<ITestHarness>().Bus.Publish(new ItemHistoryChanged(item, Guid.NewGuid(), "comment"));

        await WaitAsync(() => Task.FromResult(!activity.IsEmpty && !presence.IsEmpty));
        activity.ShouldContain(a => a.ItemId == item && a.Kind == "comment");
        presence.ShouldContain(p => p.ItemId == item && p.UserId == bob && p.Mode == "editing");

        await bobHub.InvokeAsync("Unwatch", item.ToString());
        await WaitAsync(() => Task.FromResult(presence.Any(p => p.UserId == bob && p.Mode == "left")));
    }

    private static string Ticket(Guid user, Guid item, DateTimeOffset? expires = null) =>
        LiveRoomToken.Create(NotificationApiFactory.CollaborationSecret, item, user, expires ?? DateTimeOffset.UtcNow.AddHours(1));

    private async Task<HubConnection> ConnectAsync(Guid user)
    {
        HubConnection connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, NotificationHub.Path), o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling; // TestServer không hỗ trợ WebSocket thật
                o.AccessTokenProvider = () => Task.FromResult<string?>(TestJwt.CreateToken(user));
            })
            .Build();
        await connection.StartAsync();
        return connection;
    }

    private async Task<ListResponse> ListAsync(Guid user)
    {
        using HttpClient client = factory.CreateClient().AsUser(user);
        return (await client.GetFromJsonAsync<ListResponse>("/api/v1/notifications"))!;
    }

    private static async Task WaitAsync(Func<Task<bool>> condition)
    {
        for (int i = 0; i < 100; i++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Condition was not met within 10 seconds.");
    }
}
