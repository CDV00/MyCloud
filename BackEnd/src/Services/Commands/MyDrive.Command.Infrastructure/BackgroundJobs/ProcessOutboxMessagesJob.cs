using MyDrive.BuidingBlock.Contract.Abstractions.Messages;
//using MyDrive.BuidingBlock.Contract.Services.V1.Folder;
using FolderDomainEvent = MyDrive.BuidingBlock.Contract.Services.V1.Folder.DomainEvent;
using FileDomainEvent = MyDrive.BuidingBlock.Contract.Services.V1.File.DomainEvent;
using MyDrive.Command.Persistence.DbContexts;
using MyDrive.Command.Persistence.Outbox;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Quartz;

namespace MyDrive.Command.Infrastructure.BackgroundJobs;

[DisallowConcurrentExecution]
public class ProcessOutboxMessagesJob : IJob
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPublishEndpoint _publishEndpoint; // Maybe can use more Rebus library

    public ProcessOutboxMessagesJob(ApplicationDbContext dbContext, IPublishEndpoint publishEndpoint)
    {
        _dbContext = dbContext;
        _publishEndpoint = publishEndpoint;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        List<OutboxMessage> messages = await _dbContext
            .Set<OutboxMessage>()
            .Where(m => m.ProcessedOnUtc == null)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(20)
            .ToListAsync(context.CancellationToken);
        if(messages.Count == 0)
            return;
        foreach (OutboxMessage outboxMessage in messages)
        {
            IDomainEvent? domainEvent = JsonConvert
                .DeserializeObject<IDomainEvent>(
                    outboxMessage.Content,
                    new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.All
                    });

            if (domainEvent is null)
                continue;

            try
            {
                switch (domainEvent.GetType().Name)
                {
                    case nameof(FolderDomainEvent.FolderCreated):
                        await PublishEndpoint<FolderDomainEvent.FolderCreated>(outboxMessage, context);
                        break;

                    case nameof(FolderDomainEvent.FolderUpdated):
                        await PublishEndpoint<FolderDomainEvent.FolderUpdated>(outboxMessage, context);
                        break;

                    case nameof(FolderDomainEvent.FolderDeleted):
                        await PublishEndpoint<FolderDomainEvent.FolderDeleted>(outboxMessage, context);
                        break;

                    case nameof(FileDomainEvent.FileCreated):
                        await PublishEndpoint<FileDomainEvent.FileCreated>(outboxMessage, context);
                        break;

                    case nameof(FileDomainEvent.FileUpdated):
                        await PublishEndpoint<FileDomainEvent.FileUpdated>(outboxMessage, context);
                        break;

                    case nameof(FileDomainEvent.FileDeleted):
                        await PublishEndpoint<FileDomainEvent.FileDeleted>(outboxMessage, context);
                        break;


                    default:
                        break;
                }

                outboxMessage.ProcessedOnUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                outboxMessage.Error = ex.Message;
            }
        }

        await _dbContext.SaveChangesAsync();
    }

    private async Task PublishEndpoint<T>(OutboxMessage outboxMessage, IJobExecutionContext context)
    {
        T? dataEvent = JsonConvert.DeserializeObject<T>(
                                    outboxMessage.Content,
                                    new JsonSerializerSettings
                                    {
                                        TypeNameHandling = TypeNameHandling.All
                                    });
        await _publishEndpoint.Publish(dataEvent, context.CancellationToken);
    }
}