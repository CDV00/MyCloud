using MediatR;
using MyDrive.BuidingBlock.Contract.Abstractions.Shared;

namespace MyDrive.BuidingBlock.Contract.Abstractions.Messages;

public interface ICommandHandler<TCommand>:IRequestHandler<TCommand, Result>
    where TCommand : ICommand
{

}

public interface ICommandHandler<TCommand, TResponse>:IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>
{

}
