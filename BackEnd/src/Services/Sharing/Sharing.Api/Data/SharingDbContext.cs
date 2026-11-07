using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MyCloud.BuildingBlocks.Messaging;

namespace Sharing.Api.Data;

/// <summary>Vai trò, thứ tự tăng dần quyền: vai trò cao bao gồm mọi quyền của vai trò thấp hơn.</summary>
public enum Role : short
{
    None = 0,
    Viewer = 1,
    Commenter = 2,
    Editor = 3,
    Owner = 4,
}

/// <summary>Quyền trực tiếp của một người dùng trên một item; kế thừa xuống mọi con cháu.</summary>
public sealed class Permission
{
    public Guid Id { get; set; }

    public Guid ItemId { get; set; }

    /// <summary>Chủ sở hữu item (để xoá theo người dùng / thống kê; không dùng để quyết định quyền).</summary>
    public Guid OwnerId { get; set; }

    public Guid UserId { get; set; }

    public Role Role { get; set; }

    public Guid GrantedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Link chia sẻ. Chỉ lưu SHA-256 của token (token chỉ hiện cho người tạo một lần).</summary>
public sealed class ShareLink
{
    public Guid Id { get; set; }

    public Guid ItemId { get; set; }

    public Guid OwnerId { get; set; }

    public byte[] TokenHash { get; set; } = [];

    public Role Role { get; set; }

    public string? PasswordHash { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? DisabledAt { get; set; }
}

public sealed class SharingDbContext(DbContextOptions<SharingDbContext> options) : DbContext(options)
{
    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<ShareLink> Links => Set<ShareLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Permission>(b =>
        {
            b.ToTable("permissions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Role).HasConversion<short>();
            b.HasIndex(x => new { x.ItemId, x.UserId }).IsUnique();  // một quyền trực tiếp / người / item
            b.HasIndex(x => new { x.UserId, x.ItemId });             // kiểm tra quyền: user + tổ tiên
        });

        modelBuilder.Entity<ShareLink>(b =>
        {
            b.ToTable("share_links");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Role).HasConversion<short>();
            b.Property(x => x.PasswordHash).HasMaxLength(512);
            b.HasIndex(x => x.TokenHash).IsUnique();
            b.HasIndex(x => x.ItemId);
        });

        modelBuilder.AddMessagingTables(); // outbox (ItemShared...) + inbox (ItemsPurged)
    }
}

/// <summary>Chỉ dùng cho <c>dotnet ef migrations add</c>.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SharingDbContext>
{
    public SharingDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SharingDbContext>()
            .UseNpgsql("Host=localhost;Database=sharing")
            .UseSnakeCaseNamingConvention()
            .Options);
}
