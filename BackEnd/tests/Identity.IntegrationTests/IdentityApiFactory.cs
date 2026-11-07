using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Testcontainers.PostgreSql;

namespace Identity.IntegrationTests;

/// <summary>Identity.Api thật + PostgreSQL thật (Testcontainers); RabbitMQ thay bằng MassTransit test harness.</summary>
public sealed class IdentityApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Đường dẫn auth khi gọi thẳng Identity (không qua Gateway) — cookie phải khớp để client test gửi kèm.</summary>
    public const string AuthPath = "/api/v1/auth";

    public const string ServiceClientSecret = "test-drive-client-secret";

    /// <summary>Email được cấu hình là quản trị viên (Identity:AdminEmails).</summary>
    public const string AdminEmail = "root-admin@example.com";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development"); // áp dụng migration lúc khởi động
        builder.UseSetting("ConnectionStrings:identitydb", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:rabbitmq", "amqp://guest:guest@localhost:5672");
        builder.UseSetting("Tokens:CookiePath", AuthPath);
        builder.UseSetting("Tokens:RefreshReuseLeeway", "00:00:00"); // test phát hiện dùng lại token ngay lập tức
        builder.UseSetting("ServiceClients:Clients:drive:Secret", ServiceClientSecret);
        builder.UseSetting("Identity:AdminEmails:0", AdminEmail);

        builder.ConfigureTestServices(services => services.AddMassTransitTestHarness());
    }
}
