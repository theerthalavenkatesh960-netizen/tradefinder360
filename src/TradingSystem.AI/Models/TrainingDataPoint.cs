namespace TradingSystem.AI.Models;

/// <summary>
/// Training dataset record with features and labels
/// </summary>
public class TrainingDataPoint
{
    public int InstrumentId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }

    // Feature vector
    public QuantFeatureVector Features { get; set; } = new();

    // Target labels
    public float TargetReturn5D { get; set; }
    public float TargetReturn10D { get; set; }
    public bool TradeSuccessLabel { get; set; }

    // Optional metadata
    public Dictionary<string, object> Metadata { get; set; } = new();
}