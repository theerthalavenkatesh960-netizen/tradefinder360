using TradingSystem.Core.Models;
using CoreLimits = TradingSystem.Core.Utilities.CandleDataLimits;

namespace TradingSystem.Api.Helpers;

/// <summary>
/// Thin forwarding wrapper — delegates to <see cref="TradingSystem.Core.Utilities.CandleDataLimits"/>
/// so that controllers can keep using <c>TradingSystem.Api.Helpers</c> imports unchanged.
/// </summary>
public static class CandleDataLimits
{
    public static int GetDefaultDaysBack(InstrumentType instrumentType, int timeframeMinutes)
        => CoreLimits.GetDefaultDaysBack(instrumentType, timeframeMinutes);

    public static int GetMaxDaysBack(InstrumentType instrumentType, int timeframeMinutes)
        => CoreLimits.GetMaxDaysBack(instrumentType, timeframeMinutes);
}
