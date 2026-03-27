using TradingSystem.Core.Models;

namespace TradingSystem.Core.Utilities;

/// <summary>
/// Determines the maximum days of historical candle data available
/// based on instrument type and timeframe.
///
/// Data retention policy:
///   STOCK:  1D candles from 2021 (~1500 days), 15m for ~6 months, 1m for ~3 months
///   INDEX:  1D candles from 2021 (~1500 days), 15m for ~2 years,  1m for ~1.5 years
/// </summary>
public static class CandleDataLimits
{
    /// <summary>
    /// Returns the default (maximum) daysBack for the given instrument type and timeframe.
    /// </summary>
    public static int GetDefaultDaysBack(InstrumentType instrumentType, int timeframeMinutes)
    {
        return instrumentType switch
        {
            InstrumentType.INDEX => timeframeMinutes switch
            {
                >= 375 or 1440 => 1500,  // 1D candles — from 2021
                >= 15          => 730,   // 15m and above intraday — ~2 years
                _              => 548    // 1m, 5m — ~1.5 years
            },
            _ => timeframeMinutes switch  // STOCK (default)
            {
                >= 375 or 1440 => 1500,  // 1D candles — from 2021
                >= 15          => 180,   // 15m and above intraday — ~6 months
                _              => 90     // 1m, 5m — ~3 months
            }
        };
    }

    /// <summary>
    /// Returns the maximum allowed daysBack for validation purposes.
    /// Same as the default — callers should not request more than what is stored.
    /// </summary>
    public static int GetMaxDaysBack(InstrumentType instrumentType, int timeframeMinutes)
    {
        return GetDefaultDaysBack(instrumentType, timeframeMinutes);
    }
}
