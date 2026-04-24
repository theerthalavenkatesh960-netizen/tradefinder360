using System.ComponentModel;
using System.Text.Json;
using TradingSystem.Core.Models;
using ModelContextProtocol.Server;
using TradingSystem.Data.Services.Interfaces;

namespace TradingSystem.Api.McpTools;

/// <summary>
/// MCP Server that exposes stock analysis tools for Claude AI.
/// Tools fetch data from existing services: ICandleService, IIndicatorService, IInstrumentService.
/// </summary>
[McpServerToolType]
public class StockAnalysisTools
{
    private readonly ICandleService _candleService;
    private readonly IIndicatorService _indicatorService;
    private readonly IInstrumentService _instrumentService;

    public StockAnalysisTools(
        ICandleService candleService,
        IIndicatorService indicatorService,
        IInstrumentService instrumentService)
    {
        _candleService = candleService;
        _indicatorService = indicatorService;
        _instrumentService = instrumentService;
    }

    /// <summary>
    /// Tool 1: Fetch recent OHLC candle data for a stock.
    /// </summary>
    [McpServerTool]
    [Description("Fetch recent OHLC candle data for a stock from the database. Returns array of candles with Timestamp, Open, High, Low, Close, Volume.")]
    public async Task<string> GetOhlcData(
        [Description("NSE/BSE ticker symbol (e.g., RELIANCE, INFY)")] string symbol,
        [Description("Timeframe in minutes: 1, 5, 15, 30, 60, 1440 (daily)")] string timeframe,
        [Description("Number of recent candles to fetch (e.g., 20, 50, 100)")] int candleCount)
    {
        try
        {
            // Resolve instrument by symbol
            var instrument = await _instrumentService.GetBySymbolAsync(symbol);
            if (instrument == null)
                return JsonSerializer.Serialize(new { error = $"Symbol '{symbol}' not found", statusCode = 404 });

            // Parse timeframe string to minutes
            if (!int.TryParse(timeframe, out var timeframeMinutes))
            {
                return JsonSerializer.Serialize(new { error = $"Invalid timeframe '{timeframe}'. Use minutes (1, 5, 15, 30, 60, 1440)", statusCode = 400 });
            }

            // Fetch candles
            var toDate = DateTime.Today.AddDays(1);
            var fromDate = DateTime.Today.AddDays(-30); // Look back 30 days for flexibility
            var allCandles = await _candleService.GetCandlesFromDbAsync(instrument.Id, timeframeMinutes, fromDate, toDate);

            // Return last N candles
            var recentCandles = allCandles.TakeLast(candleCount).Select(c => new
            {
                c.Timestamp,
                c.Open,
                c.High,
                c.Low,
                c.Close,
                c.Volume
            }).ToList();

            if (!recentCandles.Any())
                return JsonSerializer.Serialize(new { warning = "No candle data found for this symbol and timeframe", data = new List<object>() });

            return JsonSerializer.Serialize(new { data = recentCandles, count = recentCandles.Count });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message, statusCode = 500 });
        }
    }

    /// <summary>
    /// Tool 2: Get latest indicator snapshot for a stock.
    /// Returns all technical indicators: EMA, RSI, MACD, ADX, ATR, Bollinger Bands, VWAP.
    /// </summary>
    [McpServerTool]
    [Description("Get latest technical indicator values for a stock: EMA (fast/slow), RSI, MACD, ADX, ATR, Bollinger Bands, VWAP. Useful for momentum and trend analysis.")]
    public async Task<string> GetIndicatorSnapshot(
        [Description("NSE/BSE ticker symbol (e.g., RELIANCE)")] string symbol,
        [Description("Timeframe in minutes: 1, 5, 15, 30, 60, 1440")] string timeframe)
    {
        try
        {
            // Resolve instrument
            var instrument = await _instrumentService.GetBySymbolAsync(symbol);
            if (instrument == null)
                return JsonSerializer.Serialize(new { error = $"Symbol '{symbol}' not found", statusCode = 404 });

            if (!int.TryParse(timeframe, out var timeframeMinutes))
                return JsonSerializer.Serialize(new { error = $"Invalid timeframe '{timeframe}'", statusCode = 400 });

            // Get latest indicator snapshot
            var snapshot = await _indicatorService.GetLatestAsync(instrument.Id, timeframeMinutes);
            if (snapshot == null)
                return JsonSerializer.Serialize(new { error = "No indicator data available yet", statusCode = 404 });

            var result = new
            {
                timestamp = snapshot.Timestamp,
                emaFast = snapshot.EMAFast,
                emaSlow = snapshot.EMASlow,
                rsi = snapshot.RSI,
                macdLine = snapshot.MacdLine,
                macdSignal = snapshot.MacdSignal,
                macdHistogram = snapshot.MacdHistogram,
                adx = snapshot.ADX,
                plusDI = snapshot.PlusDI,
                minusDI = snapshot.MinusDI,
                atr = snapshot.ATR,
                bollingerUpper = snapshot.BollingerUpper,
                bollingerMiddle = snapshot.BollingerMiddle,
                bollingerLower = snapshot.BollingerLower,
                vwap = snapshot.VWAP
            };

            return JsonSerializer.Serialize(new { data = result });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message, statusCode = 500 });
        }
    }

    /// <summary>
    /// Tool 3: Get support and resistance levels derived from Bollinger Bands and recent swings.
    /// </summary>
    [McpServerTool]
    [Description("Get key support and resistance price levels for a stock. Derives levels from Bollinger Bands and recent swing highs/lows.")]
    public async Task<string> GetSupportResistanceLevels(
        [Description("NSE/BSE ticker symbol")] string symbol,
        [Description("Timeframe in minutes")] string timeframe)
    {
        try
        {
            // Resolve instrument
            var instrument = await _instrumentService.GetBySymbolAsync(symbol);
            if (instrument == null)
                return JsonSerializer.Serialize(new { error = $"Symbol '{symbol}' not found", statusCode = 404 });

            if (!int.TryParse(timeframe, out var timeframeMinutes))
                return JsonSerializer.Serialize(new { error = $"Invalid timeframe '{timeframe}'", statusCode = 400 });

            // Get latest indicator snapshot for Bollinger Bands
            var indicator = await _indicatorService.GetLatestAsync(instrument.Id, timeframeMinutes);
            if (indicator == null)
                return JsonSerializer.Serialize(new { error = "No indicator data available", statusCode = 404 });

            // Get recent candles to find swing highs/lows
            var toDate = DateTime.Today.AddDays(1);
            var fromDate = DateTime.Today.AddDays(-20);
            var candles = await _candleService.GetCandlesFromDbAsync(instrument.Id, timeframeMinutes, fromDate, toDate);

            if (!candles.Any())
                return JsonSerializer.Serialize(new { error = "No candle data found", statusCode = 404 });

            var lastCandle = candles.Last();
            var currentPrice = lastCandle.Close;

            // Derive S/R from Bollinger Bands and swing levels
            var support = indicator.BollingerLower > 0 ? indicator.BollingerLower : candles.Min(c => c.Low);
            var resistance = indicator.BollingerUpper > 0 ? indicator.BollingerUpper : candles.Max(c => c.High);

            // Calculate distance percentages
            var supportDistance = Math.Abs(currentPrice - support) / support * 100;
            var resistanceDistance = Math.Abs(currentPrice - resistance) / resistance * 100;

            var result = new
            {
                currentPrice,
                support = Math.Round(support, 2),
                resistance = Math.Round(resistance, 2),
                supportDistance = Math.Round(supportDistance, 2),
                resistanceDistance = Math.Round(resistanceDistance, 2),
                source = "Bollinger Bands + Swing Analysis"
            };

            return JsonSerializer.Serialize(new { data = result });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message, statusCode = 500 });
        }
    }

    /// <summary>
    /// Tool 4: Get stock summary with metadata and latest price.
    /// Note: Backtest summary tool skipped — BacktestRunnerService is not designed for query API.
    /// </summary>
    [McpServerTool]
    [Description("Get high-level summary of a stock: name, exchange, latest price, sector, market cap, and other metadata.")]
    public async Task<string> GetStockSummary(
        [Description("NSE/BSE ticker symbol (e.g., RELIANCE)")] string symbol)
    {
        try
        {
            // Get instrument metadata
            var instrument = await _instrumentService.GetBySymbolAsync(symbol);
            if (instrument == null)
                return JsonSerializer.Serialize(new { error = $"Symbol '{symbol}' not found", statusCode = 404 });

            // Get latest candle for price
            var latestCandle = await _candleService.GetLatestCandleAsync(instrument.Id, 1440); // Daily
            var lastClose = latestCandle?.Close ?? 0;

            // Get recent candles for 52-week range (approximated from available data)
            var toDate = DateTime.Today.AddDays(1);
            var fromDate = DateTime.Today.AddDays(-365);
            var yearCandles = await _candleService.GetCandlesFromDbAsync(instrument.Id, 1440, fromDate, toDate);

            var high52w = yearCandles.Any() ? yearCandles.Max(c => c.High) : lastClose;
            var low52w = yearCandles.Any() ? yearCandles.Min(c => c.Low) : lastClose;

            var result = new
            {
                symbol,
                name = instrument.Name,
                exchange = instrument.Exchange,
                sector = instrument.Industry ?? "N/A",
                lastPrice = Math.Round(lastClose, 2),
                marketCap = instrument.MarketCap,
                high52w = Math.Round(high52w, 2),
                low52w = Math.Round(low52w, 2),
                lotSize = instrument.LotSize,
                isin = instrument.ISIN,
                instrumentType = instrument.InstrumentType.ToString()
            };

            return JsonSerializer.Serialize(new { data = result });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message, statusCode = 500 });
        }
    }

    // NOTE: Tool 5 (GetBacktestSummary) is intentionally skipped.
    // Reason: BacktestRunnerService is designed for backtesting execution, not querying historical results.
    // To implement this tool, a dedicated backtest result repository would be needed (e.g., IBacktestResultRepository).
    // This would require schema design for storing backtest results, which is outside the scope of this MCP integration.
}
