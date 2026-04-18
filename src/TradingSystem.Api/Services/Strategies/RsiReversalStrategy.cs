using TradingSystem.Api.DTOs;

namespace TradingSystem.Api.Services.Strategies;

public sealed class RsiReversalStrategy : BacktestStrategyBase
{
    private readonly BacktestRunnerService _runner;

    public RsiReversalStrategy(BacktestRunnerService runner)
    {
        _runner = runner;
    }

    public override string StrategyName => "RSI_REVERSAL";

    public override BacktestStrategyResult Execute(BacktestRunContext ctx)
    {
        var trades = _runner.RunRsiReversalInternal(ctx.Candles, ctx.Indicators, ctx.IstTimes, ctx.Params, ctx.InitialCapital);
        return new BacktestStrategyResult(trades);
    }
}
