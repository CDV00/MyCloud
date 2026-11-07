using Drive.Application.Abstractions;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyCloud.Testing;
using Testcontainers.PostgreSql;

namespace Drive.IntegrationTests;

/// <summary>
/// Drive.Api thật + PostgreSQL thật (Testcontainers). RabbitMQ thay bằng MassTransit test harness (outbox vẫn chạy thật),
/// Storage thay bằng <see cref="FakeStorageClient"/>, JWT ký bằng khoá test.
/// </summary>
public sealed class DriveApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public FakeStorageClient Storage { get; } = new();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development"); // áp dụng migration lúc khởi động
        builder.UseSetting("ConnectionStrings:drivedb", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:rabbitmq", "amqp://guest:guest@localhost:5672");

        builder.ConfigureTestServices(services =>
        {
            services.AddMassTransitTestHarness();
            services.RemoveAll<IStorageClient>();
            services.AddSingleton<IStorageClient>(Storage);

            services.TrustTestJwt();
        });
    }
}
