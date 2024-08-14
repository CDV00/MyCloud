using MassTransit;

namespace MyDrive.BuidingBlock.Contract.Abstractions.Messages;

[ExcludeFromTopology]
public interface IDomainEvent
{
    public Guid IdEvent { get; init; }
}