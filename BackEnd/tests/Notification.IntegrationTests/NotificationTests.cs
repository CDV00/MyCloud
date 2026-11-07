using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MyCloud.Contracts.Events.Identity;
using MyCloud.Contracts.Events.Sharing;
using MyCloud.Contracts.Events.Storage;
using MyCloud.Testing;
using Notification.Api.Delivery;
using Testcontainers.PostgreSql;

namespace Notification.IntegrationTests;

/// <summary>Notification.Api thật + PostgreSQL thật; RabbitMQ thay bằng test harness (outbox vẫn chạy thật).</summary>
public sealed class NotificationApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string CollaborationSecret = "test-collaboration-secret";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:notificationdb", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:rabbitmq", "amqp://guest:guest@localhost:5672");
        builder.UseSetting("Email:WebBaseUrl", "http://mycloud.test");
        builder.UseSetting("Collaboration:Secret", CollaborationSecret);
        builder.ConfigureTestServices(services =>
        {
            services.AddMassTransitTestHarness();
            services.TrustTestJwt();
        });
    }
}

public sealed class NotificationTests(NotificationApiFactory factory) : IClassFixture<NotificationApiFactory>
{
    private sealed record Item(Guid Id, string Type, JsonElement Payload, bool Read);

    private sealed record ListResponse(Item[] Items, int UnreadCount);

    private ITestHarness Harness => factory.Services.GetRequiredService<ITestHarness>();

    [Fact]
    public async Task ItemShared_CreatesNotification_EmailsGrantee_AndPushesRealtime()
    {
        Guid owner = Guid.NewGuid(), grantee = Guid.NewGuid(), itemId = Guid.NewGuid();
        await PublishAndWaitAsync(new UserRegistered(owner, "an@example.com", "Nguyễn An"));
        await PublishAndWaitAsync(new UserRegistered(grantee, "binh@example.com", "Trần Bình"));

        await PublishAndWaitAsync(new ItemShared(Guid.NewGuid(), itemId, owner, "Báo cáo <Q4>", "folder", grantee, "editor", owner, true, "Xem giúp"));

        ListResponse list = await ListAsync(grantee);
        list.UnreadCount.ShouldBe(1);
        Item n = list.Items.ShouldHaveSingleItem();
        n.Type.ShouldBe("item_shared");
        n.Payload.GetProperty("sharedByName").GetString().ShouldBe("Nguyễn An");
        n.Payload.GetProperty("itemName").GetString().ShouldBe("Báo cáo <Q4>");

        (await DeliveredAsync<SendEmail>(x => x.Context.Message.To == "binh@example.com"
            && x.Context.Message.Subject.Contains("Báo cáo <Q4>")
            && x.Context.Message.HtmlBody.Contains("Báo cáo &lt;Q4&gt;") // tên do người dùng đặt được escape
            && x.Context.Message.HtmlBody.Contains($"http://mycloud.test/my-drive/folders/{itemId}"))).ShouldBeTrue();
        (await DeliveredAsync<PushNotification>(x => x.Context.Message.UserId == grantee && x.Context.Message.NotificationId == n.Id))
            .ShouldBeTrue();
        (await ListAsync(owner)).Items.ShouldBeEmpty(); // người chia sẻ không nhận thông báo
    }

