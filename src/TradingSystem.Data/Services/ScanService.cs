using Npgsql;
using TradingSystem.Core.Models;

namespace TradingSystem.Data.Services;

public class ScanService : IScanService
{
    private readonly DbConnectionFactory _factory;

    public ScanService(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task SaveAsync(ScanSnapshot snapshot)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO scan_snapshots
                (instrument_key, timestamp, market_state, setup_score, bias,
                 adx_score, rsi_score, ema_vwap_score, volume_score, bollinger_score,
                 structure_score, last_close, atr, created_at)
            VALUES
                (@key, @ts, @state, @score, @bias,
                 @adx, @rsi, @ema_vwap, @vol, @bb,
                 @struct, @last_close, @atr, @now)", conn);
        cmd.Parameters.AddWithValue("key", snapshot.InstrumentKey);
        cmd.Parameters.AddWithValue("ts", snapshot.Timestamp);
        cmd.Parameters.AddWithValue("state", snapshot.MarketState);
        cmd.Parameters.AddWithValue("score", snapshot.SetupScore);
        cmd.Parameters.AddWithValue("bias", snapshot.Bias);
        cmd.Parameters.AddWithValue("adx", snapshot.AdxScore);
        cmd.Parameters.AddWithValue("rsi", snapshot.RsiScore);
        cmd.Parameters.AddWithValue("ema_vwap", snapshot.EmaVwapScore);
        cmd.Parameters.AddWithValue("vol", snapshot.VolumeScore);
        cmd.Parameters.AddWithValue("bb", snapshot.BollingerScore);
        cmd.Parameters.AddWithValue("struct", snapshot.StructureScore);
        cmd.Parameters.AddWithValue("last_close", snapshot.LastClose);
        cmd.Parameters.AddWithValue("atr", snapshot.ATR);
        cmd.Parameters.AddWithValue("now", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<ScanSnapshot>> GetTopAsync(int minScore, int limit)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT DISTINCT ON (instrument_key) *
            FROM scan_snapshots
            WHERE setup_score >= @min_score
            ORDER BY instrument_key, timestamp DESC, setup_score DESC
            LIMIT @limit", conn);
        cmd.Parameters.AddWithValue("min_score", minScore);
        cmd.Parameters.AddWithValue("limit", limit);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<ScanSnapshot>();
        while (await reader.ReadAsync())
            list.Add(MapSnapshot(reader));
        return list.OrderByDescending(s => s.SetupScore).ToList();
    }

    private static ScanSnapshot MapSnapshot(NpgsqlDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("id")),
        InstrumentKey = r.GetString(r.GetOrdinal("instrument_key")),
        Timestamp = r.GetDateTime(r.GetOrdinal("timestamp")),
        MarketState = r.GetString(r.GetOrdinal("market_state")),
        SetupScore = r.GetInt32(r.GetOrdinal("setup_score")),
        Bias = r.GetString(r.GetOrdinal("bias")),
        AdxScore = r.GetInt32(r.GetOrdinal("adx_score")),
        RsiScore = r.GetInt32(r.GetOrdinal("rsi_score")),
        EmaVwapScore = r.GetInt32(r.GetOrdinal("ema_vwap_score")),
        VolumeScore = r.GetInt32(r.GetOrdinal("volume_score")),
        BollingerScore = r.GetInt32(r.GetOrdinal("bollinger_score")),
        StructureScore = r.GetInt32(r.GetOrdinal("structure_score")),
        LastClose = r.GetDecimal(r.GetOrdinal("last_close")),
        ATR = r.GetDecimal(r.GetOrdinal("atr")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("created_at"))
    };
}
