using Microsoft.Extensions.Logging;
using TradingSystem.Core.Models;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Indicators;
using TradingSystem.Scanner.Strategies;

namespace TradingSystem.Scanner.Services;

/// <summary>
/// Service for optimizing portfolio allocation across multiple trading opportunities
/// </summary>
public class PortfolioOptimizationService
{
    private readonly IInstrumentService _instrumentService;
    private readonly ICandleService _candleService;
    private readonly IIndicatorService _indicatorService;
    private readonly IMarketSentimentService _marketSentimentService;
    private readonly ILogger<PortfolioOptimizationService> _logger;
    private readonly Dictionary<StrategyType, ITradingStrategy> _strategies;

    public PortfolioOptimizationService(
        IInstrumentService instrumentService,
        ICandleService candleService,
        IIndicatorService indicatorService,
        IMarketSentimentService marketSentimentService,
        ILogger<PortfolioOptimizationService> logger)
    {
        _instrumentService = instrumentService;
        _candleService = candleService;
        _indicatorService = indicatorService;
        _marketSentimentService = marketSentimentService;
        _logger = logger;

        // Register all strategies
        _strategies = new Dictionary<StrategyType, ITradingStrategy>
        {
            [StrategyType.MOMENTUM] = new MomentumStrategy(),
            [StrategyType.BREAKOUT] = new BreakoutStrategy(),
            [StrategyType.MEAN_REVERSION] = new MeanReversionStrategy(),
            [StrategyType.SWING_TRADING] = new SwingTradingStrategy()
        };
    }

    /// <summary>
    /// Generate optimized portfolio with capital allocation
    /// </summary>
    public async Task<OptimizedPortfolio> OptimizePortfolioAsync(
        PortfolioOptimizationRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting portfolio optimization: Capital={Capital}, MaxRiskPerTrade={MaxRisk}%, MaxPortfolioRisk={PortfolioRisk}%",
            request.TotalCapital, request.MaxRiskPerTradePercent, request.MaxPortfolioRiskPercent);

        ValidateRequest(request);

        // Get market context
        var marketContext = await _marketSentimentService.GetCurrentMarketContextAsync(cancellationToken);

        // Determine which strategies to use
        var strategiesToUse = request.AllowedStrategies.Any()
            ? request.AllowedStrategies
            : _strategies.Keys.ToList();

        // Scan for opportunities across all strategies
        var opportunities = await ScanForOpportunitiesAsync(
            strategiesToUse,
            request.TimeframeMinutes,
            request.MinConfidence,
            marketContext,
            cancellationToken);

        _logger.LogInformation("Found {Count} trading opportunities", opportunities.Count);

        // Rank and filter opportunities
        var rankedOpportunities = RankOpportunities(opportunities, marketContext);

        // Optimize allocation
        var portfolio = BuildOptimizedPortfolio(rankedOpportunities, request);

        // Calculate portfolio health
        portfolio.HealthScore = CalculatePortfolioHealth(portfolio, request);

        _logger.LogInformation(
            "Portfolio optimization complete: {Positions} positions, {Allocated}% allocated, {Risk}% risk",
            portfolio.TotalPositions, portfolio.AllocationPercent, portfolio.TotalRiskPercent);

