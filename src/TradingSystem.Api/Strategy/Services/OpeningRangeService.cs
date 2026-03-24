using Microsoft.Extensions.Logging;
using TradingSystem.Api.Strategy.Interfaces;
using TradingSystem.Api.Strategy.Models;

namespace TradingSystem.Api.Strategy.Services;

public sealed class OpeningRangeService : IOpeningRangeService
{
    private readonly IMarketDataService _marketData;
    private readonly ILogger<OpeningRangeService> _logger;

    private static readonly TimeOnly OpeningCandleTime = new TimeOnly(9, 15, 0);

    public OpeningRangeService(IMarketDataService marketData, ILogger<OpeningRangeService> logger)
    {
        _marketData = marketData;
        _logger = logger;
    }

    public async Task<OpeningRange?> CaptureAsync(string symbol, DateOnly date, CancellationToken ct = default)
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var candleStart = TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(date.Year, date.Month, date.Day, 9, 15, 0),
            ist);
        var candleEnd = candleStart.AddMinutes(5);

        var candle = await _marketData.GetCandleAsync(symbol, TimeFrame.FiveMin, candleStart, candleEnd);

        if (candle is null)
        {
            _logger.LogDebug("[OR] No 5-min candle found at 09:15 for {Symbol} on {Date}", symbol, date);
            return null;
        }

        var or = new OpeningRange
        {
            High = candle.High,
            Low = candle.Low,
            CapturedAt = candle.Timestamp.DateTime
        };

        if (or.WidthPct < 0.003m)
        {
            _logger.LogInformation(
                "[OR] Rejected: width {Width:P2} below 0.3% threshold for {Symbol} on {Date}",
                or.WidthPct, symbol, date);
            return null;
        }

        _logger.LogInformation(
            "[OR] Captured for {Symbol}: High={High} Low={Low} Width={Width:P2}",
            symbol, or.High, or.Low, or.WidthPct);

        return or;
    }
}
