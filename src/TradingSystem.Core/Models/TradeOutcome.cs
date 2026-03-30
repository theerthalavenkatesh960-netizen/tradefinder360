namespace TradingSystem.Core.Models;

/// <summary>
/// Tracks actual outcomes of AI-predicted trades for continuous learning
/// </summary>
public class TradeOutcome
{
    public long Id { get; set; }
    public int InstrumentId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    
    // Trade execution details
    public DateTimeOffset EntryTime { get; set; }
    public DateTimeOffset? ExitTime { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal? ExitPrice { get; set; }
    public string Direction { get; set; } = string.Empty; // BUY/SELL
    public decimal Quantity { get; set; }
    
    // AI Prediction data (captured at entry)
    public float PredictedReturn { get; set; }  // %
    public float PredictedSuccessProbability { get; set; }
    public float PredictedRiskScore { get; set; }
    public string ModelVersion { get; set; } = string.Empty;
    
    // Meta-factors at entry (JSON)
    public string MetaFactorsJson { get; set; } = string.Empty;
    
    // Market regime at entry
    public string MarketRegimeAtEntry { get; set; } = string.Empty;
    public float RegimeConfidence { get; set; }
    
    // Actual outcome
    public decimal? ActualReturn { get; set; }  // %
    public decimal? ProfitLoss { get; set; }
    public decimal? ProfitLossPercent { get; set; }
    public bool? IsSuccessful { get; set; }
    
    // Prediction accuracy metrics
    public float? PredictionError { get; set; }  // Abs difference between predicted and actual
    public float? PredictionAccuracyScore { get; set; }  // 0-100
    
    // Learning signals
    public string? FailureReason { get; set; }
    public List<string> LearningTags { get; set; } = new();
    
    // Strategy details
    public string Strategy { get; set; } = string.Empty;
    public string Sector { get; set; } = string.Empty;
    
    // Status
    public string Status { get; set; } = "OPEN";  // OPEN, CLOSED, STOPPED_OUT, TARGET_HIT
    
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    
    // Navigation
    public TradingInstrument? Instrument { get; set; }
}