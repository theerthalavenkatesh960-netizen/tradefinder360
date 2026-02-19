using Npgsql;
using TradingSystem.Core.Models;

namespace TradingSystem.Data.Services;

public class TradeService : ITradeService
{
    private readonly DbConnectionFactory _factory;

    public TradeService(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task SaveAsync(string instrumentKey, Trade trade)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO trades
                (id, instrument_key, trade_type, entry_time, exit_time,
                 entry_price, exit_price, quantity, stop_loss, target, atr_at_entry,
                 option_symbol, option_strike, option_entry_price, option_exit_price,
                 entry_reason, exit_reason, direction, state, pnl, pnl_percent, created_at)
            VALUES
                (@id, @key, @type, @entry_time, @exit_time,
                 @entry_price, @exit_price, @qty, @sl, @target, @atr,
                 @opt_sym, @opt_strike, @opt_entry, @opt_exit,
                 @entry_reason, @exit_reason, @dir, @state, @pnl, @pnl_pct, @now)", conn);
        cmd.Parameters.AddWithValue("id", trade.Id);
        cmd.Parameters.AddWithValue("key", instrumentKey);
        cmd.Parameters.AddWithValue("type", trade.OptionSymbol != null ? "OPTIONS" : "SPOT");
        cmd.Parameters.AddWithValue("entry_time", trade.EntryTime);
        cmd.Parameters.AddWithValue("exit_time", trade.ExitTime.HasValue ? (object)trade.ExitTime.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("entry_price", trade.SpotEntryPrice);
        cmd.Parameters.AddWithValue("exit_price", trade.SpotExitPrice.HasValue ? (object)trade.SpotExitPrice.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("qty", trade.Quantity);
        cmd.Parameters.AddWithValue("sl", trade.StopLoss);
        cmd.Parameters.AddWithValue("target", trade.Target);
        cmd.Parameters.AddWithValue("atr", trade.ATRAtEntry);
        cmd.Parameters.AddWithValue("opt_sym", trade.OptionSymbol ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("opt_strike", trade.OptionStrike.HasValue ? (object)trade.OptionStrike.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("opt_entry", trade.OptionEntryPrice.HasValue ? (object)trade.OptionEntryPrice.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("opt_exit", trade.OptionExitPrice.HasValue ? (object)trade.OptionExitPrice.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("entry_reason", trade.EntryReason ?? string.Empty);
        cmd.Parameters.AddWithValue("exit_reason", trade.ExitReason ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("dir", trade.Direction.ToString());
        cmd.Parameters.AddWithValue("state", trade.State.ToString());
        cmd.Parameters.AddWithValue("pnl", trade.PnL ?? 0m);
        cmd.Parameters.AddWithValue("pnl_pct", trade.PnLPercent ?? 0m);
        cmd.Parameters.AddWithValue("now", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateAsync(TradeRecord tradeRecord)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            UPDATE trades SET
                exit_time = @exit_time, exit_price = @exit_price,
                option_exit_price = @opt_exit, exit_reason = @exit_reason,
                state = @state, pnl = @pnl, pnl_percent = @pnl_pct, updated_at = @now
            WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", tradeRecord.Id);
        cmd.Parameters.AddWithValue("exit_time", tradeRecord.ExitTime.HasValue ? (object)tradeRecord.ExitTime.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("exit_price", tradeRecord.ExitPrice.HasValue ? (object)tradeRecord.ExitPrice.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("opt_exit", tradeRecord.OptionExitPrice.HasValue ? (object)tradeRecord.OptionExitPrice.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("exit_reason", tradeRecord.ExitReason ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("state", tradeRecord.State);
        cmd.Parameters.AddWithValue("pnl", tradeRecord.PnL);
        cmd.Parameters.AddWithValue("pnl_pct", tradeRecord.PnLPercent);
        cmd.Parameters.AddWithValue("now", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<TradeRecord>> GetByInstrumentAsync(string instrumentKey, DateTime? startDate = null, DateTime? endDate = null)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        var sql = "SELECT * FROM trades WHERE instrument_key = @key";
        if (startDate.HasValue) sql += " AND entry_time >= @start";
        if (endDate.HasValue) sql += " AND entry_time <= @end";
        sql += " ORDER BY entry_time DESC";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("key", instrumentKey);
        if (startDate.HasValue) cmd.Parameters.AddWithValue("start", startDate.Value);
        if (endDate.HasValue) cmd.Parameters.AddWithValue("end", endDate.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<TradeRecord>();
        while (await reader.ReadAsync())
            list.Add(MapTradeRecord(reader));
        return list;
    }

    public async Task<List<TradeRecord>> GetTodayAsync(string instrumentKey)
    {
        var today = DateTime.UtcNow.Date;
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT * FROM trades
            WHERE instrument_key = @key AND entry_time >= @today
            ORDER BY entry_time ASC", conn);
        cmd.Parameters.AddWithValue("key", instrumentKey);
        cmd.Parameters.AddWithValue("today", today);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<TradeRecord>();
        while (await reader.ReadAsync())
            list.Add(MapTradeRecord(reader));
        return list;
    }

    public async Task<TradeRecord?> GetByIdAsync(Guid id)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT * FROM trades WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapTradeRecord(reader) : null;
    }

    private static TradeRecord MapTradeRecord(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid(r.GetOrdinal("id")),
        InstrumentKey = r.GetString(r.GetOrdinal("instrument_key")),
        TradeType = r.GetString(r.GetOrdinal("trade_type")),
        EntryTime = r.GetDateTime(r.GetOrdinal("entry_time")),
        ExitTime = r.IsDBNull(r.GetOrdinal("exit_time")) ? null : r.GetDateTime(r.GetOrdinal("exit_time")),
        EntryPrice = r.GetDecimal(r.GetOrdinal("entry_price")),
        ExitPrice = r.IsDBNull(r.GetOrdinal("exit_price")) ? null : r.GetDecimal(r.GetOrdinal("exit_price")),
        Quantity = r.GetInt32(r.GetOrdinal("quantity")),
        StopLoss = r.GetDecimal(r.GetOrdinal("stop_loss")),
        Target = r.GetDecimal(r.GetOrdinal("target")),
        ATRAtEntry = r.GetDecimal(r.GetOrdinal("atr_at_entry")),
        OptionSymbol = r.IsDBNull(r.GetOrdinal("option_symbol")) ? null : r.GetString(r.GetOrdinal("option_symbol")),
        OptionStrike = r.IsDBNull(r.GetOrdinal("option_strike")) ? null : r.GetDecimal(r.GetOrdinal("option_strike")),
        OptionEntryPrice = r.IsDBNull(r.GetOrdinal("option_entry_price")) ? null : r.GetDecimal(r.GetOrdinal("option_entry_price")),
        OptionExitPrice = r.IsDBNull(r.GetOrdinal("option_exit_price")) ? null : r.GetDecimal(r.GetOrdinal("option_exit_price")),
        EntryReason = r.GetString(r.GetOrdinal("entry_reason")),
        ExitReason = r.IsDBNull(r.GetOrdinal("exit_reason")) ? null : r.GetString(r.GetOrdinal("exit_reason")),
        Direction = r.GetString(r.GetOrdinal("direction")),
        State = r.GetString(r.GetOrdinal("state")),
        PnL = r.GetDecimal(r.GetOrdinal("pnl")),
        PnLPercent = r.GetDecimal(r.GetOrdinal("pnl_percent")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("created_at")),
        UpdatedAt = r.IsDBNull(r.GetOrdinal("updated_at")) ? null : r.GetDateTime(r.GetOrdinal("updated_at"))
    };
}
