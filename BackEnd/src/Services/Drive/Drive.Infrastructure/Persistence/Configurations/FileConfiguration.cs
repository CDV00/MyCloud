using Drive.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DriveFile = Drive.Domain.Entities.File;

namespace Drive.Infrastructure.Persistence.Configurations;

internal sealed class FileConfiguration : IEntityTypeConfiguration<DriveFile>
{
    public void Configure(EntityTypeBuilder<DriveFile> builder)
    {
        builder.ToTable("files");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(DriveFile.NameMaxLength).IsRequired();
        builder.Property(x => x.StorageKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(DriveFile.DescriptionMaxLength).IsRequired();

        builder.HasOne<Folder>()
            .WithMany()
            .HasForeignKey(x => x.FolderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.FolderId);
    }
}
