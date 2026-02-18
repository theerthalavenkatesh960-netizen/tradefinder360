using TradingSystem.Configuration.Models;

namespace TradingSystem.Indicators;

public class TimeframeScaler
{
    private readonly TimeframeConfig _timeframeConfig;
    private readonly IndicatorConfig _indicatorConfig;
    private readonly decimal _multiplier;

    public TimeframeScaler(TimeframeConfig timeframeConfig, IndicatorConfig indicatorConfig)
    {
        _timeframeConfig = timeframeConfig;
        _indicatorConfig = indicatorConfig;
        _multiplier = timeframeConfig.GetTimeframeMultiplier();
    }

    public int GetScaledEmaFast() =>
        _indicatorConfig.GetScaledLength(_indicatorConfig.BaseEmaFastLength, _multiplier);

    public int GetScaledEmaSlow() =>
        _indicatorConfig.GetScaledLength(_indicatorConfig.BaseEmaSlowLength, _multiplier);

    public int GetScaledRsi() =>
        _indicatorConfig.GetScaledLength(_indicatorConfig.BaseRsiLength, _multiplier);

    public int GetScaledMacdFast() =>
        _indicatorConfig.GetScaledLength(_indicatorConfig.BaseMacdFast, _multiplier);

    public int GetScaledMacdSlow() =>
        _indicatorConfig.GetScaledLength(_indicatorConfig.BaseMacdSlow, _multiplier);

    public int GetScaledMacdSignal() =>
        _indicatorConfig.GetScaledLength(_indicatorConfig.BaseMacdSignal, _multiplier);

    public int GetScaledAdx() =>
        _indicatorConfig.GetScaledLength(_indicatorConfig.BaseAdxLength, _multiplier);

    public int GetScaledAtr() =>
        _indicatorConfig.GetScaledLength(_indicatorConfig.BaseAtrLength, _multiplier);

    public int GetScaledBollinger() =>
        _indicatorConfig.GetScaledLength(_indicatorConfig.BaseBollingerLength, _multiplier);

    public decimal GetMultiplier() => _multiplier;

    public IndicatorEngine CreateScaledIndicatorEngine()
    {
        return new IndicatorEngine(
            GetScaledEmaFast(),
            GetScaledEmaSlow(),
            GetScaledRsi(),
            GetScaledMacdFast(),
            GetScaledMacdSlow(),
            GetScaledMacdSignal(),
            GetScaledAdx(),
            GetScaledAtr(),
            GetScaledBollinger(),
            _indicatorConfig.BollingerStdDev
        );
    }
}
