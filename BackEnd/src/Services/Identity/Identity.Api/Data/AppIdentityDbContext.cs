using Identity.Api.Keys;
using Identity.Api.Tokens;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MyCloud.BuildingBlocks.Messaging;

namespace Identity.Api.Data;

public sealed class AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<SigningKey> SigningKeys => Set<SigningKey>();

    /// <summary>Key ring của ASP.NET Core Data Protection (mã hoá private key của khoá ký JWT).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(b =>
        {
            b.ToTable("users");
            b.Property(x => x.DisplayName).HasMaxLength(AppUser.DisplayNameMaxLength).IsRequired();
            b.Property(x => x.AvatarUrl).HasMaxLength(2048);
        });
        builder.Entity<IdentityRole<Guid>>().ToTable("roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");

        builder.Entity<RefreshToken>(b =>
        {
            b.ToTable("refresh_tokens");
            b.HasKey(x => x.Id);
            b.Property(x => x.TokenHash).HasMaxLength(RefreshToken.TokenHashLength).IsFixedLength().IsRequired();
            b.HasIndex(x => x.TokenHash).IsUnique();
            b.HasIndex(x => x.FamilyId);
            b.HasIndex(x => x.UserId);
            b.HasIndex(x => x.ExpiresAt);
            b.Property(x => x.RevokedReason).HasMaxLength(32);
            b.Property(x => x.CreatedByIp).HasMaxLength(64);
            b.Property(x => x.UserAgent).HasMaxLength(512);
            b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SigningKey>(b =>
        {
            b.ToTable("signing_keys");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasMaxLength(64);
            b.Property(x => x.Algorithm).HasMaxLength(16).IsRequired();
            b.Property(x => x.ProtectedPrivateKey).IsRequired();
            b.HasIndex(x => x.CreatedAt);
        });

        builder.Entity<DataProtectionKey>().ToTable("data_protection_keys");

        builder.AddMessagingTables(); // outbox cho UserRegistered, UserProfileChanged
    }
}

/// <summary>Chỉ dùng cho `dotnet ef migrations add`.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppIdentityDbContext>
{
    public AppIdentityDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseNpgsql("Host=localhost;Database=identity")
            .UseSnakeCaseNamingConvention()
            .Options);
}
