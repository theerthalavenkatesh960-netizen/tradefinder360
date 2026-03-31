using TradingSystem.Core.Models;

namespace TradingSystem.Api.Strategy.Interfaces;

/// <summary>
/// Detects Order Block (OB) zones in SMC strategies.
/// An OB is the last opposite-color candle immediately before a strong impulse candle.
/// </summary>
public interface IOrderBlockDetector
{
    /// <summary>
    /// Finds the Order Block candle given an impulse candle and prior candles.
    /// Returns the high/low range of the OB, or null if not found.
    /// </summary>
    /// <param name="impulseCandle">The strong impulse candle that created the move</param>
    /// <param name="priorCandles">Recent candles before the impulse</param>
    /// <param name="isBullishImpulse">True if impulse is bullish (green candle); false if bearish (red)</param>
    /// <returns>OrderBlock with high/low boundaries, or null if not found</returns>
    OrderBlock? DetectOrderBlock(Candle impulseCandle, IReadOnlyList<Candle> priorCandles, bool isBullishImpulse);
}

/// <summary>
/// Represents an Order Block zone with high and low boundaries.
/// </summary>
public sealed class OrderBlock
{
    /// <summary>High boundary of the OB candle</summary>
    public decimal High { get; set; }

    /// <summary>Low boundary of the OB candle</summary>
    public decimal Low { get; set; }

    /// <summary>Timestamp when the OB candle closed</summary>
    public DateTimeOffset FormedAt { get; set; }

    /// <summary>Body size of the OB candle (High - Low for full range, or Body for just OHLC body)</summary>
    public decimal BodySize => High - Low;
}
