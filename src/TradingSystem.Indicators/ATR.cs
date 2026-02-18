namespace TradingSystem.Indicators;

public class ATR
{
    private readonly int _period;
    private readonly EMA _ema;
    private decimal? _previousClose;

    public ATR(int period)
    {
        if (period <= 0)
            throw new ArgumentException("Period must be greater than 0", nameof(period));

        _period = period;
        _ema = new EMA(period);
    }

    public decimal Calculate(decimal high, decimal low, decimal close)
    {
        decimal trueRange;

        if (_previousClose == null)
        {
            trueRange = high - low;
        }
        else
        {
            var highLow = high - low;
            var highClose = Math.Abs(high - _previousClose.Value);
            var lowClose = Math.Abs(low - _previousClose.Value);

            trueRange = Math.Max(highLow, Math.Max(highClose, lowClose));
        }

        _previousClose = close;
        return _ema.Calculate(trueRange);
    }

    public static decimal[] CalculateSeries(decimal[] highs, decimal[] lows, decimal[] closes, int period)
    {
        if (highs.Length == 0 || lows.Length == 0 || closes.Length == 0)
            return Array.Empty<decimal>();

        if (highs.Length != lows.Length || highs.Length != closes.Length)
            throw new ArgumentException("All arrays must have the same length");

        var atr = new ATR(period);
        var results = new decimal[highs.Length];

        for (int i = 0; i < highs.Length; i++)
        {
            results[i] = atr.Calculate(highs[i], lows[i], closes[i]);
        }

        return results;
    }
}
