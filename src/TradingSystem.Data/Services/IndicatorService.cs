using Npgsql;
using TradingSystem.Core.Models;
using TradingSystem.Indicators;

namespace TradingSystem.Data.Services;

public class IndicatorService : IIndicatorService
{
    private readonly DbConnectionFactory _factory;

    public IndicatorService(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task SaveAsync(string instrumentKey, int timeframeMinutes, IndicatorValues indicators)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO indicator_snapshots
                (instrument_key, timeframe_minutes, timestamp,
                 ema_fast, ema_slow, rsi, macd_line, macd_signal, macd_histogram,
                 adx, plus_di, minus_di, atr,
                 bollinger_upper, bollinger_middle, bollinger_lower, vwap, created_at)
            VALUES
                (@key, @tf, @ts,
                 @ema_fast, @ema_slow, @rsi, @macd_line, @macd_signal, @macd_hist,
                 @adx, @plus_di, @minus_di, @atr,
                 @bb_upper, @bb_middle, @bb_lower, @vwap, @now)", conn);
        cmd.Parameters.AddWithValue("key", instrumentKey);
        cmd.Parameters.AddWithValue("tf", timeframeMinutes);
        cmd.Parameters.AddWithValue("ts", indicators.Timestamp);
        cmd.Parameters.AddWithValue("ema_fast", indicators.EMAFast);
        cmd.Parameters.AddWithValue("ema_slow", indicators.EMASlow);
        cmd.Parameters.AddWithValue("rsi", indicators.RSI);
        cmd.Parameters.AddWithValue("macd_line", indicators.MacdLine);
        cmd.Parameters.AddWithValue("macd_signal", indicators.MacdSignal);
        cmd.Parameters.AddWithValue("macd_hist", indicators.MacdHistogram);
        cmd.Parameters.AddWithValue("adx", indicators.ADX);
        cmd.Parameters.AddWithValue("plus_di", indicators.PlusDI);
        cmd.Parameters.AddWithValue("minus_di", indicators.MinusDI);
        cmd.Parameters.AddWithValue("atr", indicators.ATR);
        cmd.Parameters.AddWithValue("bb_upper", indicators.BollingerUpper);
        cmd.Parameters.AddWithValue("bb_middle", indicators.BollingerMiddle);
        cmd.Parameters.AddWithValue("bb_lower", indicators.BollingerLower);
        cmd.Parameters.AddWithValue("vwap", indicators.VWAP);
        cmd.Parameters.AddWithValue("now", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IndicatorSnapshot?> GetLatestAsync(string instrumentKey, int timeframeMinutes)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT * FROM indicator_snapshots
            WHERE instrument_key = @key AND timeframe_minutes = @tf
            ORDER BY timestamp DESC
            LIMIT 1", conn);
        cmd.Parameters.AddWithValue("key", instrumentKey);
        cmd.Parameters.AddWithValue("tf", timeframeMinutes);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapSnapshot(reader) : null;
    }

    public async Task<List<IndicatorSnapshot>> GetRecentAsync(string instrumentKey, int timeframeMinutes, int count)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT * FROM (
                SELECT * FROM indicator_snapshots
                WHERE instrument_key = @key AND timeframe_minutes = @tf
                ORDER BY timestamp DESC
                LIMIT @count
            ) sub
            ORDER BY timestamp ASC", conn);
        cmd.Parameters.AddWithValue("key", instrumentKey);
        cmd.Parameters.AddWithValue("tf", timeframeMinutes);
        cmd.Parameters.AddWithValue("count", count);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<IndicatorSnapshot>();
        while (await reader.ReadAsync())
            list.Add(MapSnapshot(reader));
        return list;
    }

    private static IndicatorSnapshot MapSnapshot(NpgsqlDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("id")),
        InstrumentKey = r.GetString(r.GetOrdinal("instrument_key")),
        TimeframeMinutes = r.GetInt32(r.GetOrdinal("timeframe_minutes")),
        Timestamp = r.GetDateTime(r.GetOrdinal("timestamp")),
        EMAFast = r.GetDecimal(r.GetOrdinal("ema_fast")),
        EMASlow = r.GetDecimal(r.GetOrdinal("ema_slow")),
        RSI = r.GetDecimal(r.GetOrdinal("rsi")),
        MacdLine = r.GetDecimal(r.GetOrdinal("macd_line")),
        MacdSignal = r.GetDecimal(r.GetOrdinal("macd_signal")),
        MacdHistogram = r.GetDecimal(r.GetOrdinal("macd_histogram")),
        ADX = r.GetDecimal(r.GetOrdinal("adx")),
        PlusDI = r.GetDecimal(r.GetOrdinal("plus_di")),
        MinusDI = r.GetDecimal(r.GetOrdinal("minus_di")),
        ATR = r.GetDecimal(r.GetOrdinal("atr")),
        BollingerUpper = r.GetDecimal(r.GetOrdinal("bollinger_upper")),
        BollingerMiddle = r.GetDecimal(r.GetOrdinal("bollinger_middle")),
        BollingerLower = r.GetDecimal(r.GetOrdinal("bollinger_lower")),
        VWAP = r.GetDecimal(r.GetOrdinal("vwap")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("created_at"))
    };
}
