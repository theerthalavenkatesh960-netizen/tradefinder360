namespace TradingSystem.Indicators;

public class ADX
{
    private readonly int _period;
    private readonly ATR _atr;
    private readonly EMA _plusDIEma;
    private readonly EMA _minusDIEma;
    private readonly EMA _adxEma;
    private decimal? _previousHigh;
    private decimal? _previousLow;

    public decimal PlusDI { get; private set; }
    public decimal MinusDI { get; private set; }
    public decimal ADXValue { get; private set; }

    public ADX(int period)
    {
        if (period <= 0)
            throw new ArgumentException("Period must be greater than 0", nameof(period));

        _period = period;
        _atr = new ATR(period);
        _plusDIEma = new EMA(period);
        _minusDIEma = new EMA(period);
        _adxEma = new EMA(period);
    }

    public void Calculate(decimal high, decimal low, decimal close)
    {
        var atr = _atr.Calculate(high, low, close);

        if (_previousHigh == null || _previousLow == null)
        {
            _previousHigh = high;
            _previousLow = low;
            ADXValue = 0;
            return;
        }

        var upMove = high - _previousHigh.Value;
        var downMove = _previousLow.Value - low;

        var plusDM = (upMove > downMove && upMove > 0) ? upMove : 0;
        var minusDM = (downMove > upMove && downMove > 0) ? downMove : 0;

        _previousHigh = high;
        _previousLow = low;

        if (atr > 0)
        {
            PlusDI = _plusDIEma.Calculate((plusDM / atr) * 100);
            MinusDI = _minusDIEma.Calculate((minusDM / atr) * 100);

            var diDiff = Math.Abs(PlusDI - MinusDI);
            var diSum = PlusDI + MinusDI;

            if (diSum > 0)
            {
                var dx = (diDiff / diSum) * 100;
                ADXValue = _adxEma.Calculate(dx);
            }
        }
    }

    public static (decimal[] adx, decimal[] plusDI, decimal[] minusDI) CalculateSeries(
        decimal[] highs, decimal[] lows, decimal[] closes, int period)
    {
        if (highs.Length == 0 || lows.Length == 0 || closes.Length == 0)
            return (Array.Empty<decimal>(), Array.Empty<decimal>(), Array.Empty<decimal>());

        if (highs.Length != lows.Length || highs.Length != closes.Length)
            throw new ArgumentException("All arrays must have the same length");

        var adx = new ADX(period);
        var adxValues = new decimal[highs.Length];
        var plusDIValues = new decimal[highs.Length];
        var minusDIValues = new decimal[highs.Length];

        for (int i = 0; i < highs.Length; i++)
        {
            adx.Calculate(highs[i], lows[i], closes[i]);
            adxValues[i] = adx.ADXValue;
            plusDIValues[i] = adx.PlusDI;
            minusDIValues[i] = adx.MinusDI;
        }

        return (adxValues, plusDIValues, minusDIValues);
    }
}
