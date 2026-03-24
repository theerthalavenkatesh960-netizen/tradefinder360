using Microsoft.Extensions.Logging;
using TradingSystem.Api.Strategy.Interfaces;
using TradingSystem.Api.Strategy.Models;
using TradingSystem.Core.Models;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Indicators;

namespace TradingSystem.Api.Strategy.Services;

/// <summary>
/// Bridges the strategy engine to existing ICandleService / IIndicatorService / IndicatorEngine.
/// Resolves symbol ? instrumentId internally.
/// </summary>
public sealed class MarketDataService : IMarketDataService
{
    private readonly ICandleService _candleService;
    private readonly IIndicatorService _indicatorService;
    private readonly IInstrumentService _instrumentService;
    private readonly ILogger<MarketDataService> _logger;

    public MarketDataService(
        ICandleService candleService,
        IIndicatorService indicatorService,
        IInstrumentService instrumentService,
        ILogger<MarketDataService> logger)
    {
        _candleService = candleService;
        _indicatorService = indicatorService;
        _instrumentService = instrumentService;
        _logger = logger;
    }

    public async Task<Candle?> GetCandleAsync(string symbol, TimeFrame timeFrame, DateTime fromUtc, DateTime toUtc)
    {
        var instrumentId = await ResolveInstrumentIdAsync(symbol);
        if (instrumentId is null) return null;

        int tfMinutes = ToMinutes(timeFrame);
        var candles = await _candleService.GetCandlesAsync(instrumentId.Value, tfMinutes, fromUtc, toUtc);

        return candles.FirstOrDefault();
    }

    public async Task<long> GetVolumeAvgAsync(string symbol, int lookback, TimeFrame timeFrame)
    {
        var instrumentId = await ResolveInstrumentIdAsync(symbol);
        if (instrumentId is null) return 0;

        int tfMinutes = ToMinutes(timeFrame);
        var candles = await _candleService.GetRecentCandlesAsync(instrumentId.Value, tfMinutes, daysBack: 5);

        if (candles.Count < lookback)
            return 0;

        var recent = candles
            .OrderByDescending(c => c.Timestamp)
            .Take(lookback)
            .ToList();

        return (long)recent.Average(c => c.Volume);
    }

    public async Task<decimal> GetEmaAsync(string symbol, int period, TimeFrame timeFrame, int barOffset = 0)
    {
        var instrumentId = await ResolveInstrumentIdAsync(symbol);
        if (instrumentId is null) return 0m;

        int tfMinutes = ToMinutes(timeFrame);

        // Fetch enough candles to warm up the EMA + provide the offset
        int candlesNeeded = period + 20 + barOffset;
        var candles = await _candleService.GetRecentCandlesAsync(instrumentId.Value, tfMinutes, daysBack: 30);

        if (candles.Count < period)
            return 0m;

        var ordered = candles.OrderBy(c => c.Timestamp).ToList();

        // Run EMA engine across all candles
        var ema = new EMA(period);
        var emaValues = new List<decimal>(ordered.Count);

        foreach (var candle in ordered)
            emaValues.Add(ema.Calculate(candle.Close));

        // barOffset=0 ? last value, barOffset=1 ? second-to-last, etc.
        int targetIndex = emaValues.Count - 1 - barOffset;
        if (targetIndex < 0 || targetIndex >= emaValues.Count)
            return 0m;

        return emaValues[targetIndex];
    }

    public async Task<decimal> GetRsiAsync(string symbol, int period, TimeFrame timeFrame)
    {
        var instrumentId = await ResolveInstrumentIdAsync(symbol);
        if (instrumentId is null) return 0m;

        int tfMinutes = ToMinutes(timeFrame);

        // Try to get from the latest stored indicator snapshot first
        var snapshot = await _indicatorService.GetLatestAsync(instrumentId.Value, tfMinutes);
        if (snapshot is not null && snapshot.RSI > 0)
            return snapshot.RSI;

        // Fallback: calculate from candles
        var candles = await _candleService.GetRecentCandlesAsync(instrumentId.Value, tfMinutes, daysBack: 30);
        if (candles.Count < period + 1)
            return 0m;

        var ordered = candles.OrderBy(c => c.Timestamp).ToList();
        var rsi = new RSI(period);
        decimal lastRsi = 0m;

        foreach (var candle in ordered)
            lastRsi = rsi.Calculate(candle.Close);

        return lastRsi;
    }

    private async Task<int?> ResolveInstrumentIdAsync(string symbol)
    {
        // Try exact symbol match first
        var instrument = await _instrumentService.GetBySymbolAsync(symbol);
        if (instrument is not null)
            return instrument.Id;

        // Try key match (e.g. "NSE:NIFTY")
        instrument = await _instrumentService.GetByKeyAsync(symbol);
        if (instrument is not null)
            return instrument.Id;

        _logger.LogWarning("[MarketData] Could not resolve instrument for symbol '{Symbol}'", symbol);
        return null;
    }

    private static int ToMinutes(TimeFrame tf) => tf switch
    {
        TimeFrame.OneMin => 1,
        TimeFrame.FiveMin => 5,
        TimeFrame.FifteenMin => 15,
        _ => 15
    };
}
