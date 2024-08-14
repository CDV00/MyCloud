
namespace MyDrive.Command.Domain.Abstractions.Entities;

public interface IAuditableEntity
{
    DateTimeOffset CreatedOnUtc { get; set; }
    Guid CreatedBy { get; set; }

    DateTimeOffset? ModifiedOnUtc { get; set; }
    Guid? ModifiedBy { get; set; }
}