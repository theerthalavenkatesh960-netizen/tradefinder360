using Microsoft.Extensions.Logging;
using TradingSystem.Core.Models;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Indicators;
using TradingSystem.Scanner.Strategies;

namespace TradingSystem.Scanner.Services;

/// <summary>
/// Service for managing and executing trading strategies
/// </summary>
public class StrategyService
{
    private readonly Dictionary<StrategyType, ITradingStrategy> _strategies;
    private readonly IInstrumentService _instrumentService;
    private readonly ICandleService _candleService;
    private readonly IIndicatorService _indicatorService;
    private readonly IMarketSentimentService _marketSentimentService;
    private readonly ILogger<StrategyService> _logger;

    public StrategyService(
        IInstrumentService instrumentService,
        ICandleService candleService,
        IIndicatorService indicatorService,
        IMarketSentimentService marketSentimentService,
        ILogger<StrategyService> logger)
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
    /// Get all available strategies
    /// </summary>
    public List<StrategyConfig> GetAvailableStrategies()
    {
        return _strategies.Values.Select(s => new StrategyConfig
        {
            StrategyType = s.StrategyType,
            Name = s.Name,
            Description = s.Description,
            IsActive = true
        }).ToList();
    }

    /// <summary>
    /// Generate recommendations for a specific strategy
    /// </summary>
    public async Task<List<StrategyRecommendation>> GenerateRecommendationsAsync(
        StrategyType strategyType,
        int timeframeMinutes = 15,
        int minConfidence = 60,
        int topCount = 10,
        CancellationToken cancellationToken = default)
    {
        if (!_strategies.TryGetValue(strategyType, out var strategy))
        {
            throw new ArgumentException($"Strategy {strategyType} not found");
        }

        _logger.LogInformation("Generating recommendations for {Strategy}", strategyType);

        var recommendations = new List<StrategyRecommendation>();
        var marketContext = await _marketSentimentService.GetCurrentMarketContextAsync(cancellationToken);
        var instruments = await _instrumentService.GetActiveAsync();

        foreach (var instrument in instruments)
        {
            try
            {
                // Get candles
                var candles = await _candleService.GetRecentCandlesAsync(
                    instrument.Id,
                    timeframeMinutes,
                    daysBack: 30);

                if (!candles.Any() || !strategy.IsInstrumentSuitable(instrument, candles))
                    continue;

                // Get latest indicators
                var latestIndicator = await _indicatorService.GetLatestAsync(
                    instrument.Id,
                    timeframeMinutes);

                if (latestIndicator == null)
                    continue;

                var indicators = MapToIndicatorValues(latestIndicator);

                // Evaluate strategy
                var signal = strategy.Evaluate(instrument, candles, indicators, marketContext);

                if (signal.IsValid && signal.Confidence >= minConfidence)
                {
                    recommendations.Add(new StrategyRecommendation
                    {
                        Instrument = instrument,
                        PrimarySignal = signal,
                        GeneratedAt = DateTimeOffset.UtcNow,
                        ExpiresAt = DateTimeOffset.UtcNow.AddHours(4)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error evaluating {Strategy} for {Symbol}", 
                    strategyType, instrument.Symbol);
            }
        }

        // Sort by score and confidence
        return recommendations
            .OrderByDescending(r => r.PrimarySignal.Score)
            .ThenByDescending(r => r.PrimarySignal.Confidence)
            .Take(topCount)
            .ToList();
    }

    /// <summary>
    /// Evaluate all strategies for a specific instrument
    /// </summary>
    public async Task<List<StrategySignal>> EvaluateAllStrategiesAsync(
        string symbol,
        int timeframeMinutes = 15,
        CancellationToken cancellationToken = default)
    {
        var instrument = await _instrumentService.GetBySymbolAsync(symbol);
        if (instrument == null)
            throw new ArgumentException($"Instrument {symbol} not found");

        var candles = await _candleService.GetRecentCandlesAsync(
            instrument.Id,
            timeframeMinutes,
            daysBack: 30);

        var latestIndicator = await _indicatorService.GetLatestAsync(
            instrument.Id,
            timeframeMinutes);

        if (latestIndicator == null)
            throw new InvalidOperationException($"No indicators available for {symbol}");

        var indicators = MapToIndicatorValues(latestIndicator);
        var marketContext = await _marketSentimentService.GetCurrentMarketContextAsync(cancellationToken);

        var signals = new List<StrategySignal>();

        foreach (var strategy in _strategies.Values)
        {
            if (!strategy.IsInstrumentSuitable(instrument, candles))
                continue;

            var signal = strategy.Evaluate(instrument, candles, indicators, marketContext);
            signals.Add(signal);
        }

        return signals.OrderByDescending(s => s.Score).ToList();
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
}