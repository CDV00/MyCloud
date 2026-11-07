using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;

namespace MyCloud.BuildingBlocks.Messaging;

/// <summary>
/// Cấu hình MassTransit + RabbitMQ chuẩn cho mọi service. Toàn bộ chi tiết thư viện nằm ở đây
/// để có thể thay MassTransit (ADR-008) mà không phải sửa từng service.
/// </summary>
public static class MessagingExtensions
{
    /// <summary>Tên connection string RabbitMQ (Aspire inject vào <c>ConnectionStrings:rabbitmq</c>).</summary>
    public const string ConnectionName = "rabbitmq";

    public static IHostApplicationBuilder AddMessaging(
        this IHostApplicationBuilder builder,
        Action<IBusRegistrationConfigurator>? configure = null)
    {
        builder.Services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();
            configure?.Invoke(x);

            x.UsingRabbitMq((context, cfg) =>
            {
                string connectionString = builder.Configuration.GetConnectionString(ConnectionName)
                    ?? throw new InvalidOperationException($"Connection string '{ConnectionName}' is not configured.");
                cfg.Host(new Uri(connectionString));

                // Retry ngắn trong bộ nhớ cho lỗi tạm thời. Lỗi cuối cùng → queue "<endpoint>_error".
                cfg.UseMessageRetry(r =>
                {
                    r.Exponential(5, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(500));
                    r.Ignore<ArgumentException>();
                });

                cfg.ConfigureEndpoints(context);
            });
        });

        builder.Services.AddOpenTelemetry().WithTracing(t => t.AddSource("MassTransit"));
        return builder;
    }

    /// <summary>
    /// Transactional outbox + inbox trên DbContext của service (PostgreSQL):
    /// publish trong handler được ghi vào bảng outbox cùng transaction với dữ liệu,
    /// consumer được khử trùng lặp bằng bảng inbox.
    /// DbContext phải gọi <see cref="AddMessagingTables"/> trong OnModelCreating.
    /// </summary>
    public static void AddPostgresOutbox<TDbContext>(this IBusRegistrationConfigurator x)
        where TDbContext : DbContext
    {
        x.AddEntityFrameworkOutbox<TDbContext>(o =>
        {
            o.UsePostgres();
            o.UseBusOutbox();
            o.QueryDelay = TimeSpan.FromSeconds(1);
            o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);
        });

        x.AddConfigureEndpointsCallback((context, _, cfg) => cfg.UseEntityFrameworkOutbox<TDbContext>(context));
    }

    public static void AddMessagingTables(this ModelBuilder modelBuilder)
    {
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
