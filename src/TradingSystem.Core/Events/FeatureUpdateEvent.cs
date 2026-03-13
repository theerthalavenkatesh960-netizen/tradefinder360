namespace TradingSystem.Core.Events;

/// <summary>
/// Event triggered when feature vector is computed for an instrument
/// </summary>
public class FeatureUpdateEvent : IEvent
{
    public DateTimeOffset Timestamp { get; init; }
    public long EventId { get; init; }
    public int InstrumentId { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public Dictionary<string, float> Features { get; init; } = new();
    public int FeatureCount { get; init; }
}