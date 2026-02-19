using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;
using TradingSystem.Data;
using TradingSystem.Indicators;
using TradingSystem.Scanner.Models;

namespace TradingSystem.Scanner;

public class MarketScannerService
{
    private readonly TradingDbContext _db;
    private readonly SetupScoringService _scorer;
    private readonly ScannerConfig _config;

    public MarketScannerService(TradingDbContext db, SetupScoringService scorer, ScannerConfig config)
    {
        _db = db;
        _scorer = scorer;
        _config = config;
    }

    public async Task<List<ScanResult>> ScanAllAsync(int timeframeMinutes = 15)
    {
        var instruments = await _db.Instruments
            .Where(i => i.IsActive)
            .ToListAsync();

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
        var candles = await _db.MarketCandles
            .Where(c => c.InstrumentKey == instrument.InstrumentKey && c.TimeframeMinutes == timeframeMinutes)
            .OrderByDescending(c => c.Timestamp)
            .Take(100)
            .OrderBy(c => c.Timestamp)
            .ToListAsync();

        if (candles.Count < 50)
            return null;

        var domainCandles = candles.Select(c => c.ToCandle()).ToList();

        var latestIndicator = await _db.IndicatorSnapshots
            .Where(s => s.InstrumentKey == instrument.InstrumentKey && s.TimeframeMinutes == timeframeMinutes)
            .OrderByDescending(s => s.Timestamp)
            .FirstOrDefaultAsync();

        IndicatorValues indicators;
        if (latestIndicator != null)
        {
            indicators = MapToIndicatorValues(latestIndicator);
        }
        else
        {
            var engine = new IndicatorEngine(20, 50, 14, 12, 26, 9, 14, 14, 20, 2.0m);
            indicators = engine.Calculate(domainCandles.Last());
        }

        var result = _scorer.Score(instrument, indicators, domainCandles);
        await PersistScanResultAsync(result);
        return result;
    }

    public async Task<List<ScanResult>> GetTopSetups(int minScore = 70, int limit = 10)
    {
        var snapshots = await _db.ScanSnapshots
            .Where(s => s.SetupScore >= minScore)
            .GroupBy(s => s.InstrumentKey)
            .Select(g => g.OrderByDescending(s => s.Timestamp).First())
            .OrderByDescending(s => s.SetupScore)
            .Take(limit)
            .ToListAsync();

        var instruments = await _db.Instruments
            .Where(i => i.IsActive)
            .ToDictionaryAsync(i => i.InstrumentKey);

        return snapshots.Select(s =>
        {
            instruments.TryGetValue(s.InstrumentKey, out var inst);
            return new ScanResult
            {
                InstrumentKey = s.InstrumentKey,
                Symbol = inst?.Symbol ?? s.InstrumentKey,
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
            InstrumentKey = result.InstrumentKey,
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

        await _db.ScanSnapshots.AddAsync(snapshot);
        await _db.SaveChangesAsync();
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
