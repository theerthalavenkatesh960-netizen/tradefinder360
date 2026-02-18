namespace TradingSystem.Indicators;

public class EMA
{
    private readonly int _period;
    private readonly decimal _multiplier;
    private decimal? _previousEma;

    public EMA(int period)
    {
        if (period <= 0)
            throw new ArgumentException("Period must be greater than 0", nameof(period));

        _period = period;
        _multiplier = 2m / (period + 1);
    }

    public decimal Calculate(decimal price)
    {
        if (_previousEma == null)
        {
            _previousEma = price;
            return price;
        }

        var ema = (price - _previousEma.Value) * _multiplier + _previousEma.Value;
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
    }
}
