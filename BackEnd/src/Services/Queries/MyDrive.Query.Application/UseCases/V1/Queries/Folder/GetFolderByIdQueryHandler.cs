using MyDrive.BuidingBlock.Contract.Abstractions.Messages;
using MyDrive.BuidingBlock.Contract.Abstractions.Shared;
using MyDrive.BuidingBlock.Contract.Services.V1.Folder;
using MyDrive.Query.Domain.Abstractions.Repositories;
using MyDrive.Query.Domain.Entities;
using MyDrive.Query.Domain.Exceptions;

namespace MyDrive.Query.Application.UseCases.V1.Queries.Folder;

public sealed class GetFolderByIdQueryHandler : IQueryHandler<BuidingBlock.Contract.Services.V1.Folder.Query.GetFolderByIdQuery, Response.FolderResponse>
{
    private readonly IMongoRepository<FolderProjection> _repository;

    public GetFolderByIdQueryHandler(IMongoRepository<FolderProjection> repository)
    {
        _repository = repository;
    }

    public async Task<Result<Response.FolderResponse>> Handle(BuidingBlock.Contract.Services.V1.Folder.Query.GetFolderByIdQuery request, CancellationToken cancellationToken)
    {
        var folder = await _repository.FindOneAsync(o=>o.DocumentId == request.Id)
            ?? throw new FolderException.FolderNotFoundException(request.Id);
        var result = new Response.FolderResponse(folder.DocumentId, folder.Name, folder.Description, folder.ParentId);
        return Result.Success(result);
    }
}
