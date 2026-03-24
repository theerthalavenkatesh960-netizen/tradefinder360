using TradingSystem.Api.Strategy.Models;
using TradingSystem.Core.Models;

namespace TradingSystem.Api.Strategy.Interfaces;

/// <summary>
/// Abstraction over existing market data services (ICandleService, IIndicatorService)
/// to provide the specific queries the ORB+FVG strategy needs.
/// </summary>
public interface IMarketDataService
{
    /// <summary>
    /// Fetches a single candle for the given symbol, timeframe, and time window.
    /// Returns null if no candle is available in the window.
    /// </summary>
    Task<Candle?> GetCandleAsync(string symbol, TimeFrame timeFrame, DateTime fromUtc, DateTime toUtc);

    /// <summary>
    /// Returns the simple average volume of the prior [lookback] candles at the given timeframe.
    /// Returns 0 if insufficient data.
    /// </summary>
    Task<long> GetVolumeAvgAsync(string symbol, int lookback, TimeFrame timeFrame);

    /// <summary>
    /// Returns the EMA value for the given symbol, period, and timeframe.
    /// barOffset=0 means the current bar; barOffset=1 means the previous bar.
    /// Returns 0 if data is unavailable.
    /// </summary>
    Task<decimal> GetEmaAsync(string symbol, int period, TimeFrame timeFrame, int barOffset = 0);

    /// <summary>
    /// Returns the RSI value for the given symbol, period, and timeframe.
    /// Returns 0 if data is unavailable.
    /// </summary>
    Task<decimal> GetRsiAsync(string symbol, int period, TimeFrame timeFrame);
}
