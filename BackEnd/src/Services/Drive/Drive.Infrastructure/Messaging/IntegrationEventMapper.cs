using Drive.Domain.Items;
using MyCloud.BuildingBlocks.Domain.Primitives;
using MyCloud.Contracts.Events.Drive;
using ContractKind = MyCloud.Contracts.Events.Drive.ItemKind;
using DomainKind = Drive.Domain.Items.ItemKind;

namespace Drive.Infrastructure.Messaging;

/// <summary>Domain event (nội bộ) → integration event (hợp đồng công khai giữa các service, docs/04 §3.2).</summary>
internal static class IntegrationEventMapper
{
    public static object? Map(IDomainEvent domainEvent, IAggregateRoot aggregate)
    {
        if (aggregate is not DriveItem item)
        {
            return null;
        }

        Guid by = item.ModifiedBy ?? item.CreatedBy;
        return domainEvent switch
        {
            ItemCreatedDomainEvent => new ItemCreated(
                item.Id,
                item.OwnerId,
                item.ParentId,
                item.AncestorIds,
                item.Kind == DomainKind.Folder ? ContractKind.Folder : ContractKind.File,
                item.Name,
                item.MimeType,
                item.SizeBytes,
                item.Revision,
                item.CreatedBy),
            ItemRenamedDomainEvent => new ItemRenamed(item.Id, item.OwnerId, item.Name, item.Revision, by),
            ItemMovedDomainEvent e => new ItemMoved(item.Id, item.OwnerId, e.OldParentId, item.ParentId!.Value, item.AncestorIds, item.Revision, by),
            ItemTrashedDomainEvent => new ItemTrashed(item.Id, item.OwnerId, item.Revision, item.TrashedBy ?? by),
            ItemRestoredDomainEvent => new ItemRestored(item.Id, item.OwnerId, item.ParentId!.Value, item.AncestorIds, item.Name, item.Revision, by),
            FileVersionsDeletedDomainEvent e => new FileVersionsDeleted(item.Id, item.OwnerId, e.VersionIds, e.BlobIds, item.Revision),
            FileVersionAddedDomainEvent e when item.Versions.SingleOrDefault(v => v.Id == e.VersionId) is { } v => new FileVersionAdded(
                item.Id, item.OwnerId, v.Id, v.VersionNo, v.BlobId, v.SizeBytes, v.MimeType, item.Revision, v.CreatedBy,
                v.Source.ToString().ToLowerInvariant(), v.RestoredFromVersionNo),
            _ => null,
        };
    }
}
