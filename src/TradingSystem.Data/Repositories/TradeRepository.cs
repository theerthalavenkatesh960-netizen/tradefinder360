using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;

namespace TradingSystem.Data.Repositories;

public class TradeRepository : ITradeRepository
{
    private readonly TradingDbContext _context;

    public TradeRepository(TradingDbContext context)
    {
        _context = context;
    }

    public async Task SaveAsync(string instrumentKey, Trade trade)
    {
        var tradeRecord = new TradeRecord
        {
            Id = trade.Id,
            InstrumentKey = instrumentKey,
            TradeType = trade.OptionSymbol != null ? "OPTIONS" : "SPOT",
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
            EntryReason = trade.EntryReason ?? string.Empty,
            ExitReason = trade.ExitReason,
            Direction = trade.Direction.ToString(),
            State = trade.State.ToString(),
            PnL = trade.PnL,
            PnLPercent = trade.PnLPercent,
            CreatedAt = DateTime.UtcNow
        };

        await _context.Trades.AddAsync(tradeRecord);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(TradeRecord tradeRecord)
    {
        tradeRecord.UpdatedAt = DateTime.UtcNow;
        _context.Trades.Update(tradeRecord);
        await _context.SaveChangesAsync();
    }

    public async Task<List<TradeRecord>> GetByInstrumentAsync(string instrumentKey, DateTime? startDate = null, DateTime? endDate = null)
    {
        var query = _context.Trades
            .Where(t => t.InstrumentKey == instrumentKey);

        if (startDate.HasValue)
            query = query.Where(t => t.EntryTime >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(t => t.EntryTime <= endDate.Value);

        return await query
            .OrderByDescending(t => t.EntryTime)
            .ToListAsync();
    }

    public async Task<List<TradeRecord>> GetTodayTradesAsync(string instrumentKey)
    {
        var today = DateTime.UtcNow.Date;
        return await _context.Trades
            .Where(t => t.InstrumentKey == instrumentKey && t.EntryTime >= today)
            .OrderBy(t => t.EntryTime)
            .ToListAsync();
    }

    public async Task<TradeRecord?> GetByIdAsync(Guid id)
    {
        return await _context.Trades.FindAsync(id);
    }
}
