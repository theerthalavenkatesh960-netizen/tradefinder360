using TradingSystem.Core.Models;
using TradingSystem.Configuration.Models;

namespace TradingSystem.MarketData;

public class MarketDataEngine
{
    private readonly CandleBuilder _candleBuilder;
    private readonly CandleStore _candleStore;
    private readonly TimeframeConfig _config;

    public event EventHandler<Candle>? OnNewCandle;
    public event EventHandler<Tick>? OnTick;

    public MarketDataEngine(TimeframeConfig config)
    {
        _config = config;
        _candleBuilder = new CandleBuilder(config.ActiveTimeframeMinutes);
        _candleStore = new CandleStore(config.MaxCandleHistory);
    }

    public void ProcessTick(Tick tick)
    {
        OnTick?.Invoke(this, tick);

        var completedCandle = _candleBuilder.ProcessTick(tick);

        if (completedCandle != null)
        {
            _candleStore.AddCandle(completedCandle);
            OnNewCandle?.Invoke(this, completedCandle);
        }
    }

    public void ProcessCandle(Candle candle)
    {
        _candleStore.AddCandle(candle);
        OnNewCandle?.Invoke(this, candle);
    }

    public List<Candle> GetCandles(int count = 0) => _candleStore.GetCandles(count);

    public Candle? GetLatestCandle() => _candleStore.GetLatestCandle();

    public bool HasMinimumCandles(int minimum) => _candleStore.HasMinimumCandles(minimum);

    public int GetCandleCount() => _candleStore.Count();

    public decimal[] GetClosePrices(int count = 0) => _candleStore.GetClosePrices(count);

    public decimal[] GetHighPrices(int count = 0) => _candleStore.GetHighPrices(count);

    public decimal[] GetLowPrices(int count = 0) => _candleStore.GetLowPrices(count);

    public long[] GetVolumes(int count = 0) => _candleStore.GetVolumes(count);

    public Candle? GetCurrentCandle() => _candleBuilder.GetCurrentCandle();
}
