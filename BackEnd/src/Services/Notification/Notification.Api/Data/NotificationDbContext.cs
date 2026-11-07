using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MyCloud.BuildingBlocks.Messaging;

namespace Notification.Api.Data;

/// <summary>Bản sao thông tin liên hệ từ Identity (event) — gửi email không phải gọi Identity.</summary>
public sealed class Contact
{
    public Guid UserId { get; set; }

    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Thông báo trong app. <see cref="Payload"/> là JSON tuỳ loại (item, người chia sẻ, phần trăm dung lượng...).</summary>
public sealed class UserNotification
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>item_shared | quota | welcome.</summary>
    public string Type { get; set; } = string.Empty;

    public string Payload { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReadAt { get; set; }
}

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<Contact> Contacts => Set<Contact>();

    public DbSet<UserNotification> Notifications => Set<UserNotification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Contact>(b =>
        {
            b.ToTable("user_contacts");
            b.HasKey(x => x.UserId);
            b.Property(x => x.UserId).ValueGeneratedNever();
            b.Property(x => x.Email).HasMaxLength(256).IsRequired();
            b.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<UserNotification>(b =>
        {
            b.ToTable("notifications");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Type).HasMaxLength(32).IsRequired();
            b.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            b.HasIndex(x => new { x.UserId, x.CreatedAt });
            b.HasIndex(x => x.UserId).HasFilter("read_at IS NULL").HasDatabaseName("ix_notifications_unread");
        });

        modelBuilder.AddMessagingTables();
    }
}

/// <summary>Chỉ dùng cho <c>dotnet ef migrations add</c>.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<NotificationDbContext>()
            .UseNpgsql("Host=localhost;Database=notification")
            .UseSnakeCaseNamingConvention()
            .Options);
}
