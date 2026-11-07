using MediatR;
using MyCloud.BuildingBlocks.Domain.Results;

namespace MyCloud.BuildingBlocks.Application.Messaging;

/// <summary>Marker cho mọi command — UnitOfWorkPipelineBehavior chỉ commit với command.</summary>
public interface IBaseCommand;

public interface ICommand : IRequest<Result>, IBaseCommand;

public interface ICommand<TResponse> : IRequest<Result<TResponse>>, IBaseCommand;

public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;

public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;
