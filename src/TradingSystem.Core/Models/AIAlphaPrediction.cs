namespace TradingSystem.Core.Models;

/// <summary>
/// AI Alpha model prediction output
/// </summary>
public class AIAlphaPrediction
{
    public int InstrumentId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public DateTimeOffset PredictionTime { get; set; }
    
    // Core predictions
    public float ExpectedReturn { get; set; }  // % expected return
    public float SuccessProbability { get; set; }  // 0.0 to 1.0
    public float RiskScore { get; set; }  // 0.0 to 100.0
    
    // Confidence levels
    public float PredictionConfidence { get; set; }  // 0.0 to 1.0
    public string ConfidenceLevel { get; set; } = string.Empty;  // HIGH/MEDIUM/LOW
    
    // Meta-factors used
    public MetaFactors MetaFactors { get; set; } = new();
    
    // Market context
    public MarketRegimeDetection MarketRegime { get; set; } = new();
    public string Sector { get; set; } = string.Empty;
    
    // Feature importance
    public Dictionary<string, float> FeatureImportance { get; set; } = new();
    
    // Risk-adjusted metrics
    public float SharpeRatio { get; set; }
    public float SortinoRatio { get; set; }
    public float MaxDrawdownEstimate { get; set; }
    
    // Trade recommendations
    public string RecommendedAction { get; set; } = string.Empty;  // BUY/SELL/HOLD
    public decimal? SuggestedEntry { get; set; }
    public decimal? SuggestedStopLoss { get; set; }
    public decimal? SuggestedTarget { get; set; }
    public float PositionSizeMultiplier { get; set; } = 1.0f;
}