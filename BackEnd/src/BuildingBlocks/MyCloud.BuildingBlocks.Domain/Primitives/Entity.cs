namespace MyCloud.BuildingBlocks.Domain.Primitives;

public abstract class Entity<TKey>
    where TKey : notnull
{
    public TKey Id { get; protected set; } = default!;
}
