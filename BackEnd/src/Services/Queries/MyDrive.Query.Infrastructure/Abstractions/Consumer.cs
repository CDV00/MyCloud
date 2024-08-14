using MassTransit;
using MediatR;
using MyDrive.BuidingBlock.Contract.Abstractions.Messages;
using MyDrive.Query.Domain.Abstractions.Repositories;
using MyDrive.Query.Domain.Entities;

namespace MyDrive.Query.Infrastructure.Abstractions;

public abstract class Consumer<TMessage> : IConsumer<TMessage>
    where TMessage : class, IDomainEvent
{
    private readonly ISender Sender;
    private readonly IMongoRepository<EventProjection> _eventRepository;
    protected Consumer(ISender sender, IMongoRepository<EventProjection> eventRepository)
    {
        Sender = sender;
        _eventRepository = eventRepository;
    }
    public async Task Consume(ConsumeContext<TMessage> context)
    {
        // Find by EventId
        // => If Existed => Ignore
        // => Not Existed => Create EventProjection

        var eventProjection = await _eventRepository.FindOneAsync(e => e.EventId == context.Message.IdEvent);

        if (eventProjection is null)
        {
            await Sender.Send(context.Message); // 

            eventProjection = new EventProjection()
            {
                EventId = context.Message.IdEvent,
                Name = context.Message.GetType().Name,
                Type = context.Message.GetType().Name
            };

            await _eventRepository.InsertOneAsync(eventProjection);
        }

    }
}