namespace TradingSystem.Core.Events;

/// <summary>
/// Base interface for all events in the system
/// </summary>
public interface IEvent
{
    DateTimeOffset Timestamp { get; }
    string EventId { get; }
}