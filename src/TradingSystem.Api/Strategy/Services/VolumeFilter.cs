using Microsoft.Extensions.Logging;
using TradingSystem.Api.Strategy.Interfaces;
using TradingSystem.Api.Strategy.Models;
using TradingSystem.Core.Models;

namespace TradingSystem.Api.Strategy.Services;

public sealed class VolumeFilter : IVolumeFilter
{
    private readonly IMarketDataService _marketData;
    private readonly ILogger<VolumeFilter> _logger;

    public VolumeFilter(IMarketDataService marketData, ILogger<VolumeFilter> logger)
    {
        _marketData = marketData;
        _logger = logger;
    }

    public async Task<bool> IsConfirmedAsync(
        string symbol,
        Candle breakoutCandle,
        int lookback,
        CancellationToken ct = default)
    {
        var avgVolume = await _marketData.GetVolumeAvgAsync(symbol, lookback, TimeFrame.OneMin);

        if (avgVolume <= 0)
        {
            _logger.LogDebug("[VOLUME] Cannot compute average — insufficient history for {Symbol}", symbol);
            return false;
        }

        bool confirmed = breakoutCandle.Volume > avgVolume;

        if (!confirmed)
        {
            _logger.LogInformation(
                "[VOLUME] Breakout volume {Actual} not above avg {Avg} — signal rejected for {Symbol}",
                breakoutCandle.Volume, avgVolume, symbol);
        }

        return confirmed;
    }
}
