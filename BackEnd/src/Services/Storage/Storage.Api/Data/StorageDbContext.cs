using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MyCloud.BuildingBlocks.Messaging;

namespace Storage.Api.Data;

public sealed class StorageDbContext(DbContextOptions<StorageDbContext> options) : DbContext(options)
{
    public DbSet<Blob> Blobs => Set<Blob>();

    public DbSet<Quota> Quotas => Set<Quota>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Blob>(b =>
        {
            b.ToTable("blobs");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Bucket).HasMaxLength(63).IsRequired();
            b.Property(x => x.ObjectKey).HasMaxLength(200).IsRequired();
            b.Property(x => x.MimeType).HasMaxLength(255).IsRequired();
            b.Property(x => x.S3UploadId).HasMaxLength(1024);
            b.Property(x => x.Status).HasConversion<short>();
            b.HasIndex(x => new { x.Status, x.ExpiresAt });   // job hết hạn upload
            b.HasIndex(x => new { x.Status, x.UploadedAt });  // job GC blob không được claim
            b.HasIndex(x => x.OwnerId);
        });

        modelBuilder.Entity<Quota>(b =>
        {
            b.ToTable("quotas", t => t.HasCheckConstraint("ck_quotas_non_negative", "used_bytes >= 0 AND reserved_bytes >= 0"));
            b.HasKey(x => x.UserId);
            b.Property(x => x.UserId).ValueGeneratedNever();
        });

        modelBuilder.AddMessagingTables(); // inbox cho consumer (UserRegistered, FileVersionAdded)
    }
}

/// <summary>Chỉ dùng cho <c>dotnet ef migrations add</c>.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<StorageDbContext>
{
    public StorageDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<StorageDbContext>()
            .UseNpgsql("Host=localhost;Database=storage")
            .UseSnakeCaseNamingConvention()
            .Options);
}
