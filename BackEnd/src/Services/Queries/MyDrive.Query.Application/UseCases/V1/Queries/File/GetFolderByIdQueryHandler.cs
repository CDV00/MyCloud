using MyDrive.BuidingBlock.Contract.Abstractions.Messages;
using MyDrive.BuidingBlock.Contract.Abstractions.Shared;
using MyDrive.BuidingBlock.Contract.Services.V1.File;
using MyDrive.Query.Domain.Abstractions.Repositories;
using MyDrive.Query.Domain.Entities;
using MyDrive.Query.Domain.Exceptions;

namespace MyDrive.Query.Application.UseCases.V1.Queries.File;

public sealed class GetFileByIdQueryHandler : IQueryHandler<BuidingBlock.Contract.Services.V1.File.Query.GetFileByIdQuery, Response.FileResponse>
{
    private readonly IMongoRepository<FileProjection> _repository;
    public GetFileByIdQueryHandler(IMongoRepository<FileProjection> repository)
    {
        _repository = repository;
    }

    public async Task<Result<Response.FileResponse>> Handle(BuidingBlock.Contract.Services.V1.File.Query.GetFileByIdQuery request, CancellationToken cancellationToken)
    {
        var file = await _repository.FindOneAsync(o=>o.DocumentId == request.Id)
            ?? throw new FileException.FileNotFoundException(request.Id);
        var result = new Response.FileResponse(file.DocumentId, file.Name, file.Type, file.Size, file.Description, file.FolderId);
        return Result.Success(result);
    }
}
