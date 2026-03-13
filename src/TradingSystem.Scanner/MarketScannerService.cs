using TradingSystem.Core.Models;
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

        var results = new List<ScanResult>();

        foreach (var instrument in instruments)
        {
            var result = await ScanInstrumentAsync(instrument, timeframeMinutes);
            if (result != null)
                results.Add(result);
        }

        return results
            .OrderByDescending(r => r.SetupScore)
            .ToList();
    }

    public async Task<ScanResult?> ScanInstrumentAsync(TradingInstrument instrument, int timeframeMinutes = 15)
    {
        // Check if a recent scan already exists for this instrument
        var lastScan = await _scanService.GetLatestSnapshotAsync(instrument.Id);
        
        // If a scan exists and it's from the same trading day, return the cached result
        if (lastScan != null && IsSameTradeDay(lastScan.Timestamp))
        {
            return MapSnapshotToResult(lastScan);
        }

        var candles = await _candleService.GetRecentCandlesAsync(instrument.Id, timeframeMinutes);

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
            Symbol = string.Empty, // Will be populated by caller if needed
            Exchange = string.Empty,
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
