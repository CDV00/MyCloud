using Drive.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using MyCloud.BuildingBlocks.Messaging;
using DriveFile = Drive.Domain.Entities.File;

namespace Drive.Infrastructure.Persistence;

public sealed class DriveDbContext(DbContextOptions<DriveDbContext> options) : DbContext(options)
{
    public DbSet<Folder> Folders => Set<Folder>();

    public DbSet<DriveFile> Files => Set<DriveFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DriveDbContext).Assembly);
        modelBuilder.AddMessagingTables(); // outbox/inbox của MassTransit
    }
}
