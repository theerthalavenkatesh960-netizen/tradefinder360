using TradingSystem.Core.Models;
using TradingSystem.Configuration.Models;
using TradingSystem.Indicators;

namespace TradingSystem.MarketState;

public class MarketStateEngine
{
    private readonly MarketStateConfig _config;
    private readonly List<decimal> _emaFastHistory = new();
    private readonly List<decimal> _emaSlowHistory = new();

    public MarketStateEngine(MarketStateConfig config)
    {
        _config = config;
    }

    public MarketStateInfo DetermineState(
        List<Candle> candles,
        IndicatorValues indicators)
    {
        _emaFastHistory.Add(indicators.EMAFast);
        _emaSlowHistory.Add(indicators.EMASlow);

        if (_emaFastHistory.Count > 50)
        {
            _emaFastHistory.RemoveAt(0);
            _emaSlowHistory.RemoveAt(0);
        }

        var latestCandle = candles[^1];
        var state = new MarketStateInfo
        {
            Timestamp = indicators.Timestamp,
            Indicators = new Dictionary<string, decimal>
            {
                ["ADX"] = indicators.ADX,
                ["RSI"] = indicators.RSI,
                ["Close"] = latestCandle.Close,
                ["EMAFast"] = indicators.EMAFast,
                ["EMASlow"] = indicators.EMASlow,
                ["VWAP"] = indicators.VWAP,
                ["MacdLine"] = indicators.MacdLine,
                ["BollingerWidth"] = indicators.BollingerWidth
            }
        };

        if (IsSidewaysMarket(candles, indicators))
        {
            state.State = Core.Models.MarketState.SIDEWAYS;
            state.Reason = "Market is consolidating - ADX weak, price choppy, narrow bands";
            return state;
        }

        if (IsBullishTrend(candles, indicators))
        {
            state.State = Core.Models.MarketState.TRENDING_BULLISH;
            state.Reason = "Strong bullish trend confirmed - ADX strong, price above EMAs and VWAP, bullish structure";
            return state;
        }

        if (IsBearishTrend(candles, indicators))
        {
            state.State = Core.Models.MarketState.TRENDING_BEARISH;
            state.Reason = "Strong bearish trend confirmed - ADX strong, price below EMAs and VWAP, bearish structure";
            return state;
        }

        state.State = Core.Models.MarketState.SIDEWAYS;
        state.Reason = "No clear trend detected - waiting for confirmation";
        return state;
    }

    private bool IsSidewaysMarket(List<Candle> candles, IndicatorValues indicators)
    {
        var latestCandle = candles[^1];

        var weakAdx = indicators.ADX < _config.SidewaysAdxThreshold;

        var rsiInRange = indicators.RSI >= _config.SidewaysRsiLower &&
                        indicators.RSI <= _config.SidewaysRsiUpper;

        var narrowBands = indicators.BollingerWidth < _config.BollingerNarrowThreshold;

        var emaFlat = StructureAnalyzer.IsEmaFlat(_emaFastHistory);

        var frequentCrossovers = StructureAnalyzer.CountEmaCrossovers(
            candles, _emaFastHistory, 10) >= 3;

        var sidewaysCount = 0;
        if (weakAdx) sidewaysCount++;
        if (rsiInRange) sidewaysCount++;
        if (narrowBands) sidewaysCount++;
        if (emaFlat || frequentCrossovers) sidewaysCount++;

        return sidewaysCount >= 3;
    }

    private bool IsBullishTrend(List<Candle> candles, IndicatorValues indicators)
    {
        var latestCandle = candles[^1];

        var strongAdx = indicators.ADX > _config.TrendingAdxThreshold;
        if (!strongAdx) return false;

        var priceAboveEmas = latestCandle.Close > indicators.EMAFast &&
                             latestCandle.Close > indicators.EMASlow;
        if (!priceAboveEmas) return false;

        var priceAboveVwap = latestCandle.Close > indicators.VWAP;
        if (!priceAboveVwap) return false;

        var bullishRsi = indicators.RSI > _config.BullishRsiThreshold;
        if (!bullishRsi) return false;

        var macdPositive = indicators.MacdLine > 0;
        if (!macdPositive) return false;

        var bullishStructure = StructureAnalyzer.IsBullishStructure(
            candles, _config.MinCandlesForTrend);
        if (!bullishStructure) return false;

        return true;
    }

    private bool IsBearishTrend(List<Candle> candles, IndicatorValues indicators)
    {
        var latestCandle = candles[^1];

        var strongAdx = indicators.ADX > _config.TrendingAdxThreshold;
        if (!strongAdx) return false;

        var priceBelowEmas = latestCandle.Close < indicators.EMAFast &&
                              latestCandle.Close < indicators.EMASlow;
        if (!priceBelowEmas) return false;

        var priceBelowVwap = latestCandle.Close < indicators.VWAP;
        if (!priceBelowVwap) return false;

        var bearishRsi = indicators.RSI < _config.BearishRsiThreshold;
        if (!bearishRsi) return false;

        var macdNegative = indicators.MacdLine < 0;
        if (!macdNegative) return false;

        var bearishStructure = StructureAnalyzer.IsBearishStructure(
            candles, _config.MinCandlesForTrend);
        if (!bearishStructure) return false;

        return true;
    }
}
