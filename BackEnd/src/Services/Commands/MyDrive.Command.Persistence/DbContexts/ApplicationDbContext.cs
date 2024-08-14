using Microsoft.EntityFrameworkCore;
using MyDrive.Command.Domain.Entities;
using MyDrive.Command.Persistence.Outbox;
using File = MyDrive.Command.Domain.Entities.File;

namespace MyDrive.Command.Persistence.DbContexts;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder) =>
        builder.ApplyConfigurationsFromAssembly(AssemblyReference.Assembly);

    public DbSet<Folder> Folders { get; set; }
    public DbSet<File> Files { get; set; }
    public DbSet<OutboxMessage> OutboxMessages { get; set; }
}