        return portfolio;
    }

    private async Task<List<TradeOpportunity>> ScanForOpportunitiesAsync(
        List<StrategyType> strategies,
        int timeframeMinutes,
        int minConfidence,
        MarketContext marketContext,
        CancellationToken cancellationToken)
    {
        var opportunities = new List<TradeOpportunity>();
        var instruments = await _instrumentService.GetActiveAsync();

        // Filter to only stocks (not indices)
        var tradableInstruments = instruments
            .Where(i => i.InstrumentType == InstrumentType.STOCK)
            .ToList();

        _logger.LogInformation("Scanning {Count} instruments across {Strategies} strategies",
            tradableInstruments.Count, strategies.Count);

        foreach (var instrument in tradableInstruments)
        {
            try
            {
                // Get candles
                var candles = await _candleService.GetRecentCandlesAsync(
                    instrument.Id,
                    timeframeMinutes,
                    daysBack: 30);

                if (!candles.Any())
                    continue;

                // Get latest indicators
                var latestIndicator = await _indicatorService.GetLatestAsync(
                    instrument.Id,
                    timeframeMinutes);

                if (latestIndicator == null)
                    continue;

                var indicators = MapToIndicatorValues(latestIndicator);

                // Evaluate each strategy
                foreach (var strategyType in strategies)
                {
                    if (!_strategies.TryGetValue(strategyType, out var strategy))
                        continue;

                    if (!strategy.IsInstrumentSuitable(instrument, candles))
                        continue;

                    var signal = strategy.Evaluate(instrument, candles, indicators, marketContext);

                    if (signal.IsValid && signal.Confidence >= minConfidence)
                    {
                        opportunities.Add(new TradeOpportunity
                        {
                            Instrument = instrument,
                            Strategy = strategyType,
                            Signal = signal,
                            Sector = instrument.Sector?.Name ?? "Unknown"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error scanning instrument {Symbol}", instrument.Symbol);
            }
        }

        return opportunities;
    }

    private List<TradeOpportunity> RankOpportunities(
        List<TradeOpportunity> opportunities,
        MarketContext marketContext)
    {
        // Calculate composite score for each opportunity
        foreach (var opp in opportunities)
        {
            var signal = opp.Signal;
            
            // Base score from strategy
            var baseScore = signal.Score;

            // Confidence weight (0-40 points)
            var confidenceScore = (signal.Confidence / 100m) * 40;

            // Risk-reward weight (0-30 points)
            var rrRatio = CalculateRiskRewardRatio(signal);
            var rrScore = Math.Min(rrRatio * 10, 30);

            // Market alignment (0-30 points)
            var marketAlignmentScore = CalculateMarketAlignment(signal, marketContext);

            opp.CompositeScore = baseScore + confidenceScore + rrScore + marketAlignmentScore;
        }

        // Sort by composite score descending
        return opportunities
            .OrderByDescending(o => o.CompositeScore)
            .ThenByDescending(o => o.Signal.Confidence)
            .ToList();
    }

    private OptimizedPortfolio BuildOptimizedPortfolio(
        List<TradeOpportunity> opportunities,
        PortfolioOptimizationRequest request)
    {
        var portfolio = new OptimizedPortfolio
        {
            TotalCapital = request.TotalCapital,
            MaxRiskPerTrade = request.MaxRiskPerTradePercent,
            MaxPortfolioRisk = request.MaxPortfolioRiskPercent,
            GeneratedAt = DateTime.UtcNow
        };

        var remainingCapital = request.TotalCapital;
        var totalRisk = 0m;
        var sectorAllocations = new Dictionary<string, decimal>();
        var selectedPositions = new List<OptimizedPosition>();
        var rejectedOpportunities = new List<RejectedOpportunity>();

        foreach (var opportunity in opportunities)
        {
            // Check if we've reached max positions
            if (selectedPositions.Count >= request.MaxPositions)
            {
                rejectedOpportunities.Add(new RejectedOpportunity
                {
                    Symbol = opportunity.Instrument.Symbol,
                    Strategy = opportunity.Strategy,
                    Confidence = opportunity.Signal.Confidence,
                    RejectionReason = "Maximum position limit reached"
                });
                continue;
            }

            var signal = opportunity.Signal;
            var sector = opportunity.Sector;

            // Calculate position risk
            var riskPerShare = Math.Abs(signal.EntryPrice - signal.StopLoss);
            var riskPercent = signal.EntryPrice > 0 ? (riskPerShare / signal.EntryPrice) * 100 : 0;

            // Calculate position size based on risk
            var maxRiskAmount = request.TotalCapital * (request.MaxRiskPerTradePercent / 100);
            var quantity = riskPerShare > 0 ? (int)(maxRiskAmount / riskPerShare) : 0;

            if (quantity == 0)
            {
                rejectedOpportunities.Add(new RejectedOpportunity
                {
                    Symbol = opportunity.Instrument.Symbol,
                    Strategy = opportunity.Strategy,
                    Confidence = signal.Confidence,
                    RejectionReason = "Risk per share too high for position sizing"
                });
                continue;
            }

            var positionValue = signal.EntryPrice * quantity;
            var positionPercent = (positionValue / request.TotalCapital) * 100;

            // Check minimum position size
            if (positionPercent < request.MinPositionSizePercent)
            {
                rejectedOpportunities.Add(new RejectedOpportunity
                {
                    Symbol = opportunity.Instrument.Symbol,
                    Strategy = opportunity.Strategy,
                    Confidence = signal.Confidence,
                    RejectionReason = $"Position size too small ({positionPercent:F1}% < {request.MinPositionSizePercent}%)"
                });
                continue;
            }

            // Check capital availability
            if (positionValue > remainingCapital)
            {
                rejectedOpportunities.Add(new RejectedOpportunity
                {
                    Symbol = opportunity.Instrument.Symbol,
                    Strategy = opportunity.Strategy,
                    Confidence = signal.Confidence,
                    RejectionReason = "Insufficient remaining capital"
                });
                continue;
            }

            var positionRiskAmount = riskPerShare * quantity;
            var positionRiskPercent = (positionRiskAmount / request.TotalCapital) * 100;

            // Check portfolio risk limit
            if (totalRisk + positionRiskPercent > request.MaxPortfolioRiskPercent)
            {
                rejectedOpportunities.Add(new RejectedOpportunity
                {
                    Symbol = opportunity.Instrument.Symbol,
                    Strategy = opportunity.Strategy,
                    Confidence = signal.Confidence,
                    RejectionReason = $"Would exceed max portfolio risk ({totalRisk + positionRiskPercent:F1}% > {request.MaxPortfolioRiskPercent}%)"
                });
                continue;
            }

            // Check sector diversification
            if (request.EnableSectorDiversification)
            {
                var currentSectorAllocation = sectorAllocations.GetValueOrDefault(sector, 0);
                var newSectorAllocation = currentSectorAllocation + positionPercent;

                if (newSectorAllocation > request.MaxSectorAllocationPercent)
                {
                    rejectedOpportunities.Add(new RejectedOpportunity
                    {
                        Symbol = opportunity.Instrument.Symbol,
                        Strategy = opportunity.Strategy,
                        Confidence = signal.Confidence,
                        RejectionReason = $"Sector allocation limit ({sector}: {newSectorAllocation:F1}% > {request.MaxSectorAllocationPercent}%)"
                    });
                    continue;
                }

                sectorAllocations[sector] = newSectorAllocation;
            }

            // Calculate expected return
            var potentialGain = Math.Abs(signal.Target - signal.EntryPrice);
            var expectedReturn = potentialGain * quantity;
            var expectedReturnPercent = (expectedReturn / positionValue) * 100;
            var riskReward = riskPerShare > 0 ? potentialGain / riskPerShare : 0;

            // Add position to portfolio
            var position = new OptimizedPosition
            {
                InstrumentId = opportunity.Instrument.Id,
                Symbol = opportunity.Instrument.Symbol,
                InstrumentName = opportunity.Instrument.Name,
                Exchange = opportunity.Instrument.Exchange,
                Sector = sector,
                Strategy = opportunity.Strategy,
                Direction = signal.Direction,
                EntryPrice = signal.EntryPrice,
                StopLoss = signal.StopLoss,
                Target = signal.Target,
                AllocatedCapital = positionValue,
                AllocationPercent = positionPercent,
                Quantity = quantity,
                RiskAmount = positionRiskAmount,
                RiskPercent = positionRiskPercent,
                RiskRewardRatio = riskReward,
                Confidence = signal.Confidence,
                Score = signal.Score,
                ExpectedReturn = expectedReturn,
                ExpectedReturnPercent = expectedReturnPercent,
                Signals = signal.Signals,
                Explanation = signal.Explanation
            };

            selectedPositions.Add(position);
            remainingCapital -= positionValue;
            totalRisk += positionRiskPercent;
        }

        // Populate portfolio
        portfolio.Positions = selectedPositions;
        portfolio.RejectedOpportunities = rejectedOpportunities;
        portfolio.AllocatedCapital = selectedPositions.Sum(p => p.AllocatedCapital);
        portfolio.UnallocatedCapital = remainingCapital;
        portfolio.AllocationPercent = (portfolio.AllocatedCapital / request.TotalCapital) * 100;
        portfolio.TotalRiskAmount = selectedPositions.Sum(p => p.RiskAmount);
        portfolio.TotalRiskPercent = totalRisk;
        portfolio.TotalPositions = selectedPositions.Count;
        portfolio.UniqueSectors = sectorAllocations.Count;
        portfolio.SectorAllocation = sectorAllocations;
        portfolio.StrategyDistribution = selectedPositions
            .GroupBy(p => p.Strategy)
            .ToDictionary(g => g.Key, g => g.Count());
        portfolio.TotalExpectedReturn = selectedPositions.Sum(p => p.ExpectedReturn);
        portfolio.TotalExpectedReturnPercent = portfolio.AllocatedCapital > 0
            ? (portfolio.TotalExpectedReturn / portfolio.AllocatedCapital) * 100
            : 0;
        portfolio.AverageConfidence = selectedPositions.Any()
            ? selectedPositions.Average(p => p.Confidence)
            : 0;
        portfolio.AverageRiskReward = selectedPositions.Any()
            ? selectedPositions.Average(p => p.RiskRewardRatio)
            : 0;

        // Add optimization notes
        portfolio.OptimizationNotes = GenerateOptimizationNotes(portfolio, request);

        return portfolio;
    }

    private PortfolioHealthScore CalculatePortfolioHealth(
        OptimizedPortfolio portfolio,
        PortfolioOptimizationRequest request)
    {
        var health = new PortfolioHealthScore();

        if (!portfolio.Positions.Any())
        {
            health.OverallScore = 0;
            health.HealthRating = "POOR";
            health.Concerns.Add("No positions allocated");
            return health;
        }

        // Diversification score (0-40 points)
        var diversificationScore = 0m;
        if (portfolio.UniqueSectors >= 5)
            diversificationScore = 40;
        else if (portfolio.UniqueSectors >= 3)
            diversificationScore = 30;
        else if (portfolio.UniqueSectors >= 2)
            diversificationScore = 20;
        else
            diversificationScore = 10;

        // Check sector concentration
        var maxSectorAlloc = portfolio.SectorAllocation.Values.Any()
            ? portfolio.SectorAllocation.Values.Max()
            : 0;
        if (maxSectorAlloc > 40)
            diversificationScore -= 10;

        health.DiversificationScore = diversificationScore;

        // Risk management score (0-35 points)
        var riskScore = 35m;
        if (portfolio.TotalRiskPercent > request.MaxPortfolioRiskPercent * 0.9m)
            riskScore -= 15;
        if (portfolio.Positions.Any(p => p.RiskPercent > request.MaxRiskPerTradePercent * 0.9m))
            riskScore -= 10;
        if (portfolio.AverageRiskReward < 1.5m)
            riskScore -= 10;

        health.RiskManagementScore = Math.Max(riskScore, 0);

        // Quality score (0-25 points)
        var qualityScore = 0m;
        if (portfolio.AverageConfidence >= 75)
            qualityScore = 25;
        else if (portfolio.AverageConfidence >= 65)
            qualityScore = 20;
        else if (portfolio.AverageConfidence >= 60)
            qualityScore = 15;
        else
            qualityScore = 10;

        health.QualityScore = qualityScore;

        // Overall score
        health.OverallScore = diversificationScore + riskScore + qualityScore;

        // Rating
        health.HealthRating = health.OverallScore switch
        {
            >= 85 => "EXCELLENT",
            >= 70 => "GOOD",
            >= 50 => "FAIR",
            _ => "POOR"
        };

        // Strengths
        if (portfolio.UniqueSectors >= 4)
            health.Strengths.Add($"Well diversified across {portfolio.UniqueSectors} sectors");
        if (portfolio.AverageRiskReward >= 2.0m)
            health.Strengths.Add($"Strong risk-reward ratio: {portfolio.AverageRiskReward:F2}");
        if (portfolio.AverageConfidence >= 70)
            health.Strengths.Add($"High average confidence: {portfolio.AverageConfidence:F1}%");
        if (portfolio.TotalRiskPercent <= request.MaxPortfolioRiskPercent * 0.7m)
            health.Strengths.Add("Conservative risk management");

        // Concerns
        if (portfolio.UniqueSectors < 3)
            health.Concerns.Add("Limited sector diversification");
        if (portfolio.TotalRiskPercent > request.MaxPortfolioRiskPercent * 0.85m)
            health.Concerns.Add("High portfolio risk utilization");
        if (portfolio.AverageConfidence < 65)
            health.Concerns.Add("Below average signal confidence");
        if (maxSectorAlloc > 35)
            health.Concerns.Add($"High concentration in {portfolio.SectorAllocation.OrderByDescending(x => x.Value).First().Key} sector");
        if (portfolio.AllocationPercent < 50)
            health.Concerns.Add("Low capital utilization");

        return health;
    }

    private List<string> GenerateOptimizationNotes(
        OptimizedPortfolio portfolio,
        PortfolioOptimizationRequest request)
    {
        var notes = new List<string>();

        notes.Add($"Portfolio optimized with {portfolio.TotalPositions} positions across {portfolio.UniqueSectors} sectors");
        notes.Add($"Total capital allocation: {portfolio.AllocationPercent:F1}% ({portfolio.AllocatedCapital:C0} allocated, {portfolio.UnallocatedCapital:C0} reserved)");
        notes.Add($"Portfolio risk: {portfolio.TotalRiskPercent:F2}% of total capital (limit: {request.MaxPortfolioRiskPercent}%)");
        notes.Add($"Average position size: {(portfolio.AllocatedCapital / portfolio.TotalPositions):C0}");
        notes.Add($"Expected return: {portfolio.TotalExpectedReturnPercent:F1}% ({portfolio.TotalExpectedReturn:C0})");

        if (portfolio.RejectedOpportunities.Any())
        {
            var rejectionReasons = portfolio.RejectedOpportunities
                .GroupBy(r => r.RejectionReason)
                .OrderByDescending(g => g.Count())
                .Take(3);

            notes.Add($"Rejected {portfolio.RejectedOpportunities.Count} opportunities:");
            foreach (var reason in rejectionReasons)
            {
                notes.Add($"  - {reason.Count()}x {reason.Key}");
            }
        }

        return notes;
    }

    private decimal CalculateRiskRewardRatio(StrategySignal signal)
    {
        var risk = Math.Abs(signal.EntryPrice - signal.StopLoss);
        var reward = Math.Abs(signal.Target - signal.EntryPrice);
        return risk > 0 ? reward / risk : 0;
    }

    private decimal CalculateMarketAlignment(StrategySignal signal, MarketContext marketContext)
    {
        var score = 15m; // Base neutral score

        // Check direction alignment
        if (signal.Direction == "BUY" && marketContext.Sentiment == SentimentType.BULLISH)
            score += 10;
        else if (signal.Direction == "SELL" && marketContext.Sentiment == SentimentType.BEARISH)
            score += 10;
        else if (signal.Direction == "BUY" && marketContext.Sentiment == SentimentType.BEARISH)
            score -= 5;
        else if (signal.Direction == "SELL" && marketContext.Sentiment == SentimentType.BULLISH)
            score -= 5;

        // Volatility alignment
        if (marketContext.VolatilityIndex < 15)
            score += 5; // Low volatility is good
        else if (marketContext.VolatilityIndex > 25)
            score -= 5; // High volatility is risky

        return Math.Max(Math.Min(score, 30), 0);
    }

    private IndicatorValues MapToIndicatorValues(IndicatorSnapshot snapshot)
    {
        return new IndicatorValues
        {
            EMAFast = snapshot.EMAFast,
            EMASlow = snapshot.EMASlow,
            RSI = snapshot.RSI,
            MacdLine = snapshot.MacdLine,
            MacdSignal = snapshot.MacdSignal,
            MacdHistogram = snapshot.MacdHistogram,
            ADX = snapshot.ADX,
            PlusDI = snapshot.PlusDI,
            MinusDI = snapshot.MinusDI,
            ATR = snapshot.ATR,
            BollingerUpper = snapshot.BollingerUpper,
            BollingerMiddle = snapshot.BollingerMiddle,
            BollingerLower = snapshot.BollingerLower,
            VWAP = snapshot.VWAP
        };
    }

    private void ValidateRequest(PortfolioOptimizationRequest request)
    {
        if (request.TotalCapital <= 0)
            throw new ArgumentException("Total capital must be positive");

        if (request.MaxRiskPerTradePercent <= 0 || request.MaxRiskPerTradePercent > 100)
            throw new ArgumentException("Max risk per trade must be between 0 and 100");

        if (request.MaxPortfolioRiskPercent <= 0 || request.MaxPortfolioRiskPercent > 100)
            throw new ArgumentException("Max portfolio risk must be between 0 and 100");

        if (request.MaxPositions < 1)
            throw new ArgumentException("Max positions must be at least 1");

        if (request.MaxSectorAllocationPercent <= 0 || request.MaxSectorAllocationPercent > 100)
            throw new ArgumentException("Max sector allocation must be between 0 and 100");
    }

    /// <summary>
    /// Internal class to hold trade opportunities during optimization
    /// </summary>
    private class TradeOpportunity
    {
        public TradingInstrument Instrument { get; set; } = null!;
        public StrategyType Strategy { get; set; }
        public StrategySignal Signal { get; set; } = null!;
        public string Sector { get; set; } = string.Empty;
        public decimal CompositeScore { get; set; }
    }
}