using TradingSystem.Api.DTOs;

namespace TradingSystem.Api.Services.Strategies;

public sealed class SmcFvgStrategy : BacktestStrategyBase
{
    private readonly BacktestRunnerService _runner;

    public SmcFvgStrategy(BacktestRunnerService runner)
    {
        _runner = runner;
    }

    public override string StrategyName => "SMC";

    public override BacktestStrategyResult Execute(BacktestRunContext ctx)
    {
        _ = (ctx.Params.SmcMode ?? "FVG_OB").Trim().ToUpperInvariant();
        var from = ctx.Candles.Count > 0 ? ctx.Candles[0].Timestamp.UtcDateTime : DateTime.UtcNow;
        var to = ctx.Candles.Count > 0 ? ctx.Candles[^1].Timestamp.UtcDateTime : DateTime.UtcNow;
        var trades = _runner.RunSmcFvgInternal(ctx.Instrument.Id, from, to, ctx.Params, ctx.InitialCapital);
        return new BacktestStrategyResult(trades);
    }
}
