using Drive.Domain.Items;
using Drive.Infrastructure.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using MyCloud.BuildingBlocks.Application.Abstractions;
using MyCloud.BuildingBlocks.Domain.Primitives;
using Npgsql;

namespace Drive.Infrastructure.Persistence;

/// <summary>
/// Một lần lưu = dữ liệu + audit + integration event (bus outbox của MassTransit: Publish chỉ ghi vào bảng outbox của
/// DbContext này) + các câu SQL hàng loạt đi kèm (cập nhật path cây con) — tất cả trong MỘT transaction.
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
        await PublishDomainEventsAsync(cancellationToken);

        IReadOnlyList<FormattableString> afterSave = db.TakeAfterSave();
        try
        {
            if (afterSave.Count == 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            // Aspire bật retry cho Npgsql ⇒ transaction tự mở phải chạy trong execution strategy.
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                foreach (FormattableString sql in afterSave)
                {
                    await db.Database.ExecuteSqlAsync(sql, cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DriveException.ConcurrentModification();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: DriveItemConfiguration.UniqueNameIndex,
        })
        {
            // Hai thao tác đồng thời cùng lấy một tên — kiểm tra trước đó đều qua, ràng buộc DB chặn cái sau.
            throw new DriveException.NameConflict(string.Empty);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: DriveItemConfiguration.UniqueRootIndex,
        })
        {
            db.ChangeTracker.Clear();
            throw new DriveException.RootCreatedConcurrently();
        }
    }

    private async Task PublishDomainEventsAsync(CancellationToken cancellationToken)
    {
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
    }

    private void ApplyAudit()
    {
        // Consumer (không có người dùng) ghi lại người gây ra thay đổi trong payload event; ở đây dùng chủ sở hữu/Empty.
        Guid userId = currentUser.UserId ?? Guid.Empty;
        DateTimeOffset now = timeProvider.GetUtcNow();

        foreach (var entry in db.ChangeTracker.Entries<IAuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedOnUtc = now;
                entry.Entity.CreatedBy = userId == Guid.Empty && entry.Entity is DriveItem item ? item.OwnerId : userId;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.ModifiedOnUtc = now;
                entry.Entity.ModifiedBy = userId;
            }
        }
    }
}
