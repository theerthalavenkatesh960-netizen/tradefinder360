using Microsoft.Extensions.Logging;
using TradingSystem.Api.Strategy.Interfaces;
using TradingSystem.Api.Strategy.Models;
using TradingSystem.Core.Models;

namespace TradingSystem.Api.Strategy.Services;

public sealed class FvgDetector : IFvgDetector
{
    private readonly ILogger<FvgDetector> _logger;

    public FvgDetector(ILogger<FvgDetector> logger)
    {
        _logger = logger;
    }

    public FairValueGap? Detect(
        IReadOnlyList<Candle> candlesAfterBreakout,
        Direction breakoutDirection,
        decimal minGapPct)
    {
        if (candlesAfterBreakout.Count < 3)
            return null;

        for (int i = 2; i < candlesAfterBreakout.Count; i++)
        {
            var c0 = candlesAfterBreakout[i - 2];
            var c1 = candlesAfterBreakout[i - 1];
            var c2 = candlesAfterBreakout[i];

            if (breakoutDirection == Direction.Bullish)
            {
                if (c0.High < c2.Low)
                {
                    var gapLow = c0.High;
                    var gapHigh = c2.Low;
                    var gapSize = gapHigh - gapLow;

                    if (c1.Close > 0 && gapSize / c1.Close >= minGapPct)
                    {
                        _logger.LogInformation(
                            "[FVG] Bullish FVG detected: GapLow={GapLow} GapHigh={GapHigh} at {Time}",
                            gapLow, gapHigh, c2.Timestamp);

                        return new FairValueGap
                        {
                            Direction = Direction.Bullish,
                            GapLow = gapLow,
                            GapHigh = gapHigh,
                            FormedAt = c2.Timestamp.DateTime
                        };
                    }
                }
            }
            else
            {
                if (c0.Low > c2.High)
                {
                    var gapHigh = c0.Low;
                    var gapLow = c2.High;
                    var gapSize = gapHigh - gapLow;

                    if (c1.Close > 0 && gapSize / c1.Close >= minGapPct)
                    {
                        _logger.LogInformation(
                            "[FVG] Bearish FVG detected: GapHigh={GapHigh} GapLow={GapLow} at {Time}",
                            gapHigh, gapLow, c2.Timestamp);

                        return new FairValueGap
                        {
                            Direction = Direction.Bearish,
                            GapHigh = gapHigh,
                            GapLow = gapLow,
                            FormedAt = c2.Timestamp.DateTime
                        };
                    }
                }
            }
        }

        _logger.LogDebug("[FVG] No valid FVG found in {Count} candles after breakout", candlesAfterBreakout.Count);
        return null;
    }
}