    [Fact]
    public async Task ItemShared_WithoutNotify_SkipsEmail_ButKeepsInAppNotification()
    {
        Guid grantee = Guid.NewGuid(), itemId = Guid.NewGuid();
        await PublishAndWaitAsync(new UserRegistered(grantee, "c@example.com", "C"));

        await PublishAndWaitAsync(new ItemShared(Guid.NewGuid(), itemId, Guid.NewGuid(), "x.txt", "file", grantee, "viewer", Guid.NewGuid(), false, null));

        (await ListAsync(grantee)).Items.ShouldHaveSingleItem();
        Harness.Consumed.Select<SendEmail>(x => x.Context.Message.To == "c@example.com" && x.Context.Message.Subject.Contains("x.txt"))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task ItemShared_ForUsersWithoutStoredContacts_UsesNamesFromTheEvent()
    {
        // Tài khoản tạo trước khi Notification lưu liên hệ: event mang sẵn tên người chia sẻ và email người nhận.
        Guid grantee = Guid.NewGuid();
        await PublishAndWaitAsync(new ItemShared(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "cũ.txt", "file", grantee, "viewer", Guid.NewGuid(), true, null,
            SharedByName: "Lê Cũ", GranteeEmail: "grantee-old@example.com"));

        (await ListAsync(grantee)).Items.ShouldHaveSingleItem().Payload.GetProperty("sharedByName").GetString().ShouldBe("Lê Cũ");
        (await DeliveredAsync<SendEmail>(x => x.Context.Message.To == "grantee-old@example.com"
            && x.Context.Message.Subject.StartsWith("Lê Cũ", StringComparison.Ordinal))).ShouldBeTrue();
    }

    [Fact]
    public async Task MarkRead_AndReadAll_UpdateUnreadCount_OnlyForOwnNotifications()
    {
        Guid user = Guid.NewGuid();
        await PublishAndWaitAsync(new QuotaThresholdReached(user, 80, 80, 100));
        await PublishAndWaitAsync(new QuotaThresholdReached(user, 90, 90, 100));
        ListResponse list = await ListAsync(user);
        list.UnreadCount.ShouldBe(2);
        list.Items.Select(i => i.Payload.GetProperty("percent").GetInt32()).ShouldBe([90, 80]); // mới nhất trước

        using HttpClient intruder = factory.CreateClient().AsUser(Guid.NewGuid());
        (await intruder.PostAsync($"/api/v1/notifications/{list.Items[0].Id}/read", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using HttpClient client = factory.CreateClient().AsUser(user);
        (await client.PostAsync($"/api/v1/notifications/{list.Items[0].Id}/read", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListAsync(user)).UnreadCount.ShouldBe(1);
        (await client.PostAsync("/api/v1/notifications/read-all", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListAsync(user)).UnreadCount.ShouldBe(0);
    }

    [Fact]
    public async Task ArchiveReadyAndFailed_BecomeInAppNotifications_PushedRealtime_WithoutEmail()
    {
        Guid user = Guid.NewGuid(), archive = Guid.NewGuid();
        await PublishAndWaitAsync(new ArchiveReady(archive, user, "Dự án.zip", 1234, 3, 1, DateTimeOffset.UtcNow.AddDays(1)));
        await PublishAndWaitAsync(new ArchiveFailed(Guid.NewGuid(), user, "Ảnh.zip", "Không tạo được tệp zip."));

        Item[] items = (await ListAsync(user)).Items;
        items.Select(i => i.Type).ShouldBe(["archive_failed", "archive_ready"]);
        Item ready = items[1];
        ready.Payload.GetProperty("archiveId").GetGuid().ShouldBe(archive);
        ready.Payload.GetProperty("name").GetString().ShouldBe("Dự án.zip");
        ready.Payload.GetProperty("skippedCount").GetInt32().ShouldBe(1);
        (await DeliveredAsync<PushNotification>(x => x.Context.Message.UserId == user)).ShouldBeTrue();
        (await Harness.Published.Any<SendEmail>(x => x.Context.Message.Subject.Contains("Dự án.zip"))).ShouldBeFalse();
    }

    [Fact]
    public async Task Notifications_RequireAuthentication()
    {
        using HttpClient anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/notifications")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task PublishAndWaitAsync<T>(T message)
        where T : MyCloud.Contracts.Events.IntegrationEvent
    {
        await Harness.Bus.Publish(message);
        for (int i = 0; i < 50 && !Harness.Consumed.Select<T>(x => x.Context.Message.EventId == message.EventId).Any(); i++)
        {
            await Task.Delay(100);
        }

        Harness.Consumed.Select<T>(x => x.Context.Message.EventId == message.EventId).ShouldNotBeEmpty();
    }

    /// <summary>
    /// Chờ bản tin nội bộ được giao tới consumer của nó (tối đa 5 s). Bản tin publish trong consumer đi qua outbox của
    /// endpoint nên harness không ghi vào <c>Published</c>; kiểm tra phía nhận (<c>Consumed</c>, kể cả lần gửi SMTP lỗi).
    /// </summary>
    private async Task<bool> DeliveredAsync<T>(FilterDelegate<IReceivedMessage<T>> filter)
        where T : class
    {
        for (int i = 0; i < 50; i++)
        {
            if (Harness.Consumed.Select(filter).Any())
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    private async Task<ListResponse> ListAsync(Guid userId)
    {
        using HttpClient client = factory.CreateClient().AsUser(userId);
        return (await client.GetFromJsonAsync<ListResponse>("/api/v1/notifications"))!;
    }
}
