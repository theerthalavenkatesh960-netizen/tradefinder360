namespace TradingSystem.Indicators;

public class MACD
{
    private readonly int _slowPeriod;
    private readonly EMA _fastEma;
    private readonly EMA _slowEma;
    private readonly EMA _signalEma;
    private int _candleCount;

    public decimal MacdLine { get; private set; }
    public decimal SignalLine { get; private set; }
    public decimal Histogram { get; private set; }

    public MACD(int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9)
    {
        _slowPeriod = slowPeriod;
        _fastEma = new EMA(fastPeriod);
        _slowEma = new EMA(slowPeriod);
        _signalEma = new EMA(signalPeriod);
        _candleCount = 0;
    }

    public void Calculate(decimal price)
    {
        _candleCount++;

        var fastValue = _fastEma.Calculate(price);
        var slowValue = _slowEma.Calculate(price);

        // MacdLine = EMA(12) - EMA(26)
        MacdLine = fastValue - slowValue;

        // SignalLine must not start until MacdLine is valid (after slowPeriod candles)
        if (_candleCount < _slowPeriod)
        {
            SignalLine = 0m;
            Histogram = 0m;
            return;
        }

        // SignalLine = EMA(9) of MacdLine
        SignalLine = _signalEma.Calculate(MacdLine);
        Histogram = MacdLine - SignalLine;
    }

    public static (decimal[] macdLine, decimal[] signalLine, decimal[] histogram) CalculateSeries(
        decimal[] prices, int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9)
    {
        if (prices.Length == 0)
            return (Array.Empty<decimal>(), Array.Empty<decimal>(), Array.Empty<decimal>());

        var macd = new MACD(fastPeriod, slowPeriod, signalPeriod);
        var macdLines = new decimal[prices.Length];
        var signalLines = new decimal[prices.Length];
        var histograms = new decimal[prices.Length];

        for (int i = 0; i < prices.Length; i++)
        {
            macd.Calculate(prices[i]);
            macdLines[i] = macd.MacdLine;
            signalLines[i] = macd.SignalLine;
            histograms[i] = macd.Histogram;
        }

        return (macdLines, signalLines, histograms);
    }
}
