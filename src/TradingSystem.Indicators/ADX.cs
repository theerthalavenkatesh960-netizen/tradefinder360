namespace TradingSystem.Indicators;

public class ADX
{
    private readonly int _period;
    private decimal? _previousHigh;
    private decimal? _previousLow;
    private decimal? _previousClose;

    // Wilder's smoothing state
    private decimal? _smoothedPlusDM;
    private decimal? _smoothedMinusDM;
    private decimal? _smoothedTR;
    private decimal? _smoothedADX;

    private readonly Queue<decimal> _plusDMSeed;
    private readonly Queue<decimal> _minusDMSeed;
    private readonly Queue<decimal> _trSeed;
    private readonly Queue<decimal> _dxSeed;

    public decimal PlusDI { get; private set; }
    public decimal MinusDI { get; private set; }
    public decimal ADXValue { get; private set; }

    public ADX(int period)
    {
        if (period <= 0)
            throw new ArgumentException("Period must be greater than 0", nameof(period));

        _period = period;
        _plusDMSeed = new Queue<decimal>(period);
        _minusDMSeed = new Queue<decimal>(period);
        _trSeed = new Queue<decimal>(period);
        _dxSeed = new Queue<decimal>(period);
    }

    public void Calculate(decimal high, decimal low, decimal close)
    {
        decimal trueRange;
        decimal plusDM = 0;
        decimal minusDM = 0;

        if (_previousHigh == null || _previousLow == null || _previousClose == null)
        {
            // First candle
            trueRange = high - low;
            _previousHigh = high;
            _previousLow = low;
            _previousClose = close;
            PlusDI = 0;
            MinusDI = 0;
            ADXValue = 0;
            return;
        }

        // ✅ FIXED: Use decimal Max and Abs instead of Math.Max/Math.Abs
        // TrueRange = max(High-Low, abs(High-PrevClose), abs(Low-PrevClose))
        trueRange = Max(high - low, Max(Abs(high - _previousClose.Value), Abs(low - _previousClose.Value)));

        // +DM and -DM calculation
        var upMove = high - _previousHigh.Value;
        var downMove = _previousLow.Value - low;

        if (upMove > downMove && upMove > 0)
            plusDM = upMove;
        if (downMove > upMove && downMove > 0)
            minusDM = downMove;

        _previousHigh = high;
        _previousLow = low;
        _previousClose = close;

        // Build seed for first 14 periods
        if (_smoothedTR == null)
        {
            _trSeed.Enqueue(trueRange);
            _plusDMSeed.Enqueue(plusDM);
            _minusDMSeed.Enqueue(minusDM);

            if (_trSeed.Count < _period)
            {
                PlusDI = 0;
                MinusDI = 0;
                ADXValue = 0;
                return;
            }

            // Initialize with simple average
            _smoothedTR = _trSeed.Average();
            _smoothedPlusDM = _plusDMSeed.Average();
            _smoothedMinusDM = _minusDMSeed.Average();
        }
        else
        {
            // ✅ FIXED: Wilder's smoothing formula corrected
            // Old (WRONG): _smoothedTR = _smoothedTR.Value - (_smoothedTR.Value / _period) + trueRange;
            // New (CORRECT): ((prev * (period-1)) + current) / period
            _smoothedTR = ((_smoothedTR.Value * (_period - 1)) + trueRange) / _period;
            _smoothedPlusDM = ((_smoothedPlusDM.Value * (_period - 1)) + plusDM) / _period;
            _smoothedMinusDM = ((_smoothedMinusDM.Value * (_period - 1)) + minusDM) / _period;
        }

        // +DI = (+DM14 / TR14) × 100
        if (_smoothedTR.Value > 0)
        {
            PlusDI = (_smoothedPlusDM.Value / _smoothedTR.Value) * 100;
            MinusDI = (_smoothedMinusDM.Value / _smoothedTR.Value) * 100;

            var diSum = PlusDI + MinusDI;
            if (diSum > 0)
            {
                // ✅ FIXED: Use decimal Abs
                // DX = abs(+DI - -DI) / (+DI + -DI) × 100
                var dx = Abs(PlusDI - MinusDI) / diSum * 100;

                // Build ADX seed (needs 14 DX values)
                if (_smoothedADX == null)
                {
                    _dxSeed.Enqueue(dx);

                    if (_dxSeed.Count < _period)
                    {
                        ADXValue = 0;
                        return;
                    }

                    // ADX seed complete
                    _smoothedADX = _dxSeed.Average();
                    ADXValue = _smoothedADX.Value;
                }
                else
                {
                    // ✅ FIXED: ADX = Wilder's smoothing of DX
                    // Old (WRONG): _smoothedADX = _smoothedADX.Value - (_smoothedADX.Value / _period) + dx;
                    // New (CORRECT): ((prev * (period-1)) + current) / period
                    _smoothedADX = ((_smoothedADX.Value * (_period - 1)) + dx) / _period;
                    ADXValue = _smoothedADX.Value;
                }
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

    // ✅ ADDED: Decimal helper methods
    private static decimal Abs(decimal value) => value < 0 ? -value : value;
    
    private static decimal Max(decimal a, decimal b) => a > b ? a : b;
}
