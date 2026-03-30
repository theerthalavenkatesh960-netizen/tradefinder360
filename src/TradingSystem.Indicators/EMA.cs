namespace TradingSystem.Indicators;

public class EMA
{
    private readonly int _period;
    private readonly decimal _multiplier;
    private decimal? _previousEma;
    private readonly Queue<decimal> _seedPrices;
    private bool _isSeeded;

    public EMA(int period)
    {
        if (period <= 0)
            throw new ArgumentException("Period must be greater than 0", nameof(period));

        _period = period;
        _multiplier = 2m / (period + 1);
        _seedPrices = new Queue<decimal>(period);
        _isSeeded = false;
    }

    public decimal Calculate(decimal price)
    {
        if (!_isSeeded)
        {
            _seedPrices.Enqueue(price);

            if (_seedPrices.Count < _period)
            {
                return 0m; // Return 0 until seed is complete
            }

            // Seed complete: calculate simple average
            _previousEma = _seedPrices.Average();
            _isSeeded = true;
            return _previousEma.Value;
        }

        // Standard EMA formula: EMA = (Close × k) + (PrevEMA × (1 - k))
        var ema = (price * _multiplier) + (_previousEma.Value * (1 - _multiplier));
        _previousEma = ema;
        return ema;
    }

    public static decimal[] CalculateSeries(decimal[] prices, int period)
    {
        if (prices.Length == 0)
            return Array.Empty<decimal>();

        var ema = new EMA(period);
        var results = new decimal[prices.Length];

        for (int i = 0; i < prices.Length; i++)
        {
            results[i] = ema.Calculate(prices[i]);
        }

        return results;
    }

    public void Reset()
    {
        _previousEma = null;
        _seedPrices.Clear();
        _isSeeded = false;
    }
}
