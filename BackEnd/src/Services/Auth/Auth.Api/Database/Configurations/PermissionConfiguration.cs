using Auth.Api.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DistributedSystem.Persistence.Configurations;

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable(nameof(Permission));

        builder.HasKey(x => new { x.RoleId, x.FunctionId, x.ActionId });

        builder.HasOne(o=>o.Role)
            .WithMany(o => o.Permissions)
            .HasForeignKey(o => o.RoleId).IsRequired();

        builder.HasOne(o=>o.Action)
            .WithMany(o => o.Permissions)
            .HasForeignKey(o => o.ActionId).IsRequired();

        builder.HasOne(o=>o.Function)
            .WithMany(o => o.Permissions)
            .HasForeignKey(o => o.FunctionId).IsRequired();


    }
}
