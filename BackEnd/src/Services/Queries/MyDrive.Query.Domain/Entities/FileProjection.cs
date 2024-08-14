using MyDrive.Query.Domain.Abstractions;
using MyDrive.Query.Domain.Attributes;
using MyDrive.Query.Domain.Constants;

namespace MyDrive.Query.Domain.Entities;

[BsonCollection(CollectionNames.File)]
public class FileProjection : Document
{
    public string Name { get; set; }
    public string StoredName { get; set; }
    public string Type { get; set; }
    public decimal Size { get; set; }
    public string Description { get; set; }
    public Guid FolderId { get; set; }
}