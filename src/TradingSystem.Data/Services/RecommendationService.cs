using System.Text.Json;
using Npgsql;
using TradingSystem.Core.Models;

namespace TradingSystem.Data.Services;

public class RecommendationService : IRecommendationService
{
    private readonly DbConnectionFactory _factory;

    public RecommendationService(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task SaveAsync(Recommendation recommendation)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO recommendations
                (id, instrument_key, timestamp, direction, entry_price, stop_loss, target,
                 risk_reward_ratio, confidence, option_type, option_strike,
                 explanation_text, reasoning_points, is_active, created_at, expires_at)
            VALUES
                (@id, @key, @ts, @dir, @entry, @sl, @target,
                 @rrr, @conf, @opt_type, @opt_strike,
                 @explain, @reasons::jsonb, @active, @now, @expires)", conn);
        cmd.Parameters.AddWithValue("id", recommendation.Id);
        cmd.Parameters.AddWithValue("key", recommendation.InstrumentKey);
        cmd.Parameters.AddWithValue("ts", recommendation.Timestamp);
        cmd.Parameters.AddWithValue("dir", recommendation.Direction);
        cmd.Parameters.AddWithValue("entry", recommendation.EntryPrice);
        cmd.Parameters.AddWithValue("sl", recommendation.StopLoss);
        cmd.Parameters.AddWithValue("target", recommendation.Target);
        cmd.Parameters.AddWithValue("rrr", recommendation.RiskRewardRatio);
        cmd.Parameters.AddWithValue("conf", recommendation.Confidence);
        cmd.Parameters.AddWithValue("opt_type", recommendation.OptionType ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("opt_strike", recommendation.OptionStrike.HasValue ? (object)recommendation.OptionStrike.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("explain", recommendation.ExplanationText);
        cmd.Parameters.AddWithValue("reasons", JsonSerializer.Serialize(recommendation.ReasoningPoints));
        cmd.Parameters.AddWithValue("active", recommendation.IsActive);
        cmd.Parameters.AddWithValue("now", recommendation.CreatedAt);
        cmd.Parameters.AddWithValue("expires", recommendation.ExpiresAt.HasValue ? (object)recommendation.ExpiresAt.Value : DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<Recommendation>> GetActiveAsync()
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT * FROM recommendations
            WHERE is_active = true AND (expires_at IS NULL OR expires_at > @now)
            ORDER BY confidence DESC", conn);
        cmd.Parameters.AddWithValue("now", DateTime.UtcNow);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<Recommendation>();
        while (await reader.ReadAsync())
            list.Add(MapRecommendation(reader));
        return list;
    }

    public async Task<Recommendation?> GetLatestForInstrumentAsync(string instrumentKey)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT * FROM recommendations
            WHERE instrument_key = @key AND is_active = true
            ORDER BY timestamp DESC
            LIMIT 1", conn);
        cmd.Parameters.AddWithValue("key", instrumentKey);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapRecommendation(reader) : null;
    }

    public async Task ExpireOldAsync(int olderThanMinutes = 60)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-olderThanMinutes);
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            UPDATE recommendations
            SET is_active = false
            WHERE is_active = true AND created_at < @cutoff", conn);
        cmd.Parameters.AddWithValue("cutoff", cutoff);
        await cmd.ExecuteNonQueryAsync();
    }

    private static Recommendation MapRecommendation(NpgsqlDataReader r)
    {
        var reasonsJson = r.GetString(r.GetOrdinal("reasoning_points"));
        var reasons = JsonSerializer.Deserialize<List<string>>(reasonsJson) ?? new List<string>();
        return new Recommendation
        {
            Id = r.GetGuid(r.GetOrdinal("id")),
            InstrumentKey = r.GetString(r.GetOrdinal("instrument_key")),
            Timestamp = r.GetDateTime(r.GetOrdinal("timestamp")),
            Direction = r.GetString(r.GetOrdinal("direction")),
            EntryPrice = r.GetDecimal(r.GetOrdinal("entry_price")),
            StopLoss = r.GetDecimal(r.GetOrdinal("stop_loss")),
            Target = r.GetDecimal(r.GetOrdinal("target")),
            RiskRewardRatio = r.GetDecimal(r.GetOrdinal("risk_reward_ratio")),
            Confidence = r.GetInt32(r.GetOrdinal("confidence")),
            OptionType = r.IsDBNull(r.GetOrdinal("option_type")) ? null : r.GetString(r.GetOrdinal("option_type")),
            OptionStrike = r.IsDBNull(r.GetOrdinal("option_strike")) ? null : r.GetDecimal(r.GetOrdinal("option_strike")),
            ExplanationText = r.GetString(r.GetOrdinal("explanation_text")),
            ReasoningPoints = reasons,
            IsActive = r.GetBoolean(r.GetOrdinal("is_active")),
            CreatedAt = r.GetDateTime(r.GetOrdinal("created_at")),
            ExpiresAt = r.IsDBNull(r.GetOrdinal("expires_at")) ? null : r.GetDateTime(r.GetOrdinal("expires_at"))
        };
    }
}
