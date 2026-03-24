using Microsoft.Extensions.Logging;
using TradingSystem.Api.Strategy.Interfaces;
using TradingSystem.Api.Strategy.Models;
using TradingSystem.Core.Models;

namespace TradingSystem.Api.Strategy.Services;

public sealed class SignalGenerator : ISignalGenerator
{
    private readonly IRsiFilter _rsiFilter;
    private readonly ILogger<SignalGenerator> _logger;

    public SignalGenerator(IRsiFilter rsiFilter, ILogger<SignalGenerator> logger)
    {
        _rsiFilter = rsiFilter;
        _logger = logger;
    }

    public async Task<TradeSignal?> GenerateAsync(
        string symbol,
        OpeningRange or,
        BreakoutResult breakout,
        FairValueGap fvg,
        Candle confirmationCandle,
        IntraDayStrategyConfig config,
        CancellationToken ct = default)
    {
        decimal entry = confirmationCandle.Close;
        decimal buffer = entry * config.StopLossBufferPct;

        decimal stopLoss, target, partialTarget, risk;

        if (breakout.Direction == Direction.Bullish)
        {
            stopLoss = fvg.GapLow - buffer;
            risk = entry - stopLoss;
            target = entry + (config.RiskRewardRatio * risk);
            partialTarget = entry + (config.PartialExitR * risk);
        }
        else
        {
            stopLoss = fvg.GapHigh + buffer;
            risk = stopLoss - entry;
            target = entry - (config.RiskRewardRatio * risk);
            partialTarget = entry - (config.PartialExitR * risk);
        }

        if (risk <= 0)
        {
            _logger.LogWarning("[SIGNAL] Invalid risk={Risk} for {Symbol} — signal aborted", risk, symbol);
            return null;
        }

        var (rsiPassed, rsiValue) = await _rsiFilter.EvaluateAsync(symbol, breakout.Direction, config, ct);

        int confidence = ComputeConfidence(
            breakoutCandle: breakout.BreakoutCandle,
            fvg: fvg,
            entry: entry,
            rsiPassed: rsiPassed,
            rsiValue: rsiValue,
            direction: breakout.Direction);

        string signalDir = breakout.Direction == Direction.Bullish ? "BUY_CE" : "BUY_PE";

        string reason = string.Join(" | ",
        [
            $"{breakout.Direction} breakout at {entry}",
            $"FVG {fvg.GapLow}–{fvg.GapHigh}",
            $"EMA aligned",
            $"RSI {rsiValue:F1} ({(rsiPassed ? "pass" : "soft-fail")})",
            $"Confidence {confidence}"
        ]);

        _logger.LogInformation(
            "[SIGNAL] {Dir} signal generated for {Symbol}: Entry={Entry} SL={SL} T1={T1} T2={T2} Conf={Conf}",
            signalDir, symbol, entry, stopLoss, partialTarget, target, confidence);

        return new TradeSignal
        {
            Symbol = symbol,
            SignalDirection = signalDir,
            EntryPrice = entry,
            StopLoss = stopLoss,
            Target = target,
            PartialTarget = partialTarget,
            RiskReward = config.RiskRewardRatio,
            ConfidenceScore = confidence,
            Timestamp = confirmationCandle.Timestamp.DateTime,
            Reason = reason
        };
    }

    private static int ComputeConfidence(
        Candle breakoutCandle,
        FairValueGap fvg,
        decimal entry,
        bool rsiPassed,
        decimal rsiValue,
        Direction direction)
    {
        int score = 0;

        decimal candleRange = breakoutCandle.High - breakoutCandle.Low;
        decimal candleBody = Math.Abs(breakoutCandle.Close - breakoutCandle.Open);
        decimal bodyRatio = candleRange > 0 ? candleBody / candleRange : 0m;

        score += bodyRatio >= 0.6m ? 20 : 10;

        decimal fvgPct = entry > 0 ? fvg.Size / entry : 0m;
        score += fvgPct >= 0.002m ? 15 : 8;

        score += 15; // EMA alignment bonus

        if (rsiPassed)
        {
            bool strongRsi = direction == Direction.Bullish
                ? rsiValue > 60m
                : rsiValue < 40m;
            score += strongRsi ? 15 : 8;
        }

        bool allStrong = bodyRatio >= 0.6m && fvgPct >= 0.002m && rsiPassed;
        if (allStrong) score += 15;

        return Math.Min(score, 100);
    }
}
