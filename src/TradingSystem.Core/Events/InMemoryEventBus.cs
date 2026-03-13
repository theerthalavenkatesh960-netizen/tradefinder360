using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace TradingSystem.Core.Events;

/// <summary>
/// In-memory implementation of event bus
/// Can be replaced with distributed event bus (RabbitMQ, Azure Service Bus) in production
/// </summary>
public class InMemoryEventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();
    private readonly ILogger<InMemoryEventBus> _logger;

    public InMemoryEventBus(ILogger<InMemoryEventBus> logger)
    {
        _logger = logger;
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        var eventType = typeof(TEvent);

        if (!_handlers.TryGetValue(eventType, out var handlers) || !handlers.Any())
        {
            _logger.LogDebug("No handlers registered for event type {EventType}", eventType.Name);
            return;
        }

        _logger.LogDebug("Publishing {EventType} to {Count} handlers", eventType.Name, handlers.Count);

        var tasks = handlers
            .Cast<Func<TEvent, Task>>()
            .Select(handler => SafeInvokeHandler(handler, @event, cancellationToken));

        await Task.WhenAll(tasks);
    }

    public void Subscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : IEvent
    {
        var eventType = typeof(TEvent);
        _handlers.AddOrUpdate(
            eventType,
            _ => new List<Delegate> { handler },
            (_, existing) =>
            {
                existing.Add(handler);
                return existing;
            });

        _logger.LogInformation("Subscribed handler for event type {EventType}", eventType.Name);
    }

    public void Unsubscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : IEvent
    {
        var eventType = typeof(TEvent);
        if (_handlers.TryGetValue(eventType, out var handlers))
        {
            handlers.Remove(handler);
            _logger.LogInformation("Unsubscribed handler for event type {EventType}", eventType.Name);
        }
    }

    private async Task SafeInvokeHandler<TEvent>(
        Func<TEvent, Task> handler,
        TEvent @event,
        CancellationToken cancellationToken) where TEvent : IEvent
    {
        try
        {
            await handler(@event);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing event handler for {EventType}", typeof(TEvent).Name);
        }
    }
}