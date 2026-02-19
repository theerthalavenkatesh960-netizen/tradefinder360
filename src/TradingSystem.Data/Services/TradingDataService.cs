using TradingSystem.Core.Models;
using TradingSystem.Indicators;

namespace TradingSystem.Data.Services;

public class TradingDataService
{
    private readonly ICandleService _candleService;
    private readonly IIndicatorService _indicatorService;
    private readonly ITradeService _tradeService;
    private readonly IInstrumentService _instrumentService;

    public TradingDataService(
        ICandleService candleService,
        IIndicatorService indicatorService,
        ITradeService tradeService,
        IInstrumentService instrumentService)
    {
        _candleService = candleService;
        _indicatorService = indicatorService;
        _tradeService = tradeService;
        _instrumentService = instrumentService;
    }

    public async Task<TradingInstrument?> GetInstrumentAsync(string instrumentKey)
        => await _instrumentService.GetByKeyAsync(instrumentKey);

    public async Task<List<Candle>> GetRecentCandlesAsync(string instrumentKey, int timeframeMinutes, int count)
        => await _candleService.GetRecentAsync(instrumentKey, timeframeMinutes, count);

    public async Task SaveCandleAsync(string instrumentKey, Candle candle)
        => await _candleService.SaveAsync(instrumentKey, candle);

    public async Task SaveIndicatorsAsync(string instrumentKey, int timeframeMinutes, IndicatorValues indicators)
        => await _indicatorService.SaveAsync(instrumentKey, timeframeMinutes, indicators);

    public async Task SaveTradeAsync(string instrumentKey, Trade trade)
        => await _tradeService.SaveAsync(instrumentKey, trade);

    public async Task<List<TradeRecord>> GetTodayTradesAsync(string instrumentKey)
        => await _tradeService.GetTodayAsync(instrumentKey);

    public async Task UpdateTradeAsync(TradeRecord tradeRecord)
        => await _tradeService.UpdateAsync(tradeRecord);
}
