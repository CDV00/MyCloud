using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using Auth.Api.Database.Models;

namespace Auth.Api.Persistence.Configurations;

internal sealed class ActionInFunctionConfiguration : IEntityTypeConfiguration<ActionInFunction>
{
    public void Configure(EntityTypeBuilder<ActionInFunction> builder)
    {
        builder.ToTable(nameof(ActionInFunction));

        builder.HasKey(x => new { x.ActionId, x.FunctionId });

        builder.HasOne(o => o.Action)
            .WithMany(o => o.ActionInFunctions)
            .HasForeignKey(o => o.ActionId);

        builder.HasOne(o => o.Function)
            .WithMany(o => o.ActionInFunctions)
            .HasForeignKey(o => o.FunctionId);

    }
}
