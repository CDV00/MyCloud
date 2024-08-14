using MediatR;
using MyDrive.BuidingBlock.Contract.Abstractions.Shared;

namespace MyDrive.BuidingBlock.Contract.Abstractions.Messages;

public interface IQueryHandler<TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>
{ }