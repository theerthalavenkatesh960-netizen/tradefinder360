using Microsoft.Extensions.Logging;
using TradingSystem.Api.Strategy.Interfaces;
using TradingSystem.Api.Strategy.Models;

namespace TradingSystem.Api.Strategy.Services;

public sealed class RsiFilter : IRsiFilter
{
    private readonly IMarketDataService _marketData;
    private readonly ILogger<RsiFilter> _logger;

    public RsiFilter(IMarketDataService marketData, ILogger<RsiFilter> logger)
    {
        _marketData = marketData;
        _logger = logger;
    }

    public async Task<(bool Passed, decimal RsiValue)> EvaluateAsync(
        string symbol,
        Direction direction,
        IntraDayStrategyConfig config,
        CancellationToken ct = default)
    {
        decimal rsi = await _marketData.GetRsiAsync(symbol, config.RsiPeriod, TimeFrame.FiveMin);

        if (rsi <= 0)
        {
            _logger.LogDebug("[RSI] Value unavailable for {Symbol}", symbol);
            return (false, 0m);
        }

        bool passed = direction == Direction.Bullish
            ? rsi > config.RsiBullThreshold
            : rsi < config.RsiBearThreshold;

        if (!passed)
            _logger.LogDebug(
                "[RSI] Soft fail: RSI={Rsi} threshold={Threshold} direction={Dir} for {Symbol}",
                rsi,
                direction == Direction.Bullish ? config.RsiBullThreshold : config.RsiBearThreshold,
                direction,
                symbol);

        return (passed, rsi);
    }
}
