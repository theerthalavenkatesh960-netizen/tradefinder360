namespace TradingSystem.Core.Models;

/// <summary>
/// Portfolio performance metrics computed from closed trades in a session or time period
/// Used to determine if learning/adaptation is needed
/// </summary>
public class PortfolioPerformanceMetrics
{
    /// <summary>
    /// Percentage of trades that were profitable (PnL > 0)
    /// Range: 0-100
    /// </summary>
    public decimal WinRate { get; set; }

    /// <summary>
    /// Risk-adjusted return metric: (avg return - risk-free rate) / std deviation
    /// Higher is better. Typical thresholds: <0.5 = poor, >1.0 = excellent
    /// </summary>
    public decimal SharpeRatio { get; set; }

    /// <summary>
    /// Maximum peak-to-trough drawdown during period
    /// Expressed as percentage of peak equity
    /// Range: 0-100
    /// </summary>
    public decimal MaxDrawdown { get; set; }

    /// <summary>
    /// Profit factor: sum of winning trades / absolute(sum of losing trades)
    /// 1.5 = profits 1.5x losses; 2.0 = profits 2x losses
    /// </summary>
    public decimal ProfitFactor { get; set; }

    /// <summary>
    /// Average hold duration in days for all closed trades
    /// </summary>
    public decimal AverageHoldDays { get; set; }

    /// <summary>
    /// Average profit/loss per day held: TotalPnL / (TotalHoldDays)
    /// Efficiency metric: higher = faster profits
    /// </summary>
    public decimal AverageHoldEfficiency { get; set; }

    /// <summary>
    /// Average fusion score of positions that were included (ShouldInclude = true)
    /// 0-1 scale; indicates quality of accepted candidates
    /// </summary>
    public decimal AverageFusionScore { get; set; }

    /// <summary>
    /// Percentage of candidates rejected by directional veto
    /// If high (>40%), indicates veto is too strict
    /// </summary>
    public decimal VetoRejectionRate { get; set; }

    /// <summary>
    /// Total number of closed trades in analysis window
    /// </summary>
    public int TotalTrades { get; set; }

    /// <summary>
    /// Count of profitable trades (Pnl > 0)
    /// </summary>
    public int WinningTrades { get; set; }

    /// <summary>
    /// Count of losing trades (Pnl <= 0)
    /// </summary>
    public int LosingTrades { get; set; }

    /// <summary>
    /// Total P&L (sum of all closed position P&L)
    /// Expressed in currency units
    /// </summary>
    public decimal TotalPnL { get; set; }

    /// <summary>
    /// When these metrics were computed
    /// </summary>
    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Helper: checks if performance is degraded (triggers learning)
    /// </summary>
    public bool IsPerformanceDegraded(
        decimal minWinRateThreshold = 50,
        decimal minSharpeThreshold = 0.5m,
        decimal maxDrawdownThreshold = 20)
    {
        return WinRate < minWinRateThreshold
            || SharpeRatio < minSharpeThreshold
            || MaxDrawdown > maxDrawdownThreshold;
    }
}
