using TradingSystem.Core.Models;

namespace TradingSystem.MarketState;

public class StructureAnalyzer
{
    public static bool IsBullishStructure(List<Candle> recentCandles, int lookback = 5)
    {
        if (recentCandles.Count < lookback)
            return false;

        var candles = recentCandles.TakeLast(lookback).ToList();
        int higherHighs = 0;
        int higherLows = 0;

        for (int i = 1; i < candles.Count; i++)
        {
            if (candles[i].High > candles[i - 1].High)
                higherHighs++;
            if (candles[i].Low > candles[i - 1].Low)
                higherLows++;
        }

        return higherHighs >= (lookback - 2) && higherLows >= (lookback - 2);
    }

    public static bool IsBearishStructure(List<Candle> recentCandles, int lookback = 5)
    {
        if (recentCandles.Count < lookback)
            return false;

        var candles = recentCandles.TakeLast(lookback).ToList();
        int lowerHighs = 0;
        int lowerLows = 0;

        for (int i = 1; i < candles.Count; i++)
        {
            if (candles[i].High < candles[i - 1].High)
                lowerHighs++;
            if (candles[i].Low < candles[i - 1].Low)
                lowerLows++;
        }

        return lowerHighs >= (lookback - 2) && lowerLows >= (lookback - 2);
    }

    public static bool IsEmaFlat(List<decimal> emaValues, int lookback = 3, decimal flatThreshold = 0.001m)
    {
        if (emaValues.Count < lookback)
            return false;

        var recent = emaValues.TakeLast(lookback).ToList();
        var maxChange = 0m;

        for (int i = 1; i < recent.Count; i++)
        {
            var change = Math.Abs((recent[i] - recent[i - 1]) / recent[i - 1]);
            maxChange = Math.Max(maxChange, change);
        }

        return maxChange < flatThreshold;
    }

    public static int CountEmaCrossovers(List<Candle> candles, List<decimal> emaValues, int lookback = 10)
    {
        if (candles.Count < lookback || emaValues.Count < lookback)
            return 0;

        var recentCandles = candles.TakeLast(lookback).ToList();
        var recentEma = emaValues.TakeLast(lookback).ToList();
        int crossovers = 0;

        for (int i = 1; i < recentCandles.Count; i++)
        {
            var wasAbove = recentCandles[i - 1].Close > recentEma[i - 1];
            var isAbove = recentCandles[i].Close > recentEma[i];

            if (wasAbove != isAbove)
                crossovers++;
        }

        return crossovers;
    }
}
