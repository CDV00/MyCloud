using MyDrive.Query.Domain.Abstractions;
using MyDrive.Query.Domain.Attributes;
using MyDrive.Query.Domain.Constants;

namespace MyDrive.Query.Domain.Entities;

[BsonCollection(CollectionNames.Folder)]
public class FolderProjection : Document
{
    public string Name { get; set; }
    public string Description { get; set; }
    public Guid? ParentId { get; set; }
}
