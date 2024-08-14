using MongoDB.Bson;

namespace MyDrive.Query.Domain.Abstractions;

public abstract class Document : IDocument
{
    public ObjectId Id { get; set; }

    public Guid DocumentId { get; set; } // Id cua SourceMessage: ProductID, CustomerID, OrderID
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedOnUtc => Id.CreationTime;
    public Guid ModifiedBy { get; set; }
    public DateTimeOffset? ModifiedOnUtc { get; set; }
}
