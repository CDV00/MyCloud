using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyDrive.Command.Domain.Entities;
using MyDrive.Command.Persistence.Constants;

namespace MyDrive.Command.Persistence.Configurations;

internal class FolderConfiguration : IEntityTypeConfiguration<Folder>
{
    public void Configure(EntityTypeBuilder<Folder> builder)
    {
        builder.ToTable(TableNames.Folder);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired(true);
        builder.Property(x => x.Description)
            .HasMaxLength(250).IsRequired(true);

        builder.HasMany(x => x.Folders).WithOne(o => o.Parent).HasForeignKey(o => o.ParentId);

    }
}
