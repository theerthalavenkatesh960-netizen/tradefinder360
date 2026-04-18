using TradingSystem.Api.DTOs;
using TradingSystem.Core.Models;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Indicators;

namespace TradingSystem.Api.Services.Strategies;

/// <summary>
/// Carries everything a strategy implementation needs. Populated by BacktestRunnerService
/// before dispatching to the strategy; strategies must not modify it.
/// </summary>
public sealed record BacktestRunContext(
    TradingInstrument Instrument,
    List<Candle> Candles,
    IndicatorValues[] Indicators,
    DateTimeOffset[] IstTimes,
    StrategyParams Params,
    double InitialCapital,
    ICandleService CandleService    // needed by multi-timeframe strategies (SMC_FVG)
);
