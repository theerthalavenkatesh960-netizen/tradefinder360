using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using TradingSystem.Api.Strategy.Interfaces;
using TradingSystem.Api.Strategy.Models;
using TradingSystem.Core.Models;

namespace TradingSystem.Api.Strategy.Services;

public sealed class StrategyOrchestrator : IStrategyOrchestrator
{
    private readonly IOpeningRangeService _orService;
    private readonly IBreakoutDetector _breakoutDetector;
    private readonly IVolumeFilter _volumeFilter;
    private readonly IFvgDetector _fvgDetector;
    private readonly ITrendFilterService _trendFilter;
    private readonly IEngulfingConfirmation _engulfing;
    private readonly ISignalGenerator _signalGenerator;
    private readonly ILogger<StrategyOrchestrator> _logger;

    public StrategyOrchestrator(
        IOpeningRangeService orService,
        IBreakoutDetector breakoutDetector,
        IVolumeFilter volumeFilter,
        IFvgDetector fvgDetector,
        ITrendFilterService trendFilter,
        IEngulfingConfirmation engulfing,
        ISignalGenerator signalGenerator,
        ILogger<StrategyOrchestrator> logger)
    {
        _orService = orService;
        _breakoutDetector = breakoutDetector;
        _volumeFilter = volumeFilter;
        _fvgDetector = fvgDetector;
        _trendFilter = trendFilter;
        _engulfing = engulfing;
        _signalGenerator = signalGenerator;
        _logger = logger;
    }

    public async IAsyncEnumerable<TradeSignal> RunAsync(
        string symbol,
        IAsyncEnumerable<Candle> oneMinCandles,
        DateOnly sessionDate,
        IntraDayStrategyConfig config,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        OpeningRange? or = await _orService.CaptureAsync(symbol, sessionDate, ct);
        if (or is null)
        {
            _logger.LogInformation("[ORCH] Session aborted — no valid opening range for {Symbol} on {Date}", symbol, sessionDate);
            yield break;
        }

        BreakoutResult? breakout = null;
        FairValueGap? fvg = null;
        bool awaitingRetracement = false;
        bool awaitingEngulf = false;
        bool signalEmitted = false;
        Candle? previousCandle = null;
        var postBreakoutCandles = new List<Candle>();

        await foreach (Candle candle in oneMinCandles.WithCancellation(ct))
        {
            if (signalEmitted) yield break;

            if (breakout is null)
            {
                breakout = _breakoutDetector.Detect(candle, or, config);

                if (breakout is not null)
                {
                    bool volumeOk = await _volumeFilter.IsConfirmedAsync(
                        symbol, breakout.BreakoutCandle, config.VolumeAvgLookback, ct);

                    if (!volumeOk)
                    {
                        _logger.LogInformation("[ORCH] Breakout rejected by volume filter — waiting for next breakout");
                        breakout = null;
                        continue;
                    }

                    bool trendOk = await _trendFilter.IsAlignedAsync(
                        symbol, candle.Close, breakout.Direction, ct);

                    if (!trendOk)
                    {
                        _logger.LogInformation("[ORCH] Breakout rejected by trend filter — session aborted");
                        yield break;
                    }

                    _logger.LogInformation(
                        "[ORCH] Valid {Dir} breakout confirmed at {Price} for {Symbol}",
                        breakout.Direction, candle.Close, symbol);
                }

                previousCandle = candle;
                continue;
            }

            postBreakoutCandles.Add(candle);

            if (fvg is null && !awaitingRetracement)
            {
                fvg = _fvgDetector.Detect(postBreakoutCandles, breakout.Direction, config.MinFvgGapPct);

                if (fvg is not null)
                {
                    awaitingRetracement = true;
                    _logger.LogInformation(
                        "[ORCH] FVG found {Low}–{High}, awaiting retracement for {Symbol}",
                        fvg.GapLow, fvg.GapHigh, symbol);
                }
            }

            if (awaitingRetracement && !awaitingEngulf && fvg is not null)
            {
                if (fvg.Contains(candle.Close))
                {
                    awaitingEngulf = true;
                    awaitingRetracement = false;
                    _logger.LogInformation(
                        "[ORCH] Price {Price} entered FVG zone — awaiting engulfing candle for {Symbol}",
                        candle.Close, symbol);
                }
            }
            else if (awaitingEngulf && previousCandle is not null && fvg is not null)
            {
                bool engulfed = _engulfing.IsEngulfing(previousCandle, candle, breakout.Direction);

                if (engulfed)
                {
                    _logger.LogInformation(
                        "[ORCH] Engulfing candle confirmed at {Time} for {Symbol}",
                        candle.Timestamp, symbol);

                    TradeSignal? signal = await _signalGenerator.GenerateAsync(
                        symbol, or, breakout, fvg, candle, config, ct);

                    if (signal is not null)
                    {
                        signalEmitted = true;
                        yield return signal;
                    }
                }
            }

            previousCandle = candle;
        }

        if (!signalEmitted)
            _logger.LogInformation("[ORCH] Session ended with no signal for {Symbol} on {Date}", symbol, sessionDate);
    }
}
