using TradingSystem.Core.Models;

namespace TradingSystem.MarketData;

public class CandleStore
{
    private readonly List<Candle> _candles;
    private readonly int _maxHistory;
    private readonly object _lock = new();

    public CandleStore(int maxHistory)
    {
        _maxHistory = maxHistory;
        _candles = new List<Candle>(maxHistory);
    }

    public void AddCandle(Candle candle)
    {
        lock (_lock)
        {
            _candles.Add(candle);

            if (_candles.Count > _maxHistory)
            {
                _candles.RemoveAt(0);
            }
        }
    }

    public List<Candle> GetCandles(int count = 0)
    {
        lock (_lock)
        {
            if (count <= 0 || count >= _candles.Count)
                return new List<Candle>(_candles);

            return _candles.Skip(_candles.Count - count).ToList();
        }
    }

    public Candle? GetLatestCandle()
    {
        lock (_lock)
        {
            return _candles.Count > 0 ? _candles[^1] : null;
        }
    }

    public List<Candle> GetCandlesInRange(DateTime start, DateTime end)
    {
        lock (_lock)
        {
            return _candles
                .Where(c => c.Timestamp >= start && c.Timestamp <= end)
                .ToList();
        }
    }

    public int Count()
    {
        lock (_lock)
        {
            return _candles.Count;
        }
    }

    public bool HasMinimumCandles(int minimum)
    {
        lock (_lock)
        {
            return _candles.Count >= minimum;
        }
    }

    public decimal[] GetClosePrices(int count = 0)
    {
        lock (_lock)
        {
            var candles = count <= 0 ? _candles : _candles.Skip(Math.Max(0, _candles.Count - count)).ToList();
            return candles.Select(c => c.Close).ToArray();
        }
    }

    public decimal[] GetHighPrices(int count = 0)
    {
        lock (_lock)
        {
            var candles = count <= 0 ? _candles : _candles.Skip(Math.Max(0, _candles.Count - count)).ToList();
            return candles.Select(c => c.High).ToArray();
        }
    }

    public decimal[] GetLowPrices(int count = 0)
    {
        lock (_lock)
        {
            var candles = count <= 0 ? _candles : _candles.Skip(Math.Max(0, _candles.Count - count)).ToList();
            return candles.Select(c => c.Low).ToArray();
        }
    }

    public long[] GetVolumes(int count = 0)
    {
        lock (_lock)
        {
            var candles = count <= 0 ? _candles : _candles.Skip(Math.Max(0, _candles.Count - count)).ToList();
            return candles.Select(c => c.Volume).ToArray();
        }
    }
}
