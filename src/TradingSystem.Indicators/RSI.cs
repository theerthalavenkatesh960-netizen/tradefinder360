namespace TradingSystem.Indicators;

public class RSI
{
    private readonly int _period;
    private readonly List<decimal> _prices;
    private decimal? _previousGain;
    private decimal? _previousLoss;
    private decimal? _previousPrice;

    public RSI(int period)
    {
        if (period <= 0)
            throw new ArgumentException("Period must be greater than 0", nameof(period));

        _period = period;
        _prices = new List<decimal>();
    }

    public decimal Calculate(decimal price)
    {
        _prices.Add(price);

        if (_previousPrice == null)
        {
            _previousPrice = price;
            return 0m; // Return 0 until valid (was 50m)
        }

        var change = price - _previousPrice.Value;
        var gain = change > 0 ? change : 0;
        var loss = change < 0 ? -change : 0;

        if (_prices.Count <= _period)
        {
            _previousPrice = price;
            return 0m; // Return 0 until valid (was 50m)
        }

        if (_previousGain == null || _previousLoss == null)
        {
            // First RSI: simple average of gains and losses over first 14 periods
            var initialGains = new List<decimal>();
            var initialLosses = new List<decimal>();

            for (int i = 1; i <= _period && i < _prices.Count; i++)
            {
                var diff = _prices[i] - _prices[i - 1];
                initialGains.Add(diff > 0 ? diff : 0);
                initialLosses.Add(diff < 0 ? -diff : 0);
            }

            _previousGain = initialGains.Average();
            _previousLoss = initialLosses.Average();
        }
        else
        {
            // Wilder's smoothing: AvgGain = ((PrevAvgGain × 13) + CurrentGain) / 14
            _previousGain = (_previousGain.Value * (_period - 1) + gain) / _period;
            _previousLoss = (_previousLoss.Value * (_period - 1) + loss) / _period;
        }

        _previousPrice = price;

        // Edge case: if AvgLoss = 0 → RSI must be exactly 100
        if (_previousLoss.Value == 0)
            return 100m;

        var rs = _previousGain.Value / _previousLoss.Value;
        return 100m - (100m / (1m + rs));
    }

    public static decimal[] CalculateSeries(decimal[] prices, int period)
    {
        if (prices.Length == 0)
            return Array.Empty<decimal>();

        var rsi = new RSI(period);
        var results = new decimal[prices.Length];

        for (int i = 0; i < prices.Length; i++)
        {
            results[i] = rsi.Calculate(prices[i]);
        }

        return results;
    }
}
