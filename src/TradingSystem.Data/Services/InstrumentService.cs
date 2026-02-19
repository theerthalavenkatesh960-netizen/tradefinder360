using Npgsql;
using TradingSystem.Core.Models;

namespace TradingSystem.Data.Services;

public class InstrumentService : IInstrumentService
{
    private readonly DbConnectionFactory _factory;

    public InstrumentService(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<TradingInstrument?> GetByKeyAsync(string instrumentKey)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM instruments WHERE instrument_key = @key LIMIT 1", conn);
        cmd.Parameters.AddWithValue("key", instrumentKey);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapInstrument(reader) : null;
    }

    public async Task<List<TradingInstrument>> GetActiveAsync()
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM instruments WHERE is_active = true ORDER BY symbol", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<TradingInstrument>();
        while (await reader.ReadAsync())
            list.Add(MapInstrument(reader));
        return list;
    }

    public async Task<Dictionary<string, string>> GetKeyToSymbolMapAsync()
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT instrument_key, symbol FROM instruments", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var map = new Dictionary<string, string>();
        while (await reader.ReadAsync())
            map[reader.GetString(0)] = reader.GetString(1);
        return map;
    }

    public async Task AddAsync(TradingInstrument instrument)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO instruments
                (instrument_key, exchange, symbol, instrument_type, lot_size, tick_size,
                 is_derivatives_enabled, default_trading_mode, is_active, created_at)
            VALUES
                (@key, @exchange, @symbol, @type, @lot, @tick, @deriv, @mode, @active, @now)
            ON CONFLICT (instrument_key) DO NOTHING", conn);
        cmd.Parameters.AddWithValue("key", instrument.InstrumentKey);
        cmd.Parameters.AddWithValue("exchange", instrument.Exchange);
        cmd.Parameters.AddWithValue("symbol", instrument.Symbol);
        cmd.Parameters.AddWithValue("type", instrument.InstrumentType);
        cmd.Parameters.AddWithValue("lot", instrument.LotSize);
        cmd.Parameters.AddWithValue("tick", instrument.TickSize);
        cmd.Parameters.AddWithValue("deriv", instrument.IsDerivativesEnabled);
        cmd.Parameters.AddWithValue("mode", instrument.DefaultTradingMode);
        cmd.Parameters.AddWithValue("active", instrument.IsActive);
        cmd.Parameters.AddWithValue("now", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateAsync(TradingInstrument instrument)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            UPDATE instruments SET
                exchange = @exchange, symbol = @symbol, instrument_type = @type,
                lot_size = @lot, tick_size = @tick, is_derivatives_enabled = @deriv,
                default_trading_mode = @mode, is_active = @active, updated_at = @now
            WHERE instrument_key = @key", conn);
        cmd.Parameters.AddWithValue("key", instrument.InstrumentKey);
        cmd.Parameters.AddWithValue("exchange", instrument.Exchange);
        cmd.Parameters.AddWithValue("symbol", instrument.Symbol);
        cmd.Parameters.AddWithValue("type", instrument.InstrumentType);
        cmd.Parameters.AddWithValue("lot", instrument.LotSize);
        cmd.Parameters.AddWithValue("tick", instrument.TickSize);
        cmd.Parameters.AddWithValue("deriv", instrument.IsDerivativesEnabled);
        cmd.Parameters.AddWithValue("mode", instrument.DefaultTradingMode);
        cmd.Parameters.AddWithValue("active", instrument.IsActive);
        cmd.Parameters.AddWithValue("now", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync();
    }

    private static TradingInstrument MapInstrument(NpgsqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("id")),
        InstrumentKey = r.GetString(r.GetOrdinal("instrument_key")),
        Exchange = r.GetString(r.GetOrdinal("exchange")),
        Symbol = r.GetString(r.GetOrdinal("symbol")),
        InstrumentType = r.GetString(r.GetOrdinal("instrument_type")),
        LotSize = r.GetInt32(r.GetOrdinal("lot_size")),
        TickSize = r.GetDecimal(r.GetOrdinal("tick_size")),
        IsDerivativesEnabled = r.GetBoolean(r.GetOrdinal("is_derivatives_enabled")),
        DefaultTradingMode = r.GetString(r.GetOrdinal("default_trading_mode")),
        IsActive = r.GetBoolean(r.GetOrdinal("is_active")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("created_at")),
        UpdatedAt = r.IsDBNull(r.GetOrdinal("updated_at")) ? null : r.GetDateTime(r.GetOrdinal("updated_at"))
    };
}
