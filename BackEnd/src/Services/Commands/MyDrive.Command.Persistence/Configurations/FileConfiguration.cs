using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyDrive.Command.Domain.Entities;
using MyDrive.Command.Persistence.Constants;
using File = MyDrive.Command.Domain.Entities.File;

namespace MyDrive.Command.Persistence.Configurations;

internal class FileConfiguration : IEntityTypeConfiguration<File>
{
    public void Configure(EntityTypeBuilder<File> builder)
    {
        builder.ToTable(TableNames.File);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired(true);
        builder.Property(x => x.StoredName).HasMaxLength(200).IsRequired(true);
        builder.Property(x => x.Description)
            .HasMaxLength(250).IsRequired(true);

        builder.HasOne<Folder>(o => o.Folder).WithMany(o=>o.Files).HasForeignKey(o=>o.FolderId);
    }
}
