using TradingSystem.Api.Strategy.Interfaces;
using TradingSystem.Api.Strategy.Models;
using TradingSystem.Core.Models;

namespace TradingSystem.Api.Strategy.Services;

public sealed class BreakoutDetector : IBreakoutDetector
{
    private static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

    public BreakoutResult? Detect(Candle candle, OpeningRange or, IntraDayStrategyConfig config)
    {
        var candleTimeIst = TimeZoneInfo.ConvertTime(candle.Timestamp, Ist);
        var candleTime = TimeOnly.FromTimeSpan(candleTimeIst.TimeOfDay);

        // Reject candles outside the trade window
        if (candleTime < config.TradeWindowStart || candleTime > config.TradeWindowEnd)
            return null;

        // Bullish: close (not wick) must be strictly above opening range high
        if (candle.Close > or.High)
            return new BreakoutResult { Direction = Direction.Bullish, BreakoutCandle = candle };

        // Bearish: close (not wick) must be strictly below opening range low
        if (candle.Close < or.Low)
            return new BreakoutResult { Direction = Direction.Bearish, BreakoutCandle = candle };

        return null;
    }
}
