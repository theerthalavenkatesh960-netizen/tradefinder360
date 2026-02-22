using TradingSystem.Core.Models;
using TradingSystem.Data.Services.Interfaces;
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

    public async Task<List<Candle>> GetRecentCandlesAsync(int instrumentId, int timeframeMinutes, int count)
        => await _candleService.GetRecentAsync(instrumentId, timeframeMinutes, count);

    public async Task SaveCandleAsync(int instrumentId, Candle candle)
        => await _candleService.SaveAsync(instrumentId, candle);

    public async Task SaveIndicatorsAsync(int instrumentId, int timeframeMinutes, IndicatorValues indicators)
        => await _indicatorService.SaveAsync(instrumentId, timeframeMinutes, indicators);

    public async Task SaveTradeAsync(int instrumentId, Trade trade)
        => await _tradeService.SaveAsync(instrumentId, trade);

    public async Task<List<TradeRecord>> GetTodayTradesAsync(int instrumentId)
        => await _tradeService.GetTodayAsync(instrumentId);

    public async Task UpdateTradeAsync(TradeRecord tradeRecord)
        => await _tradeService.UpdateAsync(tradeRecord);
}
