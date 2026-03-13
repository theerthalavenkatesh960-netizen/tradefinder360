using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.AI.Services;

/// <summary>
/// Tracks and manages trade outcomes for continuous AI learning
/// </summary>
public class TradeOutcomeService
{
    private readonly ITradeOutcomeRepository _outcomeRepository;
    private readonly IAIModelVersionRepository _modelVersionRepository;
    private readonly ILogger<TradeOutcomeService> _logger;

    public TradeOutcomeService(
        ITradeOutcomeRepository outcomeRepository,
        IAIModelVersionRepository modelVersionRepository,
        ILogger<TradeOutcomeService> logger)
    {
        _outcomeRepository = outcomeRepository;
        _modelVersionRepository = modelVersionRepository;
        _logger = logger;
    }

    /// <summary>
    /// Record a new trade entry with AI predictions
    /// </summary>
    public async Task<TradeOutcome> RecordTradeEntryAsync(
        AIAlphaPrediction prediction,
        decimal entryPrice,
        decimal quantity,
        string modelVersion,
        CancellationToken cancellationToken = default)
    {
        var outcome = new TradeOutcome
        {
            InstrumentId = prediction.InstrumentId,
            Symbol = prediction.Symbol,
            EntryTime = DateTimeOffset.UtcNow,
            EntryPrice = entryPrice,
            Direction = prediction.RecommendedAction,
            Quantity = quantity,
            
            // AI Predictions
            PredictedReturn = prediction.ExpectedReturn,
            PredictedSuccessProbability = prediction.SuccessProbability,
            PredictedRiskScore = prediction.RiskScore,
            ModelVersion = modelVersion,
            
            // Meta-factors
            MetaFactorsJson = JsonSerializer.Serialize(prediction.MetaFactors.ToDictionary()),
            
            // Market regime
            MarketRegimeAtEntry = prediction.MarketRegime.Regime.ToString(),
            RegimeConfidence = prediction.MarketRegime.Confidence,
            
            // Strategy details
            Strategy = "AI_ALPHA",
            Sector = prediction.Sector,
            Status = "OPEN"
        };

        await _outcomeRepository.CreateAsync(outcome, cancellationToken);

        _logger.LogInformation(
            "Recorded trade entry: {Symbol} {Direction} @ {Price}, Predicted Return: {Return}%, Probability: {Prob}%",
            outcome.Symbol, outcome.Direction, outcome.EntryPrice, 
            outcome.PredictedReturn, outcome.PredictedSuccessProbability * 100);

        return outcome;
    }

    /// <summary>
    /// Update trade when it exits
    /// </summary>
    public async Task<TradeOutcome> RecordTradeExitAsync(
        long outcomeId,
        decimal exitPrice,
        string exitReason,
        CancellationToken cancellationToken = default)
    {
        var outcome = await _outcomeRepository.GetByIdAsync(outcomeId, cancellationToken);
        if (outcome == null)
        {
            throw new ArgumentException($"Trade outcome {outcomeId} not found");
        }

        outcome.ExitTime = DateTimeOffset.UtcNow;
        outcome.ExitPrice = exitPrice;
        outcome.Status = "CLOSED";

        // Calculate actual results
        var priceChange = outcome.Direction == "BUY" 
            ? exitPrice - outcome.EntryPrice 
            : outcome.EntryPrice - exitPrice;

        outcome.ProfitLoss = priceChange * outcome.Quantity;
        outcome.ProfitLossPercent = priceChange / outcome.EntryPrice * 100;
        outcome.ActualReturn = outcome.ProfitLossPercent;
        outcome.IsSuccessful = outcome.ProfitLoss > 0;

        // Calculate prediction accuracy
        outcome.PredictionError = (float)Math.Abs(outcome.PredictedReturn - (float)outcome.ActualReturn.Value);
        
        // Accuracy score: 100 - (error as % of predicted return)
        var maxError = Math.Abs(outcome.PredictedReturn) > 0 
            ? Math.Abs(outcome.PredictedReturn) 
            : 10f; // Default max error
        outcome.PredictionAccuracyScore = Math.Max(0, 100 - (outcome.PredictionError.Value / maxError * 100));

        // Add learning tags
        outcome.LearningTags = GenerateLearningTags(outcome);
        outcome.FailureReason = outcome.IsSuccessful == false ? exitReason : null;

        await _outcomeRepository.UpdateAsync(outcome, cancellationToken);

        // Update model version statistics
        await UpdateModelStatisticsAsync(outcome.ModelVersion, cancellationToken);

        _logger.LogInformation(
            "Recorded trade exit: {Symbol} {Direction} @ {ExitPrice}, P&L: {PnL:C}, Accuracy: {Accuracy}%",
            outcome.Symbol, outcome.Direction, outcome.ExitPrice, 
            outcome.ProfitLoss, outcome.PredictionAccuracyScore);

        return outcome;
    }

