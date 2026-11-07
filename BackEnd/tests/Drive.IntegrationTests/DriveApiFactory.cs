using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Drive.IntegrationTests;

/// <summary>
/// Drive.Api thật + PostgreSQL thật (Testcontainers). RabbitMQ được thay bằng MassTransit test harness (in-memory)
/// nhưng outbox vẫn chạy thật: event đi DB → bảng outbox → bus.
/// </summary>
public sealed class DriveApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public string ContentRoot { get; } = Path.Combine(Path.GetTempPath(), "mycloud-drive-tests", Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        if (Directory.Exists(ContentRoot))
        {
            Directory.Delete(ContentRoot, recursive: true);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development"); // áp dụng migration lúc khởi động
        builder.UseSetting("ConnectionStrings:drivedb", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:rabbitmq", "amqp://guest:guest@localhost:5672");
        builder.UseSetting("FileStorage:RootPath", ContentRoot);

        builder.ConfigureTestServices(services =>
        {
            services.AddMassTransitTestHarness();

            // Không có Identity trong test: tin khoá của TestJwt thay vì tải JWKS qua HTTP.
            // Configure (đăng ký sau Program) chạy trước PostConfigure của JwtBearer ⇒ không tạo ConfigurationManager.
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                o.MetadataAddress = null!;
                o.TokenValidationParameters.IssuerSigningKey = TestJwt.Key;
            });
        });
    }
}
