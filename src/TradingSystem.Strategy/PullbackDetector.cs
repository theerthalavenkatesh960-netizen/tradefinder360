using TradingSystem.Core.Models;
using TradingSystem.Indicators;

namespace TradingSystem.Strategy;

public class PullbackDetector
{
    public static bool IsBullishPullback(
        List<Candle> recentCandles,
        List<IndicatorValues> recentIndicators,
        int lookback = 3)
    {
        if (recentCandles.Count < lookback + 1 || recentIndicators.Count < lookback + 1)
            return false;

        var latestCandle = recentCandles[^1];
        var latestIndicators = recentIndicators[^1];

        var priceNearEmaFast = IsPriceNearLevel(latestCandle.Close, latestIndicators.EMAFast, 0.003m);
        var priceNearBollingerMid = IsPriceNearLevel(latestCandle.Close, latestIndicators.BollingerMiddle, 0.003m);

        if (!priceNearEmaFast && !priceNearBollingerMid)
            return false;

        var pullbackCandles = recentCandles.Skip(recentCandles.Count - lookback - 1).Take(lookback).ToList();

        var hasLowerVolume = true;
        var avgVolume = recentCandles.Skip(Math.Max(0, recentCandles.Count - 10)).Take(10)
            .Average(c => c.Volume);

        foreach (var candle in pullbackCandles)
        {
            if (candle.Volume > avgVolume * 0.8m)
            {
                hasLowerVolume = false;
                break;
            }
        }

        var hasSmallBodies = pullbackCandles.All(c =>
            c.BodySize < c.Range * 0.6m
        );

        var isStrongEntryCandle = latestCandle.IsBullish &&
                                   latestCandle.BodySize > latestCandle.Range * 0.6m &&
                                   latestCandle.Volume > avgVolume;

        return (hasLowerVolume || hasSmallBodies) && isStrongEntryCandle;
    }

    public static bool IsBearishPullback(
        List<Candle> recentCandles,
        List<IndicatorValues> recentIndicators,
        int lookback = 3)
    {
        if (recentCandles.Count < lookback + 1 || recentIndicators.Count < lookback + 1)
            return false;

        var latestCandle = recentCandles[^1];
        var latestIndicators = recentIndicators[^1];

        var priceNearEmaFast = IsPriceNearLevel(latestCandle.Close, latestIndicators.EMAFast, 0.003m);
        var priceNearBollingerMid = IsPriceNearLevel(latestCandle.Close, latestIndicators.BollingerMiddle, 0.003m);

        if (!priceNearEmaFast && !priceNearBollingerMid)
            return false;

        var pullbackCandles = recentCandles.Skip(recentCandles.Count - lookback - 1).Take(lookback).ToList();

        var hasLowerVolume = true;
        var avgVolume = recentCandles.Skip(Math.Max(0, recentCandles.Count - 10)).Take(10)
            .Average(c => c.Volume);

        foreach (var candle in pullbackCandles)
        {
            if (candle.Volume > avgVolume * 0.8m)
            {
                hasLowerVolume = false;
                break;
            }
        }

        var hasSmallBodies = pullbackCandles.All(c =>
            c.BodySize < c.Range * 0.6m
        );

        var isStrongEntryCandle = latestCandle.IsBearish &&
                                   latestCandle.BodySize > latestCandle.Range * 0.6m &&
                                   latestCandle.Volume > avgVolume;

        return (hasLowerVolume || hasSmallBodies) && isStrongEntryCandle;
    }

    private static bool IsPriceNearLevel(decimal price, decimal level, decimal threshold)
    {
        var diff = Math.Abs(price - level) / level;
        return diff <= threshold;
    }
}
