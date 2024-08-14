using Auth.Api.Database.Asbtractions;

namespace Auth.Api.Database;

public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _dbContext;

    public UnitOfWork(ApplicationDbContext dbContext)
        => _dbContext = dbContext;

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        //ConvertDomainEventsToOutboxMessages();
        //UpdateAuditableEntities();
        await _dbContext.SaveChangesAsync();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    => await _dbContext.DisposeAsync();

}
