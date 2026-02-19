using Npgsql;
using TradingSystem.Core.Models;

namespace TradingSystem.Data.Services;

public class CandleService : ICandleService
{
    private readonly DbConnectionFactory _factory;

    public CandleService(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task SaveAsync(string instrumentKey, Candle candle)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO market_candles
                (instrument_key, timeframe_minutes, timestamp, open, high, low, close, volume, created_at)
            VALUES (@key, @tf, @ts, @open, @high, @low, @close, @vol, @now)", conn);
        AddCandleParams(cmd, instrumentKey, candle);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SaveBatchAsync(string instrumentKey, List<Candle> candles)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        foreach (var candle in candles)
        {
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO market_candles
                    (instrument_key, timeframe_minutes, timestamp, open, high, low, close, volume, created_at)
                VALUES (@key, @tf, @ts, @open, @high, @low, @close, @vol, @now)", conn, tx);
            AddCandleParams(cmd, instrumentKey, candle);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    public async Task<List<Candle>> GetRecentAsync(string instrumentKey, int timeframeMinutes, int count)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT * FROM (
                SELECT * FROM market_candles
                WHERE instrument_key = @key AND timeframe_minutes = @tf
                ORDER BY timestamp DESC
                LIMIT @count
            ) sub
            ORDER BY timestamp ASC", conn);
        cmd.Parameters.AddWithValue("key", instrumentKey);
        cmd.Parameters.AddWithValue("tf", timeframeMinutes);
        cmd.Parameters.AddWithValue("count", count);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<Candle>();
        while (await reader.ReadAsync())
            list.Add(MapCandle(reader));
        return list;
    }

    public async Task<List<Candle>> GetRangeAsync(string instrumentKey, int timeframeMinutes, DateTime startTime, DateTime endTime)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT * FROM market_candles
            WHERE instrument_key = @key AND timeframe_minutes = @tf
              AND timestamp >= @start AND timestamp <= @end
            ORDER BY timestamp ASC", conn);
        cmd.Parameters.AddWithValue("key", instrumentKey);
        cmd.Parameters.AddWithValue("tf", timeframeMinutes);
        cmd.Parameters.AddWithValue("start", startTime);
        cmd.Parameters.AddWithValue("end", endTime);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<Candle>();
        while (await reader.ReadAsync())
            list.Add(MapCandle(reader));
        return list;
    }

    private static void AddCandleParams(NpgsqlCommand cmd, string instrumentKey, Candle candle)
    {
        cmd.Parameters.AddWithValue("key", instrumentKey);
        cmd.Parameters.AddWithValue("tf", candle.TimeframeMinutes);
        cmd.Parameters.AddWithValue("ts", candle.Timestamp);
        cmd.Parameters.AddWithValue("open", candle.Open);
        cmd.Parameters.AddWithValue("high", candle.High);
        cmd.Parameters.AddWithValue("low", candle.Low);
        cmd.Parameters.AddWithValue("close", candle.Close);
        cmd.Parameters.AddWithValue("vol", candle.Volume);
        cmd.Parameters.AddWithValue("now", DateTime.UtcNow);
    }

    private static Candle MapCandle(NpgsqlDataReader r) => new()
    {
        TimeframeMinutes = r.GetInt32(r.GetOrdinal("timeframe_minutes")),
        Timestamp = r.GetDateTime(r.GetOrdinal("timestamp")),
        Open = r.GetDecimal(r.GetOrdinal("open")),
        High = r.GetDecimal(r.GetOrdinal("high")),
        Low = r.GetDecimal(r.GetOrdinal("low")),
        Close = r.GetDecimal(r.GetOrdinal("close")),
        Volume = r.GetInt64(r.GetOrdinal("volume"))
    };
}
