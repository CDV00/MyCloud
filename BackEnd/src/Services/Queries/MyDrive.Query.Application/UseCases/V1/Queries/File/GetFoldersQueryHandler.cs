using MyDrive.BuidingBlock.Contract.Abstractions.Messages;
using MyDrive.BuidingBlock.Contract.Abstractions.Shared;
using MyDrive.BuidingBlock.Contract.Services.V1.File;
using MyDrive.Query.Domain.Abstractions.Repositories;
using MyDrive.Query.Domain.Entities;
using MongoDB.Driver;

namespace MyDrive.Query.Application.UseCases.V1.Queries.File;

public sealed class GetFilesQueryHandler : IQueryHandler<BuidingBlock.Contract.Services.V1.File.Query.GetFilesQuery, List<Response.FileResponse>>
{
    private readonly IMongoRepository<FileProjection> _repository;
    public GetFilesQueryHandler(IMongoRepository<FileProjection> repository)
    {
        _repository = repository;
    }

    public async Task<Result<List<Response.FileResponse>>> Handle(BuidingBlock.Contract.Services.V1.File.Query.GetFilesQuery request, CancellationToken cancellationToken)
    {
        var files = await _repository.AsQueryable().ToListAsync();
        var result = new List<Response.FileResponse>();

        foreach (var item in files)
        {
            result.Add(new Response.FileResponse(item.DocumentId, item.Name, item.Type, item.Size, item.Description, item.FolderId));
        }
        return Result.Success(result);
    }
}
