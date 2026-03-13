namespace TradingSystem.Core.Models;

/// <summary>
/// Persisted strategy signal record
/// </summary>
public class StrategySignalRecord
{
    public int Id { get; set; }
    public int InstrumentId { get; set; }
    public StrategyType StrategyType { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public bool IsValid { get; set; }
    public int Score { get; set; }
    public string Direction { get; set; } = string.Empty; // BUY/SELL
    public decimal EntryPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal Target { get; set; }
    public decimal Confidence { get; set; }
    public decimal RiskRewardRatio { get; set; }
    public string SignalsJson { get; set; } = string.Empty; // JSON array of signals
    public string MetricsJson { get; set; } = string.Empty; // JSON dictionary of metrics
    public string Explanation { get; set; } = string.Empty;
    public int? MarketSentimentId { get; set; } // Link to market sentiment at time of signal
    public bool WasActedUpon { get; set; } // Was a trade taken based on this signal?
    public int? RelatedTradeId { get; set; } // Link to actual trade if executed
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }

    // Navigation properties
    public TradingInstrument? Instrument { get; set; }
    public MarketSentiment? MarketSentiment { get; set; }
    public TradeRecord? RelatedTrade { get; set; }
}

/// <summary>
/// Strategy performance metrics aggregated over time
/// </summary>
public class StrategyPerformance
{
    public int Id { get; set; }
    public StrategyType StrategyType { get; set; }
    public DateTimeOffset PeriodStart { get; set; }
    public DateTimeOffset PeriodEnd { get; set; }
    public int TotalSignals { get; set; }
    public int ValidSignals { get; set; }
    public int SignalsActedUpon { get; set; }
    public decimal AverageScore { get; set; }
    public decimal AverageConfidence { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public decimal WinRate { get; set; }
    public decimal AveragePnL { get; set; }
    public decimal TotalPnL { get; set; }
    public decimal BestTrade { get; set; }
    public decimal WorstTrade { get; set; }
    public decimal AverageRiskReward { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}