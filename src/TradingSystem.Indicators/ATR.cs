namespace TradingSystem.Indicators;

public class ATR
{
    private readonly int _period;
    private decimal? _previousClose;
    private decimal? _smoothedATR;
    private readonly Queue<decimal> _trSeed;

    public ATR(int period)
    {
        if (period <= 0)
            throw new ArgumentException("Period must be greater than 0", nameof(period));

        _period = period;
        _trSeed = new Queue<decimal>(period);
    }

    public decimal Calculate(decimal high, decimal low, decimal close)
    {
        decimal trueRange;

        if (_previousClose == null)
        {
            trueRange = high - low;
            _previousClose = close;
            return 0m; // Return 0 until seeded
        }

        // TrueRange = max(High-Low, abs(High-PrevClose), abs(Low-PrevClose))
        var highLow = high - low;
        var highClose = Math.Abs(high - _previousClose.Value);
        var lowClose = Math.Abs(low - _previousClose.Value);
        trueRange = Math.Max(highLow, Math.Max(highClose, lowClose));
        _previousClose = close;

        // Seed with simple average of first 14 TrueRange values
        if (_smoothedATR == null)
        {
            _trSeed.Enqueue(trueRange);

            if (_trSeed.Count < _period)
            {
                return 0m; // Return 0 until seed complete
            }

            _smoothedATR = _trSeed.Average();
            return _smoothedATR.Value;
        }

        // Wilder's smoothing: ATR = ((PrevATR × 13) + CurrentTR) / 14
        _smoothedATR = ((_smoothedATR.Value * (_period - 1)) + trueRange) / _period;
        return _smoothedATR.Value;
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
