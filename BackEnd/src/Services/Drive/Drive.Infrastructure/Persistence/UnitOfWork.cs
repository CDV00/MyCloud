using Drive.Infrastructure.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using MyCloud.BuildingBlocks.Application.Abstractions;
using MyCloud.BuildingBlocks.Domain.Primitives;

namespace Drive.Infrastructure.Persistence;

/// <summary>
/// Một lần SaveChanges = dữ liệu + audit + integration event.
/// IPublishEndpoint ở đây là bus outbox của MassTransit: Publish chỉ thêm bản ghi vào bảng outbox
/// của chính DbContext này, nên event được lưu cùng transaction với dữ liệu (thay outbox Quartz tự viết).
/// </summary>
internal sealed class UnitOfWork(
    DriveDbContext db,
    IPublishEndpoint publishEndpoint,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAudit();

        var aggregates = db.ChangeTracker.Entries<IAggregateRoot>().Select(e => e.Entity).ToList();
        foreach (IAggregateRoot aggregate in aggregates)
        {
            foreach (IDomainEvent domainEvent in aggregate.GetDomainEvents())
            {
                object? integrationEvent = IntegrationEventMapper.Map(domainEvent, aggregate);
                if (integrationEvent is not null)
                {
                    await publishEndpoint.Publish(integrationEvent, integrationEvent.GetType(), cancellationToken);
                }
            }

            aggregate.ClearDomainEvents();
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private void ApplyAudit()
    {
        // Bản cũ gán CreatedBy = Guid.NewGuid() (lỗi S2). Phase 1 bật JWT nên UserId sẽ luôn có giá trị.
        Guid userId = currentUser.UserId ?? Guid.Empty;
        DateTimeOffset now = timeProvider.GetUtcNow();

        foreach (var entry in db.ChangeTracker.Entries<IAuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedOnUtc = now;
                entry.Entity.CreatedBy = userId;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.ModifiedOnUtc = now;
                entry.Entity.ModifiedBy = userId;
            }
        }
    }
}
