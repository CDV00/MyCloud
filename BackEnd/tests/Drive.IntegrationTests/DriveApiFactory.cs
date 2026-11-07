using Drive.Api.Endpoints;
using Drive.Application.Abstractions;
using Grpc.Net.ClientFactory;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyCloud.Testing;
using Npgsql;
using Sharing.Api.Grpc;
using Testcontainers.PostgreSql;

namespace Drive.IntegrationTests;

/// <summary>
/// Drive.Api thật + <b>Sharing.Api thật</b> (gRPC qua TestServer, cùng container PostgreSQL, database riêng) — để ma
/// trận quyền chạy qua đúng đường đi production (ADR-014). RabbitMQ thay bằng MassTransit test harness (outbox vẫn
/// chạy thật), Storage thay bằng <see cref="FakeStorageClient"/>, Identity thay bằng <see cref="FakeUserDirectory"/>,
/// JWT người dùng và token service ký bằng khoá test.
/// </summary>
public sealed class DriveApiFactory : WebApplicationFactory<ItemEndpoints>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public FakeStorageClient Storage { get; } = new();

    public SharingApiFactory Sharing { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        Sharing = new SharingApiFactory(Database("sharing"));
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await Sharing.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development"); // áp dụng migration lúc khởi động
        builder.UseSetting("ConnectionStrings:drivedb", Database("drive"));
        builder.UseSetting("ConnectionStrings:rabbitmq", "amqp://guest:guest@localhost:5672");

        builder.ConfigureTestServices(services =>
        {
            services.AddMassTransitTestHarness();
            services.RemoveAll<IStorageClient>();
            services.AddSingleton<IStorageClient>(Storage);
            services.RemoveAll<IUserDirectory>();
            services.AddSingleton<IUserDirectory, FakeUserDirectory>();

            // Sharing: gRPC tới TestServer của SharingApiFactory, vẫn qua ServiceTokenHandler (token service test).
            const string client = "SharingServiceClient"; // tên client gRPC = tên kiểu (Drive.Infrastructure)
            services.Configure<GrpcClientFactoryOptions>(client, o => o.Address = Sharing.Server.BaseAddress);
            services.AddHttpClient(client).ConfigurePrimaryHttpMessageHandler(() => Sharing.Server.CreateHandler());
            services.UseTestServiceToken();

            services.TrustTestJwt();
        });
    }

    private string Database(string name) =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = name }.ConnectionString;
}

/// <summary>Sharing.Api thật trong cùng tiến trình test.</summary>
public sealed class SharingApiFactory(string connectionString) : WebApplicationFactory<SharingGrpcService>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:sharingdb", connectionString);
        builder.UseSetting("ConnectionStrings:rabbitmq", "amqp://guest:guest@localhost:5672");
        builder.ConfigureTestServices(services =>
        {
            services.AddMassTransitTestHarness();
            services.TrustTestJwt();
        });
    }
}

/// <summary>
/// Identity giả: mọi người dùng test đều "đã đăng ký" với email <c>{id:N}@test.local</c> (đúng claim email của
/// <see cref="TestJwt"/>), nên chia sẻ theo email cho bất kỳ user test nào cũng được.
/// </summary>
public sealed class FakeUserDirectory : IUserDirectory
{
    public static string EmailOf(Guid userId) => $"{userId:N}@test.local";

    public Task<UserInfo?> FindByEmailAsync(string email, CancellationToken ct)
    {
        string local = email.Split('@')[0];
        return Task.FromResult(
            email.EndsWith("@test.local", StringComparison.OrdinalIgnoreCase) && Guid.TryParseExact(local, "N", out Guid id)
                ? Info(id)
                : null);
    }

    public Task<IReadOnlyDictionary<Guid, UserInfo>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<Guid, UserInfo>>(ids.ToDictionary(id => id, id => Info(id)!));

    private static UserInfo? Info(Guid id) => new(id, EmailOf(id), $"User {id.ToString()[..4]}", null);
}
