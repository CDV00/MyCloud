using Drive.Domain.Comments;
using Drive.Domain.Items;
using Drive.Domain.Stars;
using Drive.Domain.Uploads;
using Microsoft.EntityFrameworkCore;
using MyCloud.BuildingBlocks.Messaging;

namespace Drive.Infrastructure.Persistence;

public sealed class DriveDbContext(DbContextOptions<DriveDbContext> options) : DbContext(options)
{
    private readonly List<FormattableString> _afterSave = [];

    public DbSet<DriveItem> Items => Set<DriveItem>();

    public DbSet<FileVersion> FileVersions => Set<FileVersion>();

    public DbSet<PendingUpload> PendingUploads => Set<PendingUpload>();

    public DbSet<Star> Stars => Set<Star>();

    public DbSet<ItemComment> Comments => Set<ItemComment>();

    internal DbSet<ItemAccessRecord> AccessLog => Set<ItemAccessRecord>();

    /// <summary>Câu SQL hàng loạt (vd cập nhật path cả cây con) chạy ngay sau SaveChanges, cùng transaction.</summary>
    internal void ExecuteAfterSave(FormattableString sql) => _afterSave.Add(sql);

    internal IReadOnlyList<FormattableString> TakeAfterSave()
    {
        FormattableString[] pending = [.. _afterSave];
        _afterSave.Clear();
        return pending;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DriveDbContext).Assembly);
        modelBuilder.AddMessagingTables(); // outbox/inbox của MassTransit
    }
}
