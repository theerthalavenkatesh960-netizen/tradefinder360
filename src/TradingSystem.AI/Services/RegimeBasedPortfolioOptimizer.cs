using Microsoft.Extensions.Logging;
using TradingSystem.Core.Models;

namespace TradingSystem.AI.Services;

/// <summary>
/// Portfolio optimization adjusted for market regime
/// </summary>
public class RegimeBasedPortfolioOptimizer
{
    private readonly AIAlphaModelService _alphaModel;
    private readonly MarketRegimeService _regimeService;
    private readonly ILogger<RegimeBasedPortfolioOptimizer> _logger;

    public RegimeBasedPortfolioOptimizer(
        AIAlphaModelService alphaModel,
        MarketRegimeService regimeService,
        ILogger<RegimeBasedPortfolioOptimizer> logger)
    {
        _alphaModel = alphaModel;
        _regimeService = regimeService;
        _logger = logger;
    }

    /// <summary>
    /// Build optimized portfolio allocation
    /// </summary>
    public async Task<PortfolioAllocation> OptimizePortfolioAsync(
        List<AIAlphaPrediction> predictions,
        decimal totalCapital,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Optimizing portfolio with {Count} predictions and {Capital:C} capital",
            predictions.Count, totalCapital);

        var regime = await _regimeService.DetectRegimeAsync(cancellationToken);

        // Filter predictions by regime guidance
        var viablePredictions = FilterByRegime(predictions, regime);

        // Calculate optimal allocations
        var allocations = CalculateAllocations(viablePredictions, regime, totalCapital);

