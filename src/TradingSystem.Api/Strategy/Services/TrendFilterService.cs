using Microsoft.Extensions.Logging;
using TradingSystem.Api.Strategy.Interfaces;
using TradingSystem.Api.Strategy.Models;

namespace TradingSystem.Api.Strategy.Services;

public sealed class TrendFilterService : ITrendFilterService
{
    private readonly IMarketDataService _marketData;
    private readonly ILogger<TrendFilterService> _logger;

    public TrendFilterService(IMarketDataService marketData, ILogger<TrendFilterService> logger)
    {
        _marketData = marketData;
        _logger = logger;
    }

    public async Task<bool> IsAlignedAsync(
        string symbol,
        decimal currentPrice,
        Direction direction,
        CancellationToken ct = default)
    {
        decimal currentEma = await _marketData.GetEmaAsync(symbol, 20, TimeFrame.FifteenMin);
        decimal previousEma = await _marketData.GetEmaAsync(symbol, 20, TimeFrame.FifteenMin, barOffset: 1);

        if (currentEma <= 0 || previousEma <= 0)
        {
            _logger.LogDebug("[TREND] EMA unavailable for {Symbol} — filter failed", symbol);
            return false;
        }

        bool slopeUp = currentEma > previousEma;
        bool slopeDown = currentEma < previousEma;

        if (direction == Direction.Bullish)
        {
            bool aligned = currentPrice > currentEma && slopeUp;
            if (!aligned)
                _logger.LogInformation(
                    "[TREND] Bullish not aligned: price={Price} ema={Ema} slopeUp={Slope} for {Symbol}",
                    currentPrice, currentEma, slopeUp, symbol);
            return aligned;
        }
        else
        {
            bool aligned = currentPrice < currentEma && slopeDown;
            if (!aligned)
                _logger.LogInformation(
                    "[TREND] Bearish not aligned: price={Price} ema={Ema} slopeDown={Slope} for {Symbol}",
                    currentPrice, currentEma, slopeDown, symbol);
            return aligned;
        }
    }
}
