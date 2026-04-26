using Microsoft.Extensions.Logging;
using TradingSystem.Core.Models;
using TradingSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace TradingSystem.AI.Services;

/// <summary>
/// Analyzes portfolio session performance by computing metrics from closed trades
/// Forms the foundation for learning/adaptation decisions
/// Follows the pattern of ModelPerformanceMonitor for consistency
/// </summary>
public class PortfolioPerformanceAnalyzer
{
    private readonly TradingDbContext _dbContext;
    private readonly ILogger<PortfolioPerformanceAnalyzer> _logger;

    // Default performance thresholds (can be overridden)
    public const decimal DefaultMinWinRateThreshold = 50m;
    public const decimal DefaultMinSharpeThreshold = 0.5m;
    public const decimal DefaultMaxDrawdownThreshold = 20m;
    public const decimal DefaultVetoRejectionThreshold = 40m;

    public PortfolioPerformanceAnalyzer(
        TradingDbContext dbContext,
        ILogger<PortfolioPerformanceAnalyzer> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Analyze a single portfolio session, computing comprehensive metrics
    /// </summary>
    public async Task<PortfolioPerformanceMetrics> AnalyzeSessionAsync(
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analyzing portfolio session {SessionId}", sessionId);

        var trades = await _dbContext.PortfolioManagerTrades
            .Where(t => t.SessionId == sessionId && t.Status == PortfolioTradeStatus.CLOSED)
            .ToListAsync(cancellationToken);

        if (trades.Count == 0)
        {
            _logger.LogWarning("No closed trades found for session {SessionId}", sessionId);
            return new PortfolioPerformanceMetrics
            {
                TotalTrades = 0,
                WinRate = 0,
                SharpeRatio = 0,
                MaxDrawdown = 0,
                ProfitFactor = 0,
                AverageHoldDays = 0,
                AverageHoldEfficiency = 0,
                AverageFusionScore = 0,
                VetoRejectionRate = 0,
                WinningTrades = 0,
                LosingTrades = 0,
                TotalPnL = 0
            };
        }

        return ComputeMetricsFromTrades(trades);
    }

    /// <summary>
    /// Analyze performance over a date range (across multiple sessions)
    /// Useful for detecting trends and comparing before/after learning
    /// </summary>
    public async Task<PortfolioPerformanceMetrics> AnalyzePeriodAsync(
        string userId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Analyzing portfolio performance for user {UserId} from {From} to {To}",
            userId, fromDate, toDate);

        // Get all sessions for user first, then find trades in those sessions
        var userSessions = await _dbContext.PortfolioManagerSessions
            .Where(s => s.UserId == userId)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var trades = await _dbContext.PortfolioManagerTrades
            .Where(t => userSessions.Contains(t.SessionId)
                && t.Status == PortfolioTradeStatus.CLOSED
                && t.ClosedAt >= fromDate
                && t.ClosedAt <= toDate)
            .ToListAsync(cancellationToken);

        if (trades.Count == 0)
        {
            _logger.LogWarning("No closed trades found for period {From}-{To}", fromDate, toDate);
            return new PortfolioPerformanceMetrics
            {
                TotalTrades = 0,
                WinRate = 0,
                SharpeRatio = 0,
                MaxDrawdown = 0,
                ProfitFactor = 0,
                AverageHoldDays = 0,
                AverageHoldEfficiency = 0,
                AverageFusionScore = 0,
                VetoRejectionRate = 0,
                WinningTrades = 0,
                LosingTrades = 0,
                TotalPnL = 0
            };
        }

        return ComputeMetricsFromTrades(trades);
    }

    /// <summary>
    /// Analyze last N closed sessions for a user
    /// Most common use case: "give me metrics from last 5 sessions"
    /// </summary>
    public async Task<PortfolioPerformanceMetrics> AnalyzeLastSessionsAsync(
        string userId,
        int sessionCount = 5,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Analyzing last {Count} sessions for user {UserId}",
            sessionCount, userId);

        var completedSessions = await _dbContext.PortfolioManagerSessions
            .Where(s => s.UserId == userId && s.Status == PortfolioSessionStatus.COMPLETED)
            .OrderByDescending(s => s.UpdatedAt)
            .Take(sessionCount)
            .ToListAsync(cancellationToken);

        if (completedSessions.Count == 0)
        {
            _logger.LogWarning("No completed sessions found for user {UserId}", userId);
            return new PortfolioPerformanceMetrics();
        }

        var sessionIds = completedSessions.Select(s => s.Id).ToList();

        var trades = await _dbContext.PortfolioManagerTrades
            .Where(t => sessionIds.Contains(t.SessionId) && t.Status == PortfolioTradeStatus.CLOSED)
            .ToListAsync(cancellationToken);

        if (trades.Count == 0)
        {
            _logger.LogWarning("No closed trades in last {Count} sessions", sessionCount);
            return new PortfolioPerformanceMetrics();
        }

