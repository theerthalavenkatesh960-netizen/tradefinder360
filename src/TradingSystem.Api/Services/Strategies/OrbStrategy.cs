using TradingSystem.Api.DTOs;

namespace TradingSystem.Api.Services.Strategies;

public sealed class OrbStrategy : BacktestStrategyBase
{
    private readonly BacktestRunnerService _runner;

    public OrbStrategy(BacktestRunnerService runner)
    {
        _runner = runner;
    }

    public override string StrategyName => "ORB";

    public override BacktestStrategyResult Execute(BacktestRunContext ctx)
    {
        var mode = (ctx.Params.OrbMode ?? "CLASSIC").Trim().ToUpperInvariant();
        if (mode == "FVG_RETEST")
        {
            var (trades, annotations) = _runner.RunOrbFvgRetestInternal(
                ctx.Candles,
                ctx.Indicators,
                ctx.IstTimes,
                ctx.Params,
                ctx.InitialCapital,
                ctx.Instrument);
            return new BacktestStrategyResult(trades, annotations);
        }

        var classicTrades = _runner.RunORBInternal(ctx.Candles, ctx.Indicators, ctx.IstTimes, ctx.Params, ctx.InitialCapital);
        return new BacktestStrategyResult(classicTrades);
    }
}
