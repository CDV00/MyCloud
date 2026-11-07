using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Gateway.Tests;

/// <summary>
/// Kiểm tra các chính sách chạy TRƯỚC khi chuyển tiếp (xác thực, rate limit, correlation id). Service phía sau
/// không chạy trong test nên request hợp lệ nhận 502/503 từ YARP — điều đó vẫn chứng minh Gateway đã cho đi qua.
/// </summary>
public sealed class GatewayTests(GatewayTests.Factory factory) : IClassFixture<GatewayTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("RateLimiting:LoginPermitsPerMinute", "3");
        }
    }

    [Theory]
    [InlineData("/api/drive/v1/folders")]
    [InlineData("/api/search/v1/folders")]
    [InlineData("/api/identity/v1/users/me")]
    public async Task ProtectedRoute_WithoutToken_Returns401_WithoutReachingService(string path)
    {
        using HttpClient client = factory.CreateClient();

        var response = await client.GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuthRoute_IsAnonymous()
    {
        using HttpClient client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/identity/v1/auth/refresh", new { });

        // Được chuyển tiếp (Identity không chạy trong test ⇒ lỗi upstream), không bị chặn 401 ở Gateway.
        response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
        ((int)response.StatusCode).ShouldBeGreaterThanOrEqualTo(500);
    }

    [Fact]
    public async Task Login_IsRateLimitedPerIp()
    {
        using HttpClient client = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();

        for (int i = 0; i < 4; i++)
        {
            var response = await client.PostAsJsonAsync("/api/identity/v1/auth/login", new { email = "a@b.c", password = "x" });
            statuses.Add(response.StatusCode);
        }

        statuses.Take(3).ShouldAllBe(s => s != HttpStatusCode.TooManyRequests);
        statuses[3].ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task CorrelationId_IsGenerated_OrEchoedWhenValid()
    {
        using HttpClient client = factory.CreateClient();

        var generated = await client.GetAsync("/api/drive/v1/folders");
        generated.Headers.GetValues("X-Correlation-Id").Single().ShouldNotBeNullOrWhiteSpace();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/drive/v1/folders");
        request.Headers.Add("X-Correlation-Id", "client-trace-123");
        var echoed = await client.SendAsync(request);
        echoed.Headers.GetValues("X-Correlation-Id").Single().ShouldBe("client-trace-123");

        using var bad = new HttpRequestMessage(HttpMethod.Get, "/api/drive/v1/folders");
        bad.Headers.TryAddWithoutValidation("X-Correlation-Id", "<script>");
        var replaced = await client.SendAsync(bad);
        replaced.Headers.GetValues("X-Correlation-Id").Single().ShouldNotBe("<script>");
    }

    [Fact]
    public async Task Health_IsAnonymous()
    {
        using HttpClient client = factory.CreateClient();

        (await client.GetAsync("/health/live")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
