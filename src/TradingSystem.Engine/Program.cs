using TradingSystem.Core.Models;
using TradingSystem.Engine;

Console.WriteLine("=== TRADING SYSTEM STARTED ===");
Console.WriteLine("Professional Intraday Options Trading Algorithm");
Console.WriteLine("===============================================\n");

var engine = new TradingEngine();

Console.WriteLine("Engine initialized successfully!");
Console.WriteLine("Waiting for market data...\n");

await SimulateMarketData(engine);

Console.WriteLine("\n=== TRADING SYSTEM STOPPED ===");

static async Task SimulateMarketData(TradingEngine engine)
{
    var random = new Random();
    var basePrice = 22000m;
    var timestamp = DateTime.Today.AddHours(9).AddMinutes(15);

    Console.WriteLine("Starting market data simulation...\n");

    for (int i = 0; i < 100; i++)
    {
        var volatility = 50m;
        var change = (decimal)(random.NextDouble() - 0.5) * 2 * volatility;
        basePrice += change;

        var open = basePrice;
        var high = basePrice + (decimal)random.NextDouble() * 20;
        var low = basePrice - (decimal)random.NextDouble() * 20;
        var close = low + (decimal)random.NextDouble() * (high - low);

        var candle = new Candle
        {
            Timestamp = timestamp,
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Volume = random.Next(100000, 1000000),
            TimeframeMinutes = 15
        };

        engine.ProcessCandle(candle);

        var state = engine.GetEngineState();
        var marketState = engine.GetCurrentMarketState();

        Console.WriteLine($"[{timestamp:HH:mm}] Price: {close:F2} | State: {state} | Market: {marketState?.State}");

        if (engine.GetActiveTrade() != null)
        {
            var trade = engine.GetActiveTrade()!;
            Console.WriteLine($"  Active Trade: {trade.Direction} @ {trade.SpotEntryPrice} | SL: {trade.StopLoss} | Target: {trade.Target}");
        }

        timestamp = timestamp.AddMinutes(15);

        await Task.Delay(100);
    }
}
