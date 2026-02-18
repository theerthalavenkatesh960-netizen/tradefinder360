using Supabase;
using TradingSystem.Core.Models;
using TradingSystem.Configuration.Models;
using TradingSystem.Data.Models;

namespace TradingSystem.Data;

public class SupabaseRepository
{
    private readonly Client _client;
    private readonly bool _enabled;

    public SupabaseRepository(DatabaseConfig config)
    {
        _enabled = config.EnablePersistence;

        if (_enabled)
        {
            var options = new SupabaseOptions
            {
                AutoRefreshToken = true,
                AutoConnectRealtime = false
            };

            _client = new Client(config.SupabaseUrl, config.SupabaseKey, options);
            _client.InitializeAsync().Wait();
        }
        else
        {
            _client = null!;
        }
    }

    public async Task SaveTrade(Trade trade)
    {
        if (!_enabled) return;

        var record = new TradeRecord
        {
            Id = trade.Id,
            EntryTime = trade.EntryTime,
            ExitTime = trade.ExitTime,
            Direction = trade.Direction.ToString(),
            State = trade.State.ToString(),
            SpotEntryPrice = trade.SpotEntryPrice,
            SpotExitPrice = trade.SpotExitPrice,
            OptionSymbol = trade.OptionSymbol,
            OptionStrike = trade.OptionStrike,
            OptionEntryPrice = trade.OptionEntryPrice,
            OptionExitPrice = trade.OptionExitPrice,
            Quantity = trade.Quantity,
            StopLoss = trade.StopLoss,
            Target = trade.Target,
            ATRAtEntry = trade.ATRAtEntry,
            EntryReason = trade.EntryReason,
            ExitReason = trade.ExitReason,
            PnL = trade.PnL,
            PnLPercent = trade.PnLPercent,
            CreatedAt = DateTime.UtcNow
        };

        await _client.From<TradeRecord>().Upsert(record);
    }

    public async Task SaveCandle(Candle candle)
    {
        if (!_enabled) return;

        var record = new CandleRecord
        {
            Id = Guid.NewGuid(),
            Timestamp = candle.Timestamp,
            Open = candle.Open,
            High = candle.High,
            Low = candle.Low,
            Close = candle.Close,
            Volume = candle.Volume,
            TimeframeMinutes = candle.TimeframeMinutes
        };

        await _client.From<CandleRecord>().Insert(record);
    }

    public async Task SaveMarketState(MarketStateInfo state)
    {
        if (!_enabled) return;

        var record = new MarketStateRecord
        {
            Id = Guid.NewGuid(),
            Timestamp = state.Timestamp,
            State = state.State.ToString(),
            Reason = state.Reason,
            ADX = state.Indicators.GetValueOrDefault("ADX", 0),
            RSI = state.Indicators.GetValueOrDefault("RSI", 0),
            MACD = state.Indicators.GetValueOrDefault("MacdLine", 0)
        };

        await _client.From<MarketStateRecord>().Insert(record);
    }

    public async Task<List<Trade>> GetTrades(DateTime? startDate = null, DateTime? endDate = null)
    {
        if (!_enabled) return new List<Trade>();

        var query = _client.From<TradeRecord>().Select("*");

        if (startDate.HasValue)
            query = query.Filter("entry_time", Postgrest.Constants.Operator.GreaterThanOrEqual, startDate.Value);

        if (endDate.HasValue)
            query = query.Filter("entry_time", Postgrest.Constants.Operator.LessThanOrEqual, endDate.Value);

        var response = await query.Get();
        var records = response.Models;

        return records.Select(r => new Trade
        {
            Id = r.Id,
            EntryTime = r.EntryTime,
            ExitTime = r.ExitTime,
            Direction = Enum.Parse<TradeDirection>(r.Direction),
            State = Enum.Parse<TradeState>(r.State),
            SpotEntryPrice = r.SpotEntryPrice,
            SpotExitPrice = r.SpotExitPrice,
            OptionSymbol = r.OptionSymbol,
            OptionStrike = r.OptionStrike,
            OptionEntryPrice = r.OptionEntryPrice,
            OptionExitPrice = r.OptionExitPrice,
            Quantity = r.Quantity,
            StopLoss = r.StopLoss,
            Target = r.Target,
            ATRAtEntry = r.ATRAtEntry,
            EntryReason = r.EntryReason,
            ExitReason = r.ExitReason,
            PnL = r.PnL,
            PnLPercent = r.PnLPercent
        }).ToList();
    }
}
