namespace TradingSystem.Indicators;

public class VWAP
{
    private static readonly TimeZoneInfo IstTimeZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
    private decimal _cumulativeTPV;
    private long _cumulativeVolume;
    private DateTime? _currentDate;

    public decimal Value { get; private set; }

    public void Calculate(decimal typical, long volume, DateTimeOffset timestamp)
    {
        // Convert to IST timezone for date boundary detection
        var istTime = TimeZoneInfo.ConvertTime(timestamp, IstTimeZone);
        var istDate = istTime.Date;

        // Reset at start of each trading day in IST
        if (_currentDate == null || istDate != _currentDate.Value)
        {
            _cumulativeTPV = 0;
            _cumulativeVolume = 0;
            _currentDate = istDate;
        }

        _cumulativeTPV += typical * volume;
        _cumulativeVolume += volume;

        // Handle zero volume edge case
        Value = _cumulativeVolume > 0 ? _cumulativeTPV / _cumulativeVolume : typical;
    }

    public static decimal[] CalculateSeries(decimal[] typicalPrices, long[] volumes, DateTimeOffset[] timestamps)
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
