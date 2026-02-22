namespace TradingSystem.Indicators;

public class VWAP
{
    private decimal _cumulativeTPV;
    private long _cumulativeVolume;
    private DateTime? _currentDate;

    public decimal Value { get; private set; }

    public void Calculate(decimal typical, long volume, DateTimeOffset timestamp)
    {
        var date = timestamp.Date;

        if (_currentDate == null || date != _currentDate.Value)
        {
            _cumulativeTPV = 0;
            _cumulativeVolume = 0;
            _currentDate = date;
        }

        _cumulativeTPV += typical * volume;
        _cumulativeVolume += volume;

        Value = _cumulativeVolume > 0 ? _cumulativeTPV / _cumulativeVolume : typical;
    }

    public static decimal[] CalculateSeries(decimal[] typicalPrices, long[] volumes, DateTime[] timestamps)
    {
        if (typicalPrices.Length == 0 || volumes.Length == 0 || timestamps.Length == 0)
            return Array.Empty<decimal>();

        if (typicalPrices.Length != volumes.Length || typicalPrices.Length != timestamps.Length)
            throw new ArgumentException("All arrays must have the same length");

        var vwap = new VWAP();
        var results = new decimal[typicalPrices.Length];

        for (int i = 0; i < typicalPrices.Length; i++)
        {
            vwap.Calculate(typicalPrices[i], volumes[i], timestamps[i]);
            results[i] = vwap.Value;
        }

        return results;
    }
}
