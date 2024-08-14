using MyDrive.Command.Domain.Abstractions.Aggregates;
using MyDrive.Command.Domain.Abstractions.Entities;
using static MyDrive.Command.Domain.Exceptions.FolderException;

namespace MyDrive.Command.Domain.Entities;

public class File : AggregateRoot<Guid>, IAuditableEntity
{
    public string Name { get; private set; }
    public string StoredName { get; private set; }
    public string Type { get; private set; }
    public decimal Size { get; private set; }
    public string Description { get; private set; }
    public Guid FolderId { get; set; }
    public virtual Folder Folder { get; set; }
    public DateTimeOffset CreatedOnUtc { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset? ModifiedOnUtc { get; set; }
    public Guid? ModifiedBy { get; set; }

    public File() { }

    public File(Guid id, string name, string storedName, string type, decimal size, string description, Guid folderId)
    {
        Id = id;
        Name = name;
        StoredName = storedName;
        Type = type;
        Size = size;
        Description = description;
        FolderId = folderId;
    }

    public static File CreateFile(Guid id, string name, string storedName, string type, decimal size, string description, Guid folderId)
    {
        if (name.Length > 50)
            throw new FolderFieldException(nameof(Name));
        var file = new File(id, name, storedName, type, size, description, folderId);

        file.RaiseDomainEvent(new BuidingBlock.Contract.Services.V1.File.DomainEvent.FileCreated(Guid.NewGuid(), file.Id,
            file.Name,
            file.Type,
            file.Size,
            file.Description,
            file.StoredName,
            file.FolderId
            ));

        return file;
    }

    public void Update(string name, string storedName, string type, decimal size, string description, Guid folderId)
    {
        if (name.Length > 50)
            throw new FolderFieldException(nameof(Name));

        Name = name;
        StoredName = storedName;
        Type = type;
        Size = size;
        Description = description;
        FolderId = folderId;

        RaiseDomainEvent(new BuidingBlock.Contract.Services.V1.File.DomainEvent.FileUpdated(Guid.NewGuid(), Id, name, type, size, description, storedName, folderId));
    }

    public void Delete()
        => RaiseDomainEvent(new BuidingBlock.Contract.Services.V1.File.DomainEvent.FileDeleted(Guid.NewGuid(), Id));
}
