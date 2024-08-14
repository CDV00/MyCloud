using MediatR;
using MyDrive.BuidingBlock.Contract.Abstractions.Shared;

namespace MyDrive.BuidingBlock.Contract.Abstractions.Messages;

public interface IQuery<TResponse> : IRequest<Result<TResponse>>
{ }