        return ComputeMetricsFromTrades(trades);
    }

    /// <summary>
    /// Core metrics computation logic
    /// Internal method used by all analysis variants
    /// </summary>
    private PortfolioPerformanceMetrics ComputeMetricsFromTrades(List<PortfolioManagerTrade> trades)
    {
        if (trades.Count == 0)
        {
            return new PortfolioPerformanceMetrics();
        }

        var metrics = new PortfolioPerformanceMetrics
        {
            TotalTrades = trades.Count,
        };

        // 1. Win rate: % of profitable trades
        var profitableTrades = trades.Where(t => t.Pnl.GetValueOrDefault(0) > 0).ToList();
        metrics.WinningTrades = profitableTrades.Count;
        metrics.LosingTrades = trades.Count - metrics.WinningTrades;
        metrics.WinRate = trades.Count > 0 ? (metrics.WinningTrades / (decimal)trades.Count) * 100 : 0;

        // 2. Total P&L
        metrics.TotalPnL = trades.Sum(t => t.Pnl.GetValueOrDefault(0));

        // 3. Profit factor: sum(wins) / abs(sum(losses))
        var totalWins = profitableTrades.Sum(t => t.Pnl.GetValueOrDefault(0));
        var totalLosses = trades.Where(t => t.Pnl.GetValueOrDefault(0) <= 0)
            .Sum(t => t.Pnl.GetValueOrDefault(0));
        var absTotalLosses = Math.Abs(totalLosses);
        metrics.ProfitFactor = absTotalLosses > 0 ? totalWins / absTotalLosses : (totalWins > 0 ? 100 : 0);

        // 4. Hold duration metrics
        var holdDurations = new List<decimal>();
        foreach (var trade in trades)
        {
            if (trade.ClosedAt.HasValue && trade.OpenedAt != default)
            {
                var holdDays = (decimal)(trade.ClosedAt.Value - trade.OpenedAt).TotalDays;
                holdDurations.Add(holdDays);
            }
        }

        if (holdDurations.Count > 0)
        {
            metrics.AverageHoldDays = holdDurations.Average();
            var totalHoldDays = holdDurations.Sum();
            metrics.AverageHoldEfficiency = totalHoldDays > 0 ? metrics.TotalPnL / totalHoldDays : 0;
        }

        // 5. Sharpe ratio: (return - risk_free) / std_dev
        // Simplified: assuming 0% risk-free rate
        var returns = trades.Select(t => t.PnlPercent.GetValueOrDefault(0)).ToList();
        var avgReturn = returns.Count > 0 ? returns.Average() : 0;
        var stdDev = ComputeStdDev(returns);
        metrics.SharpeRatio = stdDev > 0 ? avgReturn / (decimal)stdDev : 0;

        // 6. Max drawdown: peak-to-trough decline
        metrics.MaxDrawdown = ComputeMaxDrawdown(trades);

        // 7. Average fusion score (of included positions)
        var includedWithScore = trades
            .Where(t => t.FusionScore.HasValue && t.FusionIncluded == true)
            .Select(t => t.FusionScore.Value)
            .ToList();
        metrics.AverageFusionScore = includedWithScore.Count > 0 ? includedWithScore.Average() : 0;

        // 8. Veto rejection rate: candidates rejected by directional veto
        var vetoedTrades = trades.Count(t => t.FusionDirectionVeto == true);
        var candidatesWithVetoInfo = trades.Count(t => t.FusionDirectionVeto.HasValue);
        metrics.VetoRejectionRate = candidatesWithVetoInfo > 0
            ? (vetoedTrades / (decimal)candidatesWithVetoInfo) * 100
            : 0;

        _logger.LogInformation(
            "Computed metrics for {Count} trades: WinRate={WinRate:F1}%, Sharpe={Sharpe:F2}, PnL={Pnl:F2}, MaxDD={MaxDD:F1}%",
            trades.Count, metrics.WinRate, metrics.SharpeRatio, metrics.TotalPnL, metrics.MaxDrawdown);

        return metrics;
    }

    /// <summary>
    /// Calculate standard deviation of a list of values
    /// </summary>
    private decimal ComputeStdDev(List<decimal> values)
    {
        if (values.Count < 2)
            return 0;

        var avg = values.Average();
        var variance = values.Sum(x => (x - avg) * (x - avg)) / values.Count;
        return (decimal)Math.Sqrt((double)variance);
    }

    /// <summary>
    /// Calculate maximum drawdown from a sequence of trades
    /// Drawdown = peak equity to trough equity as percentage
    /// </summary>
    private decimal ComputeMaxDrawdown(List<PortfolioManagerTrade> trades)
    {
        if (trades.Count == 0)
            return 0;

        // Build equity curve (running cumulative P&L)
        var equityCurve = new List<decimal>();
        decimal runningTotal = 0;

        // Sort by close date to ensure chronological order
        var sortedTrades = trades.OrderBy(t => t.ClosedAt).ToList();

        foreach (var trade in sortedTrades)
        {
            runningTotal += trade.Pnl.GetValueOrDefault(0);
            equityCurve.Add(runningTotal);
        }

        if (equityCurve.Count == 0)
            return 0;

        // Find peak-to-trough decline
        decimal maxDrawdown = 0;
        decimal peak = equityCurve[0];

        foreach (var equity in equityCurve)
        {
            if (equity > peak)
            {
                peak = equity;
            }

            var drawdown = peak > 0 ? ((peak - equity) / peak) * 100 : 0;
            if (drawdown > maxDrawdown)
            {
                maxDrawdown = drawdown;
            }
        }

        return maxDrawdown;
    }
}
