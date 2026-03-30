namespace TradingSystem.Indicators;

public class BollingerBands
{
    private readonly int _period;
    private readonly decimal _stdDevMultiplier;
    private readonly Queue<decimal> _priceWindow;

    public decimal MiddleBand { get; private set; }
    public decimal UpperBand { get; private set; }
    public decimal LowerBand { get; private set; }
    public decimal BandWidth { get; private set; }

    public BollingerBands(int period, decimal stdDevMultiplier = 2.0m)
    {
        if (period <= 0)
            throw new ArgumentException("Period must be greater than 0", nameof(period));

        _period = period;
        _stdDevMultiplier = stdDevMultiplier;
        _priceWindow = new Queue<decimal>(period);
    }

    public void Calculate(decimal price)
    {
        _priceWindow.Enqueue(price);

        if (_priceWindow.Count > _period)
            _priceWindow.Dequeue();

        if (_priceWindow.Count < _period)
        {
            MiddleBand = 0;
            UpperBand = 0;
            LowerBand = 0;
            BandWidth = 0;
            return;
        }

        // MiddleBand = SMA(20) - simple moving average
        MiddleBand = _priceWindow.Average();

        // ✅ FIXED: Pure decimal standard deviation using decimal Sqrt
        // Population standard deviation (divide by N, not N-1)
        var variance = _priceWindow.Sum(p => (p - MiddleBand) * (p - MiddleBand)) / _period;
        var stdDev = Sqrt(variance);  // Pure decimal sqrt

        UpperBand = MiddleBand + (_stdDevMultiplier * stdDev);
        LowerBand = MiddleBand - (_stdDevMultiplier * stdDev);
        BandWidth = MiddleBand > 0 ? (UpperBand - LowerBand) / MiddleBand : 0;
    }

    public static (decimal[] middle, decimal[] upper, decimal[] lower, decimal[] bandwidth) CalculateSeries(
        decimal[] prices, int period, decimal stdDevMultiplier = 2.0m)
    {
        if (prices.Length == 0)
            return (Array.Empty<decimal>(), Array.Empty<decimal>(), Array.Empty<decimal>(), Array.Empty<decimal>());

        var bb = new BollingerBands(period, stdDevMultiplier);
        var middle = new decimal[prices.Length];
        var upper = new decimal[prices.Length];
        var lower = new decimal[prices.Length];
        var bandwidth = new decimal[prices.Length];

        for (int i = 0; i < prices.Length; i++)
        {
            bb.Calculate(prices[i]);
            middle[i] = bb.MiddleBand;
            upper[i] = bb.UpperBand;
            lower[i] = bb.LowerBand;
            bandwidth[i] = bb.BandWidth;
        }

        return (middle, upper, lower, bandwidth);
    }

    // ✅ ADDED: Decimal square root via Newton-Raphson
    private static decimal Sqrt(decimal value)
    {
        if (value < 0) throw new ArgumentException("Cannot sqrt negative value");
        if (value == 0) return 0;
        
        // Initial estimate using double (one-time conversion for seed only)
        var x = (decimal)Math.Sqrt((double)value);
        var lastX = 0m;
        
        // Refine using pure decimal Newton-Raphson
        while (x != lastX)
        {
            lastX = x;
            x = (x + value / x) / 2m;
        }
        return x;
    }
}
