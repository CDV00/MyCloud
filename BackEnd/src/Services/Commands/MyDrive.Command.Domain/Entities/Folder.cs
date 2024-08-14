using MyDrive.Command.Domain.Abstractions.Aggregates;
using MyDrive.Command.Domain.Abstractions.Entities;
using static MyDrive.Command.Domain.Exceptions.FolderException;

namespace MyDrive.Command.Domain.Entities;

public class Folder : AggregateRoot<Guid>, IAuditableEntity
{
    public string Name {  get; private set; }
    public string Description { get; private set; }
    public DateTimeOffset CreatedOnUtc { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset? ModifiedOnUtc { get; set; }
    public Guid? ModifiedBy { get; set; }
    public Guid? ParentId { get; set; }
    public virtual Folder? Parent { get; set; }
    public virtual ICollection<Folder>? Folders { get; set; }
    public virtual ICollection<File>? Files { get; set; }

    public Folder() { }

    public Folder(Guid id, string name, string description, Guid? parentId)
    {
        Id = id;
        Name = name;
        Description = description;
        ParentId = parentId;
    }

    public static Folder CreateFolder(Guid id, string name, string description, Guid? parentId)
    {
        if (name.Length > 50)
            throw new FolderFieldException(nameof(Name));

        var folder = new Folder(id, name, description, parentId);

        folder.RaiseDomainEvent(new MyDrive.BuidingBlock.Contract.Services.V1.Folder.DomainEvent.FolderCreated(Guid.NewGuid(), folder.Id,
            folder.Name,
            folder.Description,
            parentId
            ));

        return folder;
    }
    public void Update(string name, string description, Guid? parentId)
    {
        if (name.Length > 50)
            throw new FolderFieldException(nameof(Name));

        Name = name;
        Description = description;
        ParentId = parentId;

        RaiseDomainEvent(new BuidingBlock.Contract.Services.V1.Folder.DomainEvent.FolderUpdated(Guid.NewGuid(), Id, name, description, parentId));
    }

    public void Delete()
        => RaiseDomainEvent(new BuidingBlock.Contract.Services.V1.Folder.DomainEvent.FolderDeleted(Guid.NewGuid(), Id));
}
