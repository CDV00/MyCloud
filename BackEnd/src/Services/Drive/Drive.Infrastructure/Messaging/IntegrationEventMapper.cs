using Drive.Domain.Entities;
using Drive.Domain.Events;
using MyCloud.BuildingBlocks.Domain.Primitives;
using MyCloud.Contracts.Events.Drive;
using DriveFile = Drive.Domain.Entities.File;

namespace Drive.Infrastructure.Messaging;

/// <summary>Domain event (nội bộ) → integration event (hợp đồng công khai giữa các service).</summary>
internal static class IntegrationEventMapper
{
    public static object? Map(IDomainEvent domainEvent, IAggregateRoot aggregate) => (domainEvent, aggregate) switch
    {
        (FolderCreatedDomainEvent, Folder f) =>
            new FolderCreated(f.Id, f.OwnerId, f.Name, f.Description, f.ParentId, f.CreatedBy),
        (FolderUpdatedDomainEvent, Folder f) =>
            new FolderUpdated(f.Id, f.OwnerId, f.Name, f.Description, f.ParentId, f.ModifiedBy ?? f.CreatedBy),
        (FolderDeletedDomainEvent e, _) =>
            new FolderDeleted(e.FolderId),

        (FileCreatedDomainEvent, DriveFile f) =>
            new FileCreated(f.Id, f.OwnerId, f.Name, f.ContentType, f.SizeBytes, f.Description, f.FolderId, f.CreatedBy),
        (FileUpdatedDomainEvent, DriveFile f) =>
            new FileUpdated(f.Id, f.OwnerId, f.Name, f.ContentType, f.SizeBytes, f.Description, f.FolderId, f.ModifiedBy ?? f.CreatedBy),
        (FileDeletedDomainEvent e, _) =>
            new FileDeleted(e.FileId),

        _ => null,
    };
}
