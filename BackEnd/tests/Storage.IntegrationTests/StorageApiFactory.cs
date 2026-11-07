using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Grpc.Core;
using Grpc.Net.Client;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using MyCloud.Contracts.Storage.V1;
using MyCloud.Testing;
using Testcontainers.PostgreSql;

namespace Storage.IntegrationTests;

/// <summary>
/// Storage.Api thật + PostgreSQL + MinIO thật (Testcontainers). Image MinIO giống AppHost: bản build từ source của
/// Chainguard, ghim digest (ADR-011). RabbitMQ thay bằng MassTransit test harness.
/// </summary>
public sealed class StorageApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const long QuotaBytes = 12L * 1024 * 1024;
    public const long PartSizeBytes = 5L * 1024 * 1024; // nhỏ nhất S3 cho phép ⇒ file 6 MB thành 2 part

    private const string MinioImage = "cgr.dev/chainguard/minio@sha256:aea6c163749e1caca37f907976dcf337177ddf5f558808f2aa591618fd3a6eef";
    private const string AccessKey = "mycloud-test";
    private const string SecretKey = "mycloud-test-secret";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly IContainer _minio = new ContainerBuilder(MinioImage)
        .WithCommand("server", "/data")
        .WithEnvironment("MINIO_ROOT_USER", AccessKey)
        .WithEnvironment("MINIO_ROOT_PASSWORD", SecretKey)
        .WithPortBinding(9000, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(9000).ForPath("/minio/health/live")))
        .Build();

    public Uri MinioEndpoint => new($"http://{_minio.Hostname}:{_minio.GetMappedPublicPort(9000)}");

    public async Task InitializeAsync() => await Task.WhenAll(_postgres.StartAsync(), _minio.StartAsync());

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await _minio.DisposeAsync();
    }

    /// <summary>Client gRPC qua TestServer (HTTP/2 trong bộ nhớ), gửi JWT của <paramref name="userId"/>.</summary>
    public StorageService.StorageServiceClient GrpcClientFor(Guid userId)
    {
        GrpcChannel channel = GrpcChannel.ForAddress(Server.BaseAddress, new GrpcChannelOptions
        {
            HttpHandler = Server.CreateHandler(),
            Credentials = ChannelCredentials.Create(
                ChannelCredentials.Insecure,
                CallCredentials.FromInterceptor((_, metadata) =>
                {
                    metadata.Add("Authorization", $"Bearer {TestJwt.CreateToken(userId)}");
                    return Task.CompletedTask;
                })),
            UnsafeUseInsecureChannelCallCredentials = true,
        });
        return new StorageService.StorageServiceClient(channel);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development"); // áp dụng migration lúc khởi động
        builder.UseSetting("ConnectionStrings:storagedb", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:rabbitmq", "amqp://guest:guest@localhost:5672");
        builder.UseSetting("ConnectionStrings:minio", $"Endpoint={MinioEndpoint};AccessKey={AccessKey};SecretKey={SecretKey}");
        builder.UseSetting("Storage:Bucket", "mycloud-test");
        builder.UseSetting("Storage:PartSizeBytes", PartSizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Storage:DefaultQuotaBytes", QuotaBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Storage:MaintenanceInterval", "1.00:00:00"); // test tự gọi RunOnceAsync

        builder.ConfigureTestServices(services =>
        {
            services.AddMassTransitTestHarness();
            services.TrustTestJwt();
        });
    }
}
