using System.Net;
using System.Net.Http.Json;
using MyCloud.Testing;

namespace Storage.IntegrationTests;

// Quản trị dung lượng (trang quản trị người dùng).
public sealed partial class StorageTests
{
    private sealed record AdminQuota(Guid UserId, long LimitBytes, long UsedBytes, long ReservedBytes);

    [Fact]
    public async Task Admin_ListsQuotas_IncludingUsersWithoutARow_AndChangesTheLimit()
    {
        Guid user = Guid.NewGuid(), fresh = Guid.NewGuid();
        await UploadAsync(factory.GrpcClientFor(user), user, RandomBytes(1000));
        using HttpClient admin = factory.CreateClient().AsUser(Guid.NewGuid(), admin: true);

        AdminQuota[] quotas = (await admin.GetFromJsonAsync<AdminQuota[]>($"/api/v1/admin/quotas?userIds={user},{fresh}"))!;
        quotas.Single(q => q.UserId == user).UsedBytes.ShouldBe(1000);
        quotas.Single(q => q.UserId == fresh).LimitBytes.ShouldBe(StorageApiFactory.QuotaBytes); // mặc định

        HttpResponseMessage set = await admin.PutAsJsonAsync($"/api/v1/admin/quotas/{user}", new { limitBytes = 500 });
        (await set.Content.ReadFromJsonAsync<AdminQuota>())!.LimitBytes.ShouldBe(500);

        // Hạn mức thấp hơn phần đã dùng: file cũ giữ nguyên, không tải lên thêm được.
        (await QuotaAsync(user)).AvailableBytes.ShouldBe(0);
        (await Should.ThrowAsync<Grpc.Core.RpcException>(() => factory.GrpcClientFor(user).CreateUploadSessionAsync(
            new MyCloud.Contracts.Storage.V1.CreateUploadSessionRequest { UserId = user.ToString(), SizeBytes = 1 }).ResponseAsync))
            .StatusCode.ShouldBe(Grpc.Core.StatusCode.ResourceExhausted);
    }

    [Fact]
    public async Task AdminEndpoints_RequireTheAdminRole()
    {
        using HttpClient user = factory.CreateClient().AsUser(Guid.NewGuid());
        (await user.GetAsync("/api/v1/admin/summary")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await user.PutAsJsonAsync($"/api/v1/admin/quotas/{Guid.NewGuid()}", new { limitBytes = 1 })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using HttpClient admin = factory.CreateClient().AsUser(Guid.NewGuid(), admin: true);
        (await admin.GetAsync("/api/v1/admin/summary")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PutAsJsonAsync($"/api/v1/admin/quotas/{Guid.NewGuid()}", new { limitBytes = -1 })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
