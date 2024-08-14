//using Microsoft.EntityFrameworkCore;
using MyDrive.BuidingBlock.Contract.Abstractions.Messages;
using MyDrive.BuidingBlock.Contract.Abstractions.Shared;
using MyDrive.BuidingBlock.Contract.Services.V1.Folder;
using MyDrive.Query.Domain.Abstractions.Repositories;
using MyDrive.Query.Domain.Entities;
using MongoDB.Driver;

namespace MyDrive.Query.Application.UseCases.V1.Queries.Folder;

public sealed class GetFoldersQueryHandler : IQueryHandler<BuidingBlock.Contract.Services.V1.Folder.Query.GetFoldersQuery, List<Response.FolderResponse>>
{
    private readonly IMongoRepository<FolderProjection> _repository;
    public GetFoldersQueryHandler(IMongoRepository<FolderProjection> repository)
    {
        _repository = repository;
    }
    public async Task<Result<List<Response.FolderResponse>>> Handle(BuidingBlock.Contract.Services.V1.Folder.Query.GetFoldersQuery request, CancellationToken cancellationToken)
    {
        var folders = await _repository.AsQueryable().ToListAsync();
        var result = new List<Response.FolderResponse>();

        foreach (var item in folders)
        {
            result.Add(new Response.FolderResponse(item.DocumentId, item.Name, item.Description, item.ParentId));
        }
        return Result.Success(result);
    }
}
