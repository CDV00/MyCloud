using MyDrive.Query.Domain.Abstractions;
using MyDrive.Query.Domain.Attributes;
using MyDrive.Query.Domain.Constants;

namespace MyDrive.Query.Domain.Entities;

[BsonCollection(CollectionNames.Event)]
public class EventProjection : Document
{
    public Guid EventId { get; set; }
    public string Name { get; set; }
    public string Type { get; set; }
}
