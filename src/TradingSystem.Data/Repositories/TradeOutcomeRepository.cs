using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.Data.Repositories;

public class TradeOutcomeRepository : ITradeOutcomeRepository
{
    private readonly TradingDbContext _context;
    private readonly ILogger<TradeOutcomeRepository> _logger;

    public TradeOutcomeRepository(TradingDbContext context, ILogger<TradeOutcomeRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<TradeOutcome> CreateAsync(TradeOutcome outcome, CancellationToken cancellationToken = default)
    {
        _context.TradeOutcomes.Add(outcome);
        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Created trade outcome {Id} for {Symbol}", outcome.Id, outcome.Symbol);
        return outcome;
    }

    public async Task<TradeOutcome?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        return await _context.TradeOutcomes
            .Include(t => t.Instrument)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<List<TradeOutcome>> GetOpenTradesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.TradeOutcomes
            .Include(t => t.Instrument)
            .Where(t => t.Status == "OPEN")
            .OrderByDescending(t => t.EntryTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<TradeOutcome>> GetClosedTradesAsync(
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.TradeOutcomes
            .Include(t => t.Instrument)
            .Where(t => t.Status == "CLOSED" && t.ExitTime != null);

        if (startDate.HasValue)
            query = query.Where(t => t.ExitTime >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(t => t.ExitTime <= endDate.Value);

        return await query
            .OrderByDescending(t => t.ExitTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<TradeOutcome>> GetByModelVersionAsync(
        string modelVersion,
        CancellationToken cancellationToken = default)
    {
        return await _context.TradeOutcomes
            .Include(t => t.Instrument)
            .Where(t => t.ModelVersion == modelVersion)
            .OrderByDescending(t => t.EntryTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<TradeOutcome>> GetByMarketRegimeAsync(
        string regime,
        CancellationToken cancellationToken = default)
    {
        return await _context.TradeOutcomes
            .Include(t => t.Instrument)
            .Where(t => t.MarketRegimeAtEntry == regime)
            .OrderByDescending(t => t.EntryTime)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateAsync(TradeOutcome outcome, CancellationToken cancellationToken = default)
    {
        outcome.UpdatedAt = DateTimeOffset.UtcNow;
        _context.TradeOutcomes.Update(outcome);
        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Updated trade outcome {Id} for {Symbol}", outcome.Id, outcome.Symbol);
    }

    public async Task<TradeOutcomeStatistics> GetStatisticsAsync(
        string? modelVersion = null,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.TradeOutcomes
            .Where(t => t.Status == "CLOSED" && t.IsSuccessful.HasValue);

        if (!string.IsNullOrEmpty(modelVersion))
            query = query.Where(t => t.ModelVersion == modelVersion);

        if (startDate.HasValue)
            query = query.Where(t => t.ExitTime >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(t => t.ExitTime <= endDate.Value);

        var trades = await query.ToListAsync(cancellationToken);

        if (!trades.Any())
        {
            return new TradeOutcomeStatistics();
        }

        var totalTrades = trades.Count;
        var successfulTrades = trades.Count(t => t.IsSuccessful == true);
        var winRate = (float)successfulTrades / totalTrades * 100;

        var totalPnL = trades.Sum(t => t.ProfitLoss ?? 0);
        var avgPredictionError = trades.Average(t => t.PredictionError ?? 0);
        var avgAccuracy = trades.Average(t => t.PredictionAccuracyScore ?? 0);

        // Calculate profit factor
        var totalProfit = trades.Where(t => t.ProfitLoss > 0).Sum(t => t.ProfitLoss ?? 0);
        var totalLoss = Math.Abs(trades.Where(t => t.ProfitLoss < 0).Sum(t => t.ProfitLoss ?? 0));
        var profitFactor = totalLoss > 0 ? (float)(totalProfit / totalLoss) : 0f;

        // Calculate Sharpe ratio (simplified)
        var returns = trades.Select(t => (float)(t.ProfitLossPercent ?? 0)).ToList();
        var avgReturn = returns.Average();
        var stdDev = (float)Math.Sqrt(returns.Select(r => Math.Pow(r - avgReturn, 2)).Average());
        var sharpeRatio = stdDev > 0 ? (avgReturn - 0.05f) / stdDev : 0f;

        // Calculate max drawdown
        var runningPnL = 0m;
        var peak = 0m;
        var maxDrawdown = 0m;

        foreach (var trade in trades.OrderBy(t => t.ExitTime))
        {
            runningPnL += trade.ProfitLoss ?? 0;
            if (runningPnL > peak) peak = runningPnL;
            var drawdown = peak - runningPnL;
            if (drawdown > maxDrawdown) maxDrawdown = drawdown;
        }

        return new TradeOutcomeStatistics
        {
            TotalTrades = totalTrades,
            SuccessfulTrades = successfulTrades,
            WinRate = winRate,
            TotalPnL = totalPnL,
            AveragePredictionError = avgPredictionError,
            AveragePredictionAccuracy = avgAccuracy,
            ProfitFactor = profitFactor,
            SharpeRatio = sharpeRatio,
            MaxDrawdown = maxDrawdown
        };
    }
}