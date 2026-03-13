namespace TradingSystem.Core.Models;

/// <summary>
/// Stores pre-computed feature vectors for ML training and inference
/// </summary>
public class FeatureStore
{
    public long Id { get; set; }
    public int InstrumentId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    
    /// <summary>
    /// JSON serialized feature dictionary (120+ features)
    /// </summary>
    public string FeaturesJson { get; set; } = string.Empty;
    
    /// <summary>
    /// Number of features in the vector (for validation)
    /// </summary>
    public int FeatureCount { get; set; }
    
    /// <summary>
    /// Feature schema version for tracking changes
    /// </summary>
    public string FeatureVersion { get; set; } = "1.0";
    
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation property
    public TradingInstrument? Instrument { get; set; }
}