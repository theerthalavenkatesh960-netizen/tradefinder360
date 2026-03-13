namespace TradingSystem.Core.Events;

/// <summary>
/// Event triggered when new market data is received
/// </summary>
public class MarketDataEvent : IEvent
{
    public DateTimeOffset Timestamp { get; init; }
    public string EventId { get; init; } = Guid.NewGuid().ToString();
    public int InstrumentId { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public long Volume { get; init; }
    public MarketDataType DataType { get; init; }
    public Dictionary<string, object> AdditionalData { get; init; } = new();
}

public enum MarketDataType
{
    PRICE_UPDATE,
    CANDLE_CLOSE,
    VOLUME_SPIKE,
    NEWS_SENTIMENT,
    INDICATOR_UPDATE
}