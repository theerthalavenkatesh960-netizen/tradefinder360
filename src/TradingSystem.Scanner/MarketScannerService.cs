using TradingSystem.Core.Models;
using TradingSystem.Core.Utilities;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Indicators;
using TradingSystem.Scanner.Models;

namespace TradingSystem.Scanner;

public class MarketScannerService
{
    private readonly IInstrumentService _instrumentService;
    private readonly ICandleService _candleService;
    private readonly IIndicatorService _indicatorService;
    private readonly IScanService _scanService;
    private readonly SetupScoringService _scorer;
    private readonly ScannerConfig _config;

    public MarketScannerService(
        IInstrumentService instrumentService,
        ICandleService candleService,
        IIndicatorService indicatorService,
        IScanService scanService,
        SetupScoringService scorer,
        ScannerConfig config)
    {
        _instrumentService = instrumentService;
        _candleService = candleService;
        _indicatorService = indicatorService;
        _scanService = scanService;
        _scorer = scorer;
        _config = config;
    }

    public async Task<List<ScanResult>> ScanAllAsync(int timeframeMinutes = 15)
    {
        var instruments = await _instrumentService.GetActiveAsync();

        if (_config.ScanInstruments.Count > 0)
            instruments = instruments.Where(i => _config.ScanInstruments.Contains(i.InstrumentKey)).ToList();

        var instrumentIds = instruments.Select(i => i.Id).ToList();
        var latestSnapshots = await _scanService.GetLatestSnapshotsAsync(instrumentIds);
        var latestSnapshotByInstrument = latestSnapshots.ToDictionary(s => s.InstrumentId);

        // Bounded parallel scan — avoids sequential bottleneck while preventing DB/API saturation.
        const int maxConcurrency = 5;
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var tasks = instruments.Select(async instrument =>
        {
            await semaphore.WaitAsync();
            try
            {
                latestSnapshotByInstrument.TryGetValue(instrument.Id, out var lastScan);
                return await ScanInstrumentAsync(instrument, timeframeMinutes, lastScan);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var allResults = await Task.WhenAll(tasks);

        return allResults
            .Where(r => r != null)
            .Cast<ScanResult>()
            .OrderByDescending(r => r.SetupScore)
            .ToList();
    }

    public async Task<ScanResult?> ScanInstrumentAsync(TradingInstrument instrument, int timeframeMinutes = 15, ScanSnapshot? lastScan = null)
    {
        // If a scan exists and it's from the same trading day, return the cached result
        if (lastScan != null && IsSameTradeDay(lastScan.Timestamp))
        {
            return MapSnapshotToResult(lastScan);
        }

        var daysBack = CandleDataLimits.GetDefaultDaysBack(instrument.InstrumentType, timeframeMinutes);
        var toDate = DateTime.Today.AddDays(1);
        var fromDate = DateTime.Today.AddDays(-daysBack);
        var candles = await _candleService.GetCandlesFromDbAsync(instrument.Id, timeframeMinutes, fromDate, toDate);

        if (candles.Count < 50)
            return null;

        var latestIndicator = await _indicatorService.GetLatestAsync(instrument.Id, timeframeMinutes);

        IndicatorValues indicators;
        if (latestIndicator != null)
        {
            indicators = MapToIndicatorValues(latestIndicator);
        }
        else
        {
            var engine = new IndicatorEngine(20, 50, 14, 12, 26, 9, 14, 14, 20, 2.0m);
            indicators = engine.Calculate(candles.Last());
        }

        var result = _scorer.Score(instrument, indicators, candles);
        await PersistScanResultAsync(result);
        return result;
    }

    /// <summary>
    /// Checks if the given timestamp is from the same trading day as today.
    /// Assumes trading occurs Mon-Fri; if today is Monday, considers Friday as same trading day.
    /// </summary>
    private static bool IsSameTradeDay(DateTime lastScanTime)
    {
        var now = DateTime.UtcNow.Date;
        var lastScanDate = lastScanTime.Date;

        // If it's the same calendar date, definitely same trading day
        if (now == lastScanDate)
            return true;

        // If today is before last scan date, it's a different trading day
        if (now < lastScanDate)
            return false;

        // Calculate the difference in days
        var daysDiff = (now - lastScanDate).TotalDays;

        // If less than 1 day apart and within same trading week, check the actual trading days
        if (daysDiff < 1)
            return true;

        // If it's a weekend and last scan was Friday, still same trading session (market hasn't opened)
        if (IsWeekendMarketClosed(now, lastScanDate))
            return true;

        // Different trading day
        return false;
    }

    /// <summary>
    /// Determines if the market is still closed between two dates (e.g., weekend or holiday gap).
    /// </summary>
    private static bool IsWeekendMarketClosed(DateTime now, DateTime lastScanDate)
    {
        // If now is Monday/Tuesday/Wednesday/Thursday/Friday and last scan was earlier in the week
        var nowDayOfWeek = now.DayOfWeek;
        var lastDayOfWeek = lastScanDate.DayOfWeek;

        // If last scan was on Friday and today is Monday, it's a new trading day
        if (lastDayOfWeek == DayOfWeek.Friday && nowDayOfWeek == DayOfWeek.Monday)
            return false;

        // If both are within Mon-Fri, check if crossed weekend
        if (lastDayOfWeek <= DayOfWeek.Friday && nowDayOfWeek > lastDayOfWeek)
            return false; // Moved to a later trading day

        return true;
    }

    /// <summary>
    /// Maps a ScanSnapshot database record back to a ScanResult for caching purposes.
    /// </summary>
    private static ScanResult MapSnapshotToResult(ScanSnapshot snapshot)
    {
        return new ScanResult
        {
            InstrumentId = snapshot.InstrumentId,
            Symbol = snapshot.Instrument?.Symbol ?? string.Empty, // Will be populated by caller if needed
            Exchange = snapshot.Instrument?.Exchange ?? string.Empty,
            MarketState = Enum.TryParse<ScanMarketState>(snapshot.MarketState, out var ms) ? ms : ScanMarketState.SIDEWAYS,
            SetupScore = snapshot.SetupScore,
            Bias = Enum.TryParse<ScanBias>(snapshot.Bias, out var bias) ? bias : ScanBias.NONE,
            LastClose = snapshot.LastClose,
            ATR = snapshot.ATR,
            ScannedAt = snapshot.Timestamp,
            ScoreBreakdown = new ScoreBreakdown
            {
                AdxScore = snapshot.AdxScore,
                RsiScore = snapshot.RsiScore,
                EmaVwapScore = snapshot.EmaVwapScore,
                VolumeScore = snapshot.VolumeScore,
                BollingerScore = snapshot.BollingerScore,
                StructureScore = snapshot.StructureScore
            }
        };
    }

    public async Task<List<ScanResult>> GetTopSetups(int minScore = 70, int limit = 10)
    {
        var snapshots = await _scanService.GetTopAsync(minScore, limit);
        var instrumentList = await _instrumentService.GetActiveAsync();
        var instDict = instrumentList.ToDictionary(i => i.Id, i => new
        {
                Symbol = i.Symbol,
                InstrumentKey = i.InstrumentKey,
                Exchange = i.Exchange
        });

        return snapshots.Select(s =>
        {
            instDict.TryGetValue(s.InstrumentId, out var inst);
            return new ScanResult
            {
                InstrumentId = s.InstrumentId,
                Symbol = inst?.Symbol ?? string.Empty,
                Exchange = inst?.Exchange ?? string.Empty,
                MarketState = Enum.TryParse<ScanMarketState>(s.MarketState, out var ms) ? ms : ScanMarketState.SIDEWAYS,
                SetupScore = s.SetupScore,
                Bias = Enum.TryParse<ScanBias>(s.Bias, out var bias) ? bias : ScanBias.NONE,
                LastClose = s.LastClose,
                ATR = s.ATR,
                ScannedAt = s.Timestamp,
                ScoreBreakdown = new ScoreBreakdown
                {
                    AdxScore = s.AdxScore,
                    RsiScore = s.RsiScore,
                    EmaVwapScore = s.EmaVwapScore,
                    VolumeScore = s.VolumeScore,
                    BollingerScore = s.BollingerScore,
                    StructureScore = s.StructureScore
                }
            };
        }).ToList();
    }

    /// <summary>
    /// Returns top movers (gainers or losers) ranked by intraday change percent derived from
    /// today's first candle open vs latest close. Operates from already-computed snapshots for speed.
    /// </summary>
    public async Task<List<(ScanResult Result, decimal ChangePercent)>> GetMoversAsync(
        int timeframeMinutes = 15,
        int limit = 10,
        bool gainers = true)
    {
        var results = await ScanAllAsync(timeframeMinutes);
        return await GetMoversAsync(results, timeframeMinutes, limit, gainers);
    }

    public async Task<List<(ScanResult Result, decimal ChangePercent)>> GetMoversAsync(
        List<ScanResult> results,
        int timeframeMinutes,
        int limit,
        bool gainers = true)
    {
        var withChangeTasks = results.Select(async r =>
        {
            var toDate = DateTime.Today.AddDays(1);
            var fromDate = DateTime.Today;
            var candles = await _candleService.GetCandlesFromDbAsync(r.InstrumentId, timeframeMinutes, fromDate, toDate);
            if (candles.Count < 2)
                return (r, 0m);

            var todayOpen = candles.First().Open;
            var changePct = todayOpen == 0 ? 0m : (r.LastClose - todayOpen) / todayOpen * 100m;
            return (r, changePct);
        });

        var withChange = await Task.WhenAll(withChangeTasks);

        return gainers
            ? withChange.OrderByDescending(x => x.Item2).Take(limit).ToList()
            : withChange.OrderBy(x => x.Item2).Take(limit).ToList();
    }

    /// <summary>
    /// Returns the top-scoring instruments per exchange/sector group, one per group.
    /// Uses already-computed scan snapshots for fast ranking.
    /// </summary>
    public async Task<List<ScanResult>> GetSectorLeadersAsync(int timeframeMinutes = 15, int perSector = 3)
    {
        var results = await ScanAllAsync(timeframeMinutes);
        return await GetSectorLeadersAsync(results, perSector);
    }

    public Task<List<ScanResult>> GetSectorLeadersAsync(List<ScanResult> results, int perSector = 3)
    {
        return Task.FromResult(results
            .GroupBy(r => r.Exchange)
            .SelectMany(g => g.OrderByDescending(r => r.SetupScore).Take(perSector))
            .OrderByDescending(r => r.SetupScore)
            .ToList());
    }

    /// <summary>
    /// Returns instruments that have broken out of their opening 30-min range (first two 15-min candles).
    /// </summary>
    public async Task<List<(ScanResult Result, decimal OrHigh, decimal OrLow, decimal BreakoutPct, string Direction)>>
        GetBreakoutsAsync(int limit = 20)
    {
        var results = await ScanAllAsync(15);
        return await GetBreakoutsAsync(results, limit);
    }

    public async Task<List<(ScanResult Result, decimal OrHigh, decimal OrLow, decimal BreakoutPct, string Direction)>>
        GetBreakoutsAsync(List<ScanResult> results, int limit = 20)
    {
        var breakouts = new List<(ScanResult, decimal, decimal, decimal, string)>();

        using var semaphore = new SemaphoreSlim(5, 5);
        var tasks = results.Select(async r =>
        {
            await semaphore.WaitAsync();
            try
            {
                var toDate = DateTime.Today.AddDays(1);
                var fromDate = DateTime.Today;
                var candles = await _candleService.GetCandlesFromDbAsync(r.InstrumentId, 15, fromDate, toDate);
                if (candles.Count < 3) return;

                // Opening range = first 2 candles of the session
                var orHigh = candles.Take(2).Max(c => c.High);
                var orLow = candles.Take(2).Min(c => c.Low);
                var lastClose = r.LastClose;

                if (lastClose > orHigh && orHigh > 0)
                {
                    var pct = (lastClose - orHigh) / orHigh * 100m;
                    lock (breakouts) breakouts.Add((r, orHigh, orLow, pct, "LONG"));
                }
                else if (lastClose < orLow && orLow > 0)
                {
                    var pct = (orLow - lastClose) / orLow * 100m;
                    lock (breakouts) breakouts.Add((r, orHigh, orLow, pct, "SHORT"));
                }
            }
            finally { semaphore.Release(); }
        });

        await Task.WhenAll(tasks);

        return breakouts
            .OrderByDescending(b => b.Item4)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Returns instruments near key support or resistance levels (within threshold%).
    /// Approximates S/R using Bollinger bands and recent swing highs/lows from snapshot data.
    /// </summary>
    public async Task<List<(ScanResult Result, decimal Level, decimal DistancePct, string LevelType)>>
        GetNearSRAsync(int timeframeMinutes = 15, decimal thresholdPct = 1.5m, int limit = 20)
    {
        var results = await ScanAllAsync(timeframeMinutes);
        return await GetNearSRAsync(results, timeframeMinutes, thresholdPct, limit);
    }

    public async Task<List<(ScanResult Result, decimal Level, decimal DistancePct, string LevelType)>>
        GetNearSRAsync(List<ScanResult> results, int timeframeMinutes, decimal thresholdPct = 1.5m, int limit = 20)
    {
        var near = new List<(ScanResult, decimal, decimal, string)>();

        using var semaphore = new SemaphoreSlim(5, 5);
        var tasks = results.Select(async r =>
        {
            await semaphore.WaitAsync();
            try
            {
                var indicator = await _indicatorService.GetLatestAsync(r.InstrumentId, timeframeMinutes);
                if (indicator == null) return;

                var price = r.LastClose;

                // Use Bollinger bands as S/R approximation
                var support = indicator.BollingerLower;
                var resistance = indicator.BollingerUpper;

                if (support > 0)
                {
                    var distPct = Math.Abs(price - support) / support * 100m;
                    if (distPct <= thresholdPct)
                        lock (near) near.Add((r, support, distPct, "SUPPORT"));
                }

                if (resistance > 0)
                {
                    var distPct = Math.Abs(price - resistance) / resistance * 100m;
                    if (distPct <= thresholdPct)
                        lock (near) near.Add((r, resistance, distPct, "RESISTANCE"));
                }
            }
            finally { semaphore.Release(); }
        });

        await Task.WhenAll(tasks);

        return near
            .OrderBy(x => x.Item3)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Returns instruments showing recognizable candlestick patterns on the latest candle.
    /// Detects: Hammer, InvertedHammer, Engulfing (Bull/Bear), Doji, MorningStar, EveningStar.
    /// </summary>
    public async Task<List<(ScanResult Result, string PatternName, string Direction, int Confidence)>>
        GetPatternsAsync(int timeframeMinutes = 15, int limit = 20)
    {
        var results = await ScanAllAsync(timeframeMinutes);
        return await GetPatternsAsync(results, timeframeMinutes, limit);
    }

    public async Task<List<(ScanResult Result, string PatternName, string Direction, int Confidence)>>
        GetPatternsAsync(List<ScanResult> results, int timeframeMinutes, int limit = 20)
    {
        var patterns = new List<(ScanResult, string, string, int)>();

        using var semaphore = new SemaphoreSlim(5, 5);
        var tasks = results.Select(async r =>
        {
            await semaphore.WaitAsync();
            try
            {
                var toDate = DateTime.Today.AddDays(1);
                var fromDate = DateTime.Today.AddDays(-3);
                var candles = await _candleService.GetCandlesFromDbAsync(r.InstrumentId, timeframeMinutes, fromDate, toDate);
                if (candles.Count < 3) return;

                var detected = DetectCandlePatterns(candles);
                foreach (var (name, dir, conf) in detected)
                    lock (patterns) patterns.Add((r, name, dir, conf));
            }
            finally { semaphore.Release(); }
        });

        await Task.WhenAll(tasks);

        return patterns
            .OrderByDescending(p => p.Item4)
            .ThenByDescending(p => p.Item1.SetupScore)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Batch-loads daily candles for multiple instruments and returns as a dictionary.
    /// This allows section methods to reuse candles without repeated queries.
    /// Key = InstrumentId, Value = list of Candle entities sorted by timestamp (oldest first)
    /// </summary>
    public async Task<Dictionary<int, List<Candle>>> BatchGetDailyCandles(
        List<ScanResult> results,
        int daysBack = 5)
    {
        if (results.Count == 0)
            return new Dictionary<int, List<Candle>>();

        var toDate = DateTime.Today.AddDays(1);
        var fromDate = DateTime.Today.AddDays(-daysBack);
        var candlesByInstrument = new Dictionary<int, List<Candle>>();

        // Query daily (1440-min) candles for all instruments concurrently
        using var semaphore = new SemaphoreSlim(3, 3);
        var tasks = results.Select(async r =>
        {
            await semaphore.WaitAsync();
            try
            {
                var candles = await _candleService.GetCandlesFromDbAsync(r.InstrumentId, 1440, fromDate, toDate);
                lock (candlesByInstrument)
                {
                    candlesByInstrument[r.InstrumentId] = candles.OrderBy(c => c.Timestamp).ToList();
                }
            }
            finally { semaphore.Release(); }
        });

        await Task.WhenAll(tasks);
        return candlesByInstrument;
    }

    // ---- Pattern detection helpers ----

    private static List<(string Name, string Direction, int Confidence)> DetectCandlePatterns(List<Candle> candles)
    {
        var results = new List<(string, string, int)>();
        if (candles.Count < 2) return results;

        var c0 = candles[^1]; // latest
        var c1 = candles[^2]; // previous

        var range0 = c0.Range;
        if (range0 == 0) return results;

        var body0 = c0.BodySize;
        var lowerWick0 = c0.LowerWick;
        var upperWick0 = c0.UpperWick;

        // Hammer: small body at top, lower wick >= 2x body, upper wick small
        if (lowerWick0 >= body0 * 2 && upperWick0 <= body0 * 0.5m && body0 >= range0 * 0.1m)
            results.Add(("Hammer", "BULLISH", 65));

        // Inverted Hammer: small body at bottom, upper wick >= 2x body
        if (upperWick0 >= body0 * 2 && lowerWick0 <= body0 * 0.5m && body0 >= range0 * 0.1m)
            results.Add(("InvertedHammer", c0.IsBullish ? "BULLISH" : "BEARISH", 55));

        // Doji: body is very small relative to range
        if (body0 <= range0 * 0.1m)
            results.Add(("Doji", "NEUTRAL", 50));

        // Bullish Engulfing: previous bearish candle, current bullish and engulfs previous body
        if (c1.IsBearish && c0.IsBullish && c0.Open <= c1.Close && c0.Close >= c1.Open)
            results.Add(("BullishEngulfing", "BULLISH", 75));

        // Bearish Engulfing: previous bullish candle, current bearish and engulfs previous body
        if (c1.IsBullish && c0.IsBearish && c0.Open >= c1.Close && c0.Close <= c1.Open)
            results.Add(("BearishEngulfing", "BEARISH", 75));

        if (candles.Count >= 3)
        {
            var c2 = candles[^3];
            // Morning Star: bearish, small body, bullish close above midpoint of c2
            if (c2.IsBearish && c1.BodySize <= c1.Range * 0.3m && c0.IsBullish
                && c0.Close > (c2.Open + c2.Close) / 2)
                results.Add(("MorningStar", "BULLISH", 80));

            // Evening Star: bullish, small body, bearish close below midpoint of c2
            if (c2.IsBullish && c1.BodySize <= c1.Range * 0.3m && c0.IsBearish
                && c0.Close < (c2.Open + c2.Close) / 2)
                results.Add(("EveningStar", "BEARISH", 80));
        }

        return results;
    }

    private async Task PersistScanResultAsync(ScanResult result)
    {
        var snapshot = new ScanSnapshot
        {
            InstrumentId = result.InstrumentId,
            Timestamp = result.ScannedAt,
            MarketState = result.MarketState.ToString(),
            SetupScore = result.SetupScore,
            Bias = result.Bias.ToString(),
            AdxScore = result.ScoreBreakdown.AdxScore,
            RsiScore = result.ScoreBreakdown.RsiScore,
            EmaVwapScore = result.ScoreBreakdown.EmaVwapScore,
            VolumeScore = result.ScoreBreakdown.VolumeScore,
            BollingerScore = result.ScoreBreakdown.BollingerScore,
            StructureScore = result.ScoreBreakdown.StructureScore,
            LastClose = result.LastClose,
            ATR = result.ATR,
            CreatedAt = DateTime.UtcNow
        };

        await _scanService.SaveAsync(snapshot);
    }

    private static IndicatorValues MapToIndicatorValues(IndicatorSnapshot s) => new()
    {
        Timestamp = s.Timestamp,
        EMAFast = s.EMAFast,
        EMASlow = s.EMASlow,
        RSI = s.RSI,
        MacdLine = s.MacdLine,
        MacdSignal = s.MacdSignal,
        MacdHistogram = s.MacdHistogram,
        ADX = s.ADX,
        PlusDI = s.PlusDI,
        MinusDI = s.MinusDI,
        ATR = s.ATR,
        BollingerUpper = s.BollingerUpper,
        BollingerMiddle = s.BollingerMiddle,
        BollingerLower = s.BollingerLower,
        VWAP = s.VWAP
    };
}
