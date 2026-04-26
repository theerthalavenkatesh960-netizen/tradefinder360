namespace TradingSystem.Core.Models;

/// <summary>
/// Historical record of portfolio performance metrics
/// Used to track performance trends and as baseline for learning decisions
/// </summary>
public class PortfolioPerformanceHistory
{
    public long Id { get; set; }

    /// <summary>
    /// Session this history record belongs to
    /// </summary>
    public long SessionId { get; set; }

    /// <summary>
    /// Metrics snapshot at this point in time
    ///</summary>
    public decimal WinRate { get; set; }
    public decimal SharpeRatio { get; set; }
    public decimal MaxDrawdown { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal AverageHoldDays { get; set; }
    public decimal AverageHoldEfficiency { get; set; }
    public decimal AverageFusionScore { get; set; }
    public decimal VetoRejectionRate { get; set; }
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public decimal TotalPnL { get; set; }

    /// <summary>
    /// Which fusion config was active when this was computed
    /// Used to correlate performance with specific config state
    /// </summary>
    public int? ActiveFusionLearningConfigIteration { get; set; }

    /// <summary>
    /// When this metrics snapshot was recorded
    /// </summary>
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    public PortfolioManagerSession? Session { get; set; }
}
