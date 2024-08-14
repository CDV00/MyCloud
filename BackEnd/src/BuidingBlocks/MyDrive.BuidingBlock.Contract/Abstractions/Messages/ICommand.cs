using MediatR;
using MyDrive.BuidingBlock.Contract.Abstractions.Shared;

namespace MyDrive.BuidingBlock.Contract.Abstractions.Messages;

public interface ICommand : IRequest<Result>
{
}
public interface ICommand<TResponse> : IRequest<Result<TResponse>>
{

}
