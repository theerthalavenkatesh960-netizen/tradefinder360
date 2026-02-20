using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;
using TradingSystem.Data.Services.Interfaces;

namespace TradingSystem.Data.Services;

public class TradeService : ITradeService
{
    private readonly TradingDbContext _db;

    public TradeService(TradingDbContext db)
    {
        _db = db;
    }

    public async Task SaveAsync(string instrumentKey, Trade trade)
    {
        var record = new TradeRecord
        {
            Id = trade.Id,
            InstrumentKey = instrumentKey,
            TradeType = !string.IsNullOrEmpty(trade.OptionSymbol) ? "OPTIONS" : "SPOT",
            EntryTime = trade.EntryTime,
            ExitTime = trade.ExitTime,
            EntryPrice = trade.SpotEntryPrice,
            ExitPrice = trade.SpotExitPrice,
            Quantity = trade.Quantity,
            StopLoss = trade.StopLoss,
            Target = trade.Target,
            ATRAtEntry = trade.ATRAtEntry,
            OptionSymbol = trade.OptionSymbol,
            OptionStrike = trade.OptionStrike,
            OptionEntryPrice = trade.OptionEntryPrice,
            OptionExitPrice = trade.OptionExitPrice,
            EntryReason = trade.EntryReason,
            ExitReason = trade.ExitReason,
            Direction = trade.Direction.ToString(),
            State = trade.State.ToString(),
            PnL = trade.PnL ?? 0m,
            PnLPercent = trade.PnLPercent ?? 0m,
            CreatedAt = DateTime.UtcNow
        };
        await _db.Trades.AddAsync(record);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(TradeRecord tradeRecord)
    {
        _db.Trades.Update(tradeRecord);
        await _db.SaveChangesAsync();
    }

    public async Task<List<TradeRecord>> GetByInstrumentAsync(string instrumentKey, DateTime? startDate = null, DateTime? endDate = null)
    {
        var query = _db.Trades.Where(t => t.InstrumentKey == instrumentKey);
        if (startDate.HasValue) query = query.Where(t => t.EntryTime >= startDate.Value);
        if (endDate.HasValue) query = query.Where(t => t.EntryTime <= endDate.Value);
        return await query.OrderByDescending(t => t.EntryTime).ToListAsync();
    }

    public async Task<List<TradeRecord>> GetTodayAsync(string instrumentKey)
    {
        var today = DateTime.UtcNow.Date;
        return await _db.Trades
            .Where(t => t.InstrumentKey == instrumentKey && t.EntryTime >= today)
            .OrderBy(t => t.EntryTime)
            .ToListAsync();
    }

    public async Task<TradeRecord?> GetByIdAsync(Guid id)
        => await _db.Trades.FindAsync(id);
}
