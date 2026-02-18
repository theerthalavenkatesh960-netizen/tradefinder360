using TradingSystem.Core.Models;

namespace TradingSystem.MarketData;

public class CandleBuilder
{
    private readonly int _timeframeMinutes;
    private Candle? _currentCandle;
    private DateTime _currentCandleStart;

    public CandleBuilder(int timeframeMinutes)
    {
        _timeframeMinutes = timeframeMinutes;
    }

    public Candle? ProcessTick(Tick tick)
    {
        var candleStart = GetCandleStartTime(tick.Timestamp);

        if (_currentCandle == null || candleStart != _currentCandleStart)
        {
            var completedCandle = _currentCandle;

            _currentCandle = new Candle
            {
                Timestamp = candleStart,
                Open = tick.Price,
                High = tick.Price,
                Low = tick.Price,
                Close = tick.Price,
                Volume = tick.Volume,
                TimeframeMinutes = _timeframeMinutes
            };
            _currentCandleStart = candleStart;

            return completedCandle;
        }

        _currentCandle.High = Math.Max(_currentCandle.High, tick.Price);
        _currentCandle.Low = Math.Min(_currentCandle.Low, tick.Price);
        _currentCandle.Close = tick.Price;
        _currentCandle.Volume += tick.Volume;

        return null;
    }

    public Candle? GetCurrentCandle() => _currentCandle;

    private DateTime GetCandleStartTime(DateTime timestamp)
    {
        var totalMinutes = timestamp.Hour * 60 + timestamp.Minute;
        var candleNumber = totalMinutes / _timeframeMinutes;
        var candleStartMinutes = candleNumber * _timeframeMinutes;

        return new DateTime(
            timestamp.Year,
            timestamp.Month,
            timestamp.Day,
            candleStartMinutes / 60,
            candleStartMinutes % 60,
            0
        );
    }
}
