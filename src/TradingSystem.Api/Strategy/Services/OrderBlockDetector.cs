using TradingSystem.Api.Strategy.Interfaces;
using TradingSystem.Core.Models;

namespace TradingSystem.Api.Strategy.Services;

/// <summary>
/// Detects Order Blocks based on SMC logic:
/// An OB is the last candle with opposite polarity immediately before the impulse.
/// </summary>
public sealed class OrderBlockDetector : IOrderBlockDetector
{
    public OrderBlock? DetectOrderBlock(Candle impulseCandle, IReadOnlyList<Candle> priorCandles, bool isBullishImpulse)
    {
        if (priorCandles.Count == 0)
            return null;

        // Impulse is bullish (strong up move) → OB should be the last bearish candle before it
        // Impulse is bearish (strong down move) → OB should be the last bullish candle before it
        bool lookingForBullishOb = !isBullishImpulse;

        // Scan backwards through prior candles to find the last occurrence of the target polarity
        for (int i = priorCandles.Count - 1; i >= 0; i--)
        {
            var candle = priorCandles[i];

            // Determine candle color (IsBullish returns true if close > open)
            bool isBullish = candle.Close > candle.Open;

            // Match polarity requirement
            if (isBullish == lookingForBullishOb)
            {
                return new OrderBlock
                {
                    High = candle.High,
                    Low = candle.Low,
                    FormedAt = candle.Timestamp
                };
            }
        }

        // No matching OB found
        return null;
    }
}
