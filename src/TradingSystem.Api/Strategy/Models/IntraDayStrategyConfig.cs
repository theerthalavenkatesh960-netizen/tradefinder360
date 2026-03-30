namespace TradingSystem.Api.Strategy.Models;

public sealed class IntraDayStrategyConfig
{
    // Minimum opening range width as a fraction of price (0.003 = 0.3%)
    public decimal MinOpeningRangePct { get; set; } = 0.003m;

    // Minimum FVG gap size as a fraction of price (0.001 = 0.1%)
    public decimal MinFvgGapPct { get; set; } = 0.001m;

    // Number of prior candles to average for volume comparison
    public int VolumeAvgLookback { get; set; } = 10;

    // EMA period on 15-min chart for trend filter
    public int EmaPeriod { get; set; } = 20;

    // RSI period on 5-min chart
    public int RsiPeriod { get; set; } = 14;

    // RSI must be ABOVE this value to allow bullish trades
    public decimal RsiBullThreshold { get; set; } = 55m;

    // RSI must be BELOW this value to allow bearish trades
    public decimal RsiBearThreshold { get; set; } = 45m;

    // Full exit risk:reward ratio (3 = exit at 3× risk)
    public decimal RiskRewardRatio { get; set; } = 3.0m;

    // Partial exit risk multiple (2 = exit 50% at 2× risk)
    public decimal PartialExitR { get; set; } = 2.0m;

    // Earliest candle time allowed for trades (inclusive), IST
    public TimeOnly TradeWindowStart { get; set; } = new TimeOnly(9, 20, 0);

    // Latest candle time allowed for trades (inclusive), IST
    public TimeOnly TradeWindowEnd { get; set; } = new TimeOnly(10, 30, 0);

    // Stop loss buffer as fraction of entry price (0.0005 = 0.05%)
    public decimal StopLossBufferPct { get; set; } = 0.0005m;
}
