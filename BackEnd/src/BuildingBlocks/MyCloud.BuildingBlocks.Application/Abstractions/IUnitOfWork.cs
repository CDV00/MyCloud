namespace MyCloud.BuildingBlocks.Application.Abstractions;

public interface IUnitOfWork
{
    /// <summary>
    /// Lưu mọi thay đổi trong một transaction: dữ liệu + audit + integration event (outbox).
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