        // Build portfolio
        var portfolio = new PortfolioAllocation
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            TotalCapital = totalCapital,
            MarketRegime = regime,
            Positions = allocations,
            ExpectedReturn = allocations.Sum(a => a.ExpectedReturn * (float)(a.AllocationAmount / totalCapital)),
            PortfolioRisk = CalculatePortfolioRisk(allocations),
            Diversification = CalculateDiversificationScore(allocations),
            MaxDrawdownEstimate = CalculatePortfolioMaxDrawdown(allocations)
        };

        portfolio.SharpeRatio = CalculatePortfolioSharpe(portfolio);

        _logger.LogInformation(
            "Portfolio optimized: {Count} positions, Expected Return: {Return:F2}%, Risk: {Risk:F1}",
            portfolio.Positions.Count, portfolio.ExpectedReturn, portfolio.PortfolioRisk);

        return portfolio;
    }

    private List<AIAlphaPrediction> FilterByRegime(
        List<AIAlphaPrediction> predictions,
        MarketRegimeDetection regime)
    {
        return regime.Regime switch
        {
            MarketRegimeType.BEAR_MARKET => predictions
                .Where(p => p.RiskScore < 70 && p.SuccessProbability > 0.65f)
                .ToList(),

            MarketRegimeType.HIGH_VOLATILITY_MARKET => predictions
                .Where(p => p.RiskScore < 60 && p.SuccessProbability > 0.70f)
                .ToList(),

            MarketRegimeType.BULL_MARKET => predictions
                .Where(p => p.SuccessProbability > 0.55f)
                .ToList(),

            _ => predictions
                .Where(p => p.SuccessProbability > 0.60f)
                .ToList()
        };
    }

    private List<PortfolioPosition> CalculateAllocations(
        List<AIAlphaPrediction> predictions,
        MarketRegimeDetection regime,
        decimal totalCapital)
    {
        var allocations = new List<PortfolioPosition>();
        
        // Maximum exposure based on regime
        var maxExposure = regime.Guidance.RecommendedExposure;
        var availableCapital = totalCapital * (decimal)maxExposure;

        // Risk-adjusted weighting
        var totalWeight = predictions.Sum(p => CalculateWeight(p, regime));

        foreach (var prediction in predictions)
        {
            var weight = CalculateWeight(prediction, regime) / totalWeight;
            var allocationAmount = availableCapital * (decimal)weight;

            // Minimum allocation threshold
            if (allocationAmount < totalCapital * 0.02m) continue;  // Min 2% position

            // Maximum single position limit
            var maxPosition = regime.Regime == MarketRegimeType.BEAR_MARKET ? 0.15m : 0.25m;
            allocationAmount = Math.Min(allocationAmount, totalCapital * maxPosition);

            allocations.Add(new PortfolioPosition
            {
                InstrumentId = prediction.InstrumentId,
                Symbol = prediction.Symbol,
                Sector = prediction.Sector,
                Action = prediction.RecommendedAction,
                AllocationAmount = allocationAmount,
                AllocationPercent = (float)(allocationAmount / totalCapital * 100),
                ExpectedReturn = prediction.ExpectedReturn,
                RiskScore = prediction.RiskScore,
                SuccessProbability = prediction.SuccessProbability,
                Entry = prediction.SuggestedEntry,
                StopLoss = prediction.SuggestedStopLoss,
                Target = prediction.SuggestedTarget,
                PositionSizeMultiplier = prediction.PositionSizeMultiplier
            });
        }

        return allocations.OrderByDescending(a => a.AllocationAmount).ToList();
    }

    private float CalculateWeight(AIAlphaPrediction prediction, MarketRegimeDetection regime)
    {
        // Base weight from expected return
        var weight = Math.Abs(prediction.ExpectedReturn);

        // Multiply by success probability
        weight *= prediction.SuccessProbability;

        // Adjust by risk (inverse relationship)
        weight *= (100 - prediction.RiskScore) / 100f;

        // Regime multiplier
        weight *= regime.Confidence;

        return Math.Max(weight, 0.01f);
    }

    private float CalculatePortfolioRisk(List<PortfolioPosition> positions)
    {
        if (!positions.Any()) return 0f;

        // Weighted average risk
        var totalAllocation = positions.Sum(p => p.AllocationAmount);
        var weightedRisk = positions.Sum(p => 
            p.RiskScore * (float)(p.AllocationAmount / totalAllocation));

        return weightedRisk;
    }

    private float CalculateDiversificationScore(List<PortfolioPosition> positions)
    {
        if (!positions.Any()) return 0f;

        // Sector diversification
        var sectors = positions.GroupBy(p => p.Sector).Count();
        var sectorScore = Math.Min(sectors / 5f, 1f) * 50;  // Max 5 sectors

        // Position count diversification
        var positionScore = Math.Min(positions.Count / 10f, 1f) * 30;  // Max 10 positions

        // Concentration (Herfindahl index)
        var totalAllocation = positions.Sum(p => p.AllocationAmount);
        var herfindahl = positions.Sum(p => 
            Math.Pow((double)(p.AllocationAmount / totalAllocation), 2));
        var concentrationScore = (1 - (float)herfindahl) * 20;

        return sectorScore + positionScore + concentrationScore;
    }

    private float CalculatePortfolioMaxDrawdown(List<PortfolioPosition> positions)
    {
        if (!positions.Any()) return 0f;

        var totalAllocation = positions.Sum(p => p.AllocationAmount);
        var weightedDrawdown = positions.Sum(p =>
        {
            var estimatedDrawdown = p.RiskScore * 0.3f;  // Risk to drawdown conversion
            return estimatedDrawdown * (float)(p.AllocationAmount / totalAllocation);
        });

        return weightedDrawdown;
    }

    private float CalculatePortfolioSharpe(PortfolioAllocation portfolio)
    {
        var riskFreeRate = 5f;  // 5% annual
        var annualizedReturn = portfolio.ExpectedReturn * 12;
        var annualizedRisk = portfolio.PortfolioRisk / 10f;

        return annualizedRisk > 0 ? (annualizedReturn - riskFreeRate) / annualizedRisk : 0f;
    }
}

public class PortfolioAllocation
{
    public DateTimeOffset GeneratedAt { get; set; }
    public decimal TotalCapital { get; set; }
    public MarketRegimeDetection MarketRegime { get; set; } = new();
    public List<PortfolioPosition> Positions { get; set; } = new();
    public float ExpectedReturn { get; set; }
    public float PortfolioRisk { get; set; }
    public float SharpeRatio { get; set; }
    public float Diversification { get; set; }
    public float MaxDrawdownEstimate { get; set; }
}

public class PortfolioPosition
{
    public int InstrumentId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Sector { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public decimal AllocationAmount { get; set; }
    public float AllocationPercent { get; set; }
    public float ExpectedReturn { get; set; }
    public float RiskScore { get; set; }
    public float SuccessProbability { get; set; }
    public decimal? Entry { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? Target { get; set; }
    public float PositionSizeMultiplier { get; set; }
}