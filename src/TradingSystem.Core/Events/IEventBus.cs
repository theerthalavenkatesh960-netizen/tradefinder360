namespace TradingSystem.Core.Events;

/// <summary>
/// Event bus for publishing and subscribing to system events
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Publish an event to all subscribers
    /// </summary>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent;

    /// <summary>
    /// Subscribe to events of a specific type
    /// </summary>
    void Subscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : IEvent;

    /// <summary>
    /// Unsubscribe from events
    /// </summary>
    void Unsubscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : IEvent;
}