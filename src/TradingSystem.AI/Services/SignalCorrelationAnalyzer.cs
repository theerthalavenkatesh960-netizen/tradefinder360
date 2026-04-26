using Microsoft.Extensions.Logging;
using TradingSystem.Core.Models;
using TradingSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace TradingSystem.AI.Services;

/// <summary>
/// Analyzes correlation between fusion signals and trade outcomes
/// Determines which signals (Technical, News, Sector) are most predictive of wins
/// Used by PortfolioLearningService to decide which weights to increase
/// Extends TradeOutcomeService patterns for consistency
/// </summary>
public class SignalCorrelationAnalyzer
{
    private readonly TradingDbContext _dbContext;
    private readonly ILogger<SignalCorrelationAnalyzer> _logger;

    public SignalCorrelationAnalyzer(
        TradingDbContext dbContext,
        ILogger<SignalCorrelationAnalyzer> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Analyze signal correlations from portfolio trades
    /// Returns correlation scores for each signal type
    /// Scores range from -1 (perfect predictor of losses) to +1 (perfect predictor of wins)
    /// </summary>
    public async Task<Dictionary<string, float>> AnalyzeSignalCorrelationsAsync(
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analyzing signal correlations for session {SessionId}", sessionId);

        var closedTrades = await _dbContext.PortfolioManagerTrades
            .Where(t => t.SessionId == sessionId 
                && t.Status == PortfolioTradeStatus.CLOSED
                && t.FusionTechnicalSignal.HasValue
                && t.FusionNewsSignal.HasValue
                && t.FusionSectorSignal.HasValue)
            .ToListAsync(cancellationToken);

        if (closedTrades.Count < 3)
        {
            _logger.LogWarning("Not enough closed trades with signal data for session {SessionId}", sessionId);
            return new Dictionary<string, float>
            {
                ["Technical"] = 0f,
                ["News"] = 0f,
                ["Sector"] = 0f
            };
        }

        var technicalCorr = CalculateSignalCorrelation(
            closedTrades.Select(t => (double)t.FusionTechnicalSignal!.Value).ToList(),
            closedTrades.Select(t => t.Pnl.GetValueOrDefault(0) > 0 ? 1.0 : 0.0).ToList());

        var newsCorr = CalculateSignalCorrelation(
            closedTrades.Select(t => (double)t.FusionNewsSignal!.Value).ToList(),
            closedTrades.Select(t => t.Pnl.GetValueOrDefault(0) > 0 ? 1.0 : 0.0).ToList());

        var sectorCorr = CalculateSignalCorrelation(
            closedTrades.Select(t => (double)t.FusionSectorSignal!.Value).ToList(),
            closedTrades.Select(t => t.Pnl.GetValueOrDefault(0) > 0 ? 1.0 : 0.0).ToList());

        var result = new Dictionary<string, float>
        {
            ["Technical"] = (float)technicalCorr,
            ["News"] = (float)newsCorr,
            ["Sector"] = (float)sectorCorr
        };

        _logger.LogInformation(
            "Signal correlations for session {SessionId}: Technical={Tech:F3}, News={News:F3}, Sector={Sector:F3}",
            sessionId, technicalCorr, newsCorr, sectorCorr);

        return result;
    }

    /// <summary>
    /// Analyze signal correlations across multiple sessions (aggregate trend)
    /// Useful for seeing which signals are consistently predictive
    /// </summary>
    public async Task<Dictionary<string, float>> AnalyzeSignalCorrelationsForUserAsync(
        string userId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Analyzing signal correlations for user {UserId} from {From} to {To}",
            userId, fromDate, toDate);

        // Get all sessions for this user
        var userSessions = await _dbContext.PortfolioManagerSessions
            .Where(s => s.UserId == userId)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var closedTrades = await _dbContext.PortfolioManagerTrades
            .Where(t => userSessions.Contains(t.SessionId)
                && t.Status == PortfolioTradeStatus.CLOSED
                && t.ClosedAt >= fromDate
                && t.ClosedAt <= toDate
                && t.FusionTechnicalSignal.HasValue
                && t.FusionNewsSignal.HasValue
                && t.FusionSectorSignal.HasValue)
            .ToListAsync(cancellationToken);

        if (closedTrades.Count < 5)
        {
            _logger.LogWarning("Not enough trades with signal data for user {UserId}", userId);
            return new Dictionary<string, float>
            {
                ["Technical"] = 0f,
                ["News"] = 0f,
                ["Sector"] = 0f
            };
        }

        var technicalCorr = CalculateSignalCorrelation(
            closedTrades.Select(t => (double)t.FusionTechnicalSignal!.Value).ToList(),
            closedTrades.Select(t => t.Pnl.GetValueOrDefault(0) > 0 ? 1.0 : 0.0).ToList());

        var newsCorr = CalculateSignalCorrelation(
            closedTrades.Select(t => (double)t.FusionNewsSignal!.Value).ToList(),
            closedTrades.Select(t => t.Pnl.GetValueOrDefault(0) > 0 ? 1.0 : 0.0).ToList());

        var sectorCorr = CalculateSignalCorrelation(
            closedTrades.Select(t => (double)t.FusionSectorSignal!.Value).ToList(),
            closedTrades.Select(t => t.Pnl.GetValueOrDefault(0) > 0 ? 1.0 : 0.0).ToList());

        var result = new Dictionary<string, float>
        {
            ["Technical"] = (float)technicalCorr,
            ["News"] = (float)newsCorr,
            ["Sector"] = (float)sectorCorr
        };

        _logger.LogInformation(
            "Aggregate signal correlations for user {UserId} ({Count} trades): Tech={Tech:F3}, News={News:F3}, Sector={Sector:F3}",
            userId, closedTrades.Count, technicalCorr, newsCorr, sectorCorr);

        return result;
    }

    /// <summary>
    /// Pearson correlation coefficient between signal values and trade outcomes (win/loss)
    /// Range: -1 to 1
    /// Positive = signal predicts wins, Negative = signal predicts losses, 0 = no correlation
    /// </summary>
    private double CalculateSignalCorrelation(List<double> signalValues, List<double> outcomes)
    {
        if (signalValues.Count < 2 || signalValues.Count != outcomes.Count)
            return 0;

        var n = signalValues.Count;
        var meanSignal = signalValues.Average();
        var meanOutcome = outcomes.Average();

        var numerator = 0.0;
        var denomSignal = 0.0;
        var denomOutcome = 0.0;

        for (int i = 0; i < n; i++)
        {
            var signalDiff = signalValues[i] - meanSignal;
            var outcomeDiff = outcomes[i] - meanOutcome;

            numerator += signalDiff * outcomeDiff;
            denomSignal += signalDiff * signalDiff;
            denomOutcome += outcomeDiff * outcomeDiff;
        }

        var denominator = Math.Sqrt(denomSignal * denomOutcome);
        if (denominator == 0)
            return 0;

        return numerator / denominator;
    }
}
