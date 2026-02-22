using TradingSystem.Core.Models;

namespace TradingSystem.Indicators;

public class IndicatorValues
{
    public decimal EMAFast { get; set; }
    public decimal EMASlow { get; set; }
    public decimal RSI { get; set; }
    public decimal MacdLine { get; set; }
    public decimal MacdSignal { get; set; }
    public decimal MacdHistogram { get; set; }
    public decimal ADX { get; set; }
    public decimal PlusDI { get; set; }
    public decimal MinusDI { get; set; }
    public decimal ATR { get; set; }
    public decimal BollingerMiddle { get; set; }
    public decimal BollingerUpper { get; set; }
    public decimal BollingerLower { get; set; }
    public decimal BollingerWidth { get; set; }
    public decimal VWAP { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

public class IndicatorEngine
{
    private readonly EMA _emaFast;
    private readonly EMA _emaSlow;
    private readonly RSI _rsi;
    private readonly MACD _macd;
    private readonly ADX _adx;
    private readonly ATR _atr;
    private readonly BollingerBands _bollingerBands;
    private readonly VWAP _vwap;

    public IndicatorEngine(
        int emaFastPeriod,
        int emaSlowPeriod,
        int rsiPeriod,
        int macdFast,
        int macdSlow,
        int macdSignal,
        int adxPeriod,
        int atrPeriod,
        int bollingerPeriod,
        decimal bollingerStdDev)
    {
        _emaFast = new EMA(emaFastPeriod);
        _emaSlow = new EMA(emaSlowPeriod);
        _rsi = new RSI(rsiPeriod);
        _macd = new MACD(macdFast, macdSlow, macdSignal);
        _adx = new ADX(adxPeriod);
        _atr = new ATR(atrPeriod);
        _bollingerBands = new BollingerBands(bollingerPeriod, bollingerStdDev);
        _vwap = new VWAP();
    }

    public IndicatorValues Calculate(Candle candle)
    {
        var emaFast = _emaFast.Calculate(candle.Close);
        var emaSlow = _emaSlow.Calculate(candle.Close);
        var rsi = _rsi.Calculate(candle.Close);

        _macd.Calculate(candle.Close);
        _adx.Calculate(candle.High, candle.Low, candle.Close);
        var atr = _atr.Calculate(candle.High, candle.Low, candle.Close);
        _bollingerBands.Calculate(candle.Close);
        _vwap.Calculate(candle.TypicalPrice, candle.Volume, candle.Timestamp);

        return new IndicatorValues
        {
            EMAFast = emaFast,
            EMASlow = emaSlow,
            RSI = rsi,
            MacdLine = _macd.MacdLine,
            MacdSignal = _macd.SignalLine,
            MacdHistogram = _macd.Histogram,
            ADX = _adx.ADXValue,
            PlusDI = _adx.PlusDI,
            MinusDI = _adx.MinusDI,
            ATR = atr,
            BollingerMiddle = _bollingerBands.MiddleBand,
            BollingerUpper = _bollingerBands.UpperBand,
            BollingerLower = _bollingerBands.LowerBand,
            BollingerWidth = _bollingerBands.BandWidth,
            VWAP = _vwap.Value,
            Timestamp = candle.Timestamp
        };
    }
}
