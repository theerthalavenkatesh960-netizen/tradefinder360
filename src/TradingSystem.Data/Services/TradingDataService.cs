using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories;
using TradingSystem.Indicators;

namespace TradingSystem.Data.Services;

public class TradingDataService
{
    private readonly ICandleRepository _candleRepo;
    private readonly IIndicatorRepository _indicatorRepo;
    private readonly ITradeRepository _tradeRepo;
    private readonly IInstrumentRepository _instrumentRepo;

    public TradingDataService(
        ICandleRepository candleRepo,
        IIndicatorRepository indicatorRepo,
        ITradeRepository tradeRepo,
        IInstrumentRepository instrumentRepo)
    {
        _candleRepo = candleRepo;
        _indicatorRepo = indicatorRepo;
        _tradeRepo = tradeRepo;
        _instrumentRepo = instrumentRepo;
    }

    public async Task<TradingInstrument?> GetInstrumentAsync(string instrumentKey)
    {
        return await _instrumentRepo.GetByKeyAsync(instrumentKey);
    }

    public async Task<List<Candle>> GetRecentCandlesAsync(string instrumentKey, int timeframeMinutes, int count)
    {
        return await _candleRepo.GetRecentAsync(instrumentKey, timeframeMinutes, count);
    }

    public async Task SaveCandleAsync(string instrumentKey, Candle candle)
    {
        await _candleRepo.SaveAsync(instrumentKey, candle);
    }

    public async Task SaveIndicatorsAsync(string instrumentKey, int timeframeMinutes, IndicatorValues indicators)
    {
        await _indicatorRepo.SaveAsync(instrumentKey, timeframeMinutes, indicators);
    }

    public async Task SaveTradeAsync(string instrumentKey, Trade trade)
    {
        await _tradeRepo.SaveAsync(instrumentKey, trade);
    }

    public async Task<List<TradeRecord>> GetTodayTradesAsync(string instrumentKey)
    {
        return await _tradeRepo.GetTodayTradesAsync(instrumentKey);
    }

    public async Task UpdateTradeAsync(TradeRecord tradeRecord)
    {
        await _tradeRepo.UpdateAsync(tradeRecord);
    }
}
