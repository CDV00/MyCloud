namespace MyCloud.Contracts.Events;

/// <summary>Sự kiện đã xảy ra, phát giữa các service qua RabbitMQ (qua outbox).</summary>
public abstract record IntegrationEvent
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();

    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    public int SchemaVersion { get; init; } = 1;
}