    /// <summary>
    /// Get performance statistics
    /// </summary>
    public async Task<TradeOutcomeStatistics> GetPerformanceStatisticsAsync(
        string? modelVersion = null,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        CancellationToken cancellationToken = default)
    {
        return await _outcomeRepository.GetStatisticsAsync(
            modelVersion, startDate, endDate, cancellationToken);
    }

    /// <summary>
    /// Get trades for retraining
    /// </summary>
    public async Task<List<TradeOutcome>> GetCompletedTradesForTrainingAsync(
        int minTrades = 100,
        CancellationToken cancellationToken = default)
    {
        var endDate = DateTimeOffset.UtcNow;
        var startDate = endDate.AddDays(-90); // Last 90 days

        var trades = await _outcomeRepository.GetClosedTradesAsync(startDate, endDate, cancellationToken);

        if (trades.Count < minTrades)
        {
            // Expand date range if not enough trades
            startDate = endDate.AddDays(-180);
            trades = await _outcomeRepository.GetClosedTradesAsync(startDate, endDate, cancellationToken);
        }

        return trades;
    }

    private List<string> GenerateLearningTags(TradeOutcome outcome)
    {
        var tags = new List<string>();

        // Prediction accuracy tags
        if (outcome.PredictionAccuracyScore > 80)
            tags.Add("HIGH_ACCURACY");
        else if (outcome.PredictionAccuracyScore < 50)
            tags.Add("LOW_ACCURACY");

        // Performance tags
        if (outcome.IsSuccessful == true && (outcome.ActualReturn ?? 0m) > (decimal)outcome.PredictedReturn * 1.5m)
            tags.Add("OUTPERFORMED");
        else if (outcome.IsSuccessful == false && outcome.PredictedReturn > 5)
            tags.Add("FALSE_POSITIVE");

        // Market regime tags
        tags.Add($"REGIME_{outcome.MarketRegimeAtEntry}");

        // Sector tag
        tags.Add($"SECTOR_{outcome.Sector}");

        return tags;
    }

    private async Task UpdateModelStatisticsAsync(string modelVersion, CancellationToken cancellationToken)
    {
        var stats = await _outcomeRepository.GetStatisticsAsync(
            modelVersion, cancellationToken: cancellationToken);

        var model = await _modelVersionRepository.GetByVersionAsync(
            modelVersion, "AlphaModel", cancellationToken);

        if (model != null)
        {
            model.TotalPredictions = stats.TotalTrades;
            model.SuccessfulPredictions = stats.SuccessfulTrades;
            model.ProductionAccuracy = stats.WinRate;
            model.ProductionSharpeRatio = stats.SharpeRatio;
            model.TotalPnL = stats.TotalPnL;
            model.AveragePredictionError = stats.AveragePredictionError;

            await _modelVersionRepository.UpdateAsync(model, cancellationToken);
        }
    }
}