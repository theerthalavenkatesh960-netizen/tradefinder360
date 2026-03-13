using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;

namespace TradingSystem.Data;

public class TradingDbContext : DbContext
{
    public DbSet<Sector> Sectors { get; set; } = null!;
    public DbSet<TradingInstrument> Instruments { get; set; } = null!;
    public DbSet<InstrumentPrice> InstrumentPrices { get; set; } = null!;
    public DbSet<MarketCandle> MarketCandles { get; set; } = null!;
    public DbSet<IndicatorSnapshot> IndicatorSnapshots { get; set; } = null!;
    public DbSet<TradeRecord> Trades { get; set; } = null!;
    public DbSet<ScanSnapshot> ScanSnapshots { get; set; } = null!;
    public DbSet<Recommendation> Recommendations { get; set; } = null!;
    public DbSet<UserProfile> UserProfiles { get; set; } = null!;
    public DbSet<MarketSentiment> MarketSentiments { get; set; } = null!;

    public TradingDbContext(DbContextOptions<TradingDbContext> options) 
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Sectors
        modelBuilder.Entity<Sector>(entity =>
        {
            entity.ToTable("sectors");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.Name).HasColumnName("name").IsRequired().HasMaxLength(200);
            entity.Property(e => e.Code).HasColumnName("code").IsRequired().HasMaxLength(50);
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => e.Code).IsUnique().HasDatabaseName("idx_sectors_code");
            entity.HasIndex(e => e.Name).HasDatabaseName("idx_sectors_name");

            entity.HasMany(e => e.Instruments)
                .WithOne(i => i.Sector)
                .HasForeignKey(i => i.SectorId)
                .OnDelete(DeleteBehavior.SetNull);
        });
        
        // Instruments
        modelBuilder.Entity<TradingInstrument>(entity =>
        {
            entity.ToTable("instruments");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.InstrumentKey).HasColumnName("instrument_key").IsRequired().HasMaxLength(50);
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.Exchange).HasColumnName("exchange").IsRequired().HasMaxLength(20);
            entity.Property(e => e.Symbol).HasColumnName("symbol").IsRequired().HasMaxLength(50);
            entity.Property(e => e.InstrumentType).HasColumnName("instrument_type").IsRequired().HasMaxLength(10).HasConversion<string>();
            entity.Property(e => e.LotSize).HasColumnName("lot_size").IsRequired();
            entity.Property(e => e.TickSize).HasColumnName("tick_size").IsRequired().HasPrecision(18, 4);
            entity.Property(e => e.IsDerivativesEnabled).HasColumnName("is_derivatives_enabled").IsRequired();
            entity.Property(e => e.DefaultTradingMode).HasColumnName("default_trading_mode").IsRequired().HasMaxLength(10).HasConversion<string>();
            entity.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
            entity.Property(e => e.SectorId).HasColumnName("sector_id");
            entity.Property(e => e.Industry).HasColumnName("industry");
            entity.Property(e => e.MarketCap).HasColumnName("market_cap").HasPrecision(18, 2);
            entity.Property(e => e.ISIN).HasColumnName("isin");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

            entity.HasIndex(e => e.InstrumentKey).IsUnique().HasDatabaseName("idx_instruments_key");
            entity.HasIndex(e => e.Symbol).HasDatabaseName("idx_instruments_symbol");
            entity.HasIndex(e => e.IsActive).HasDatabaseName("idx_instruments_active");
            entity.HasIndex(e => e.SectorId).HasDatabaseName("idx_instruments_sector_id");

            entity.HasOne(e => e.Sector)
                .WithMany(s => s.Instruments)
                .HasForeignKey(e => e.SectorId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.Prices)
                .WithOne(p => p.Instrument)
                .HasForeignKey(p => p.InstrumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // MarketCandles
        modelBuilder.Entity<MarketCandle>(entity =>
        {
            entity.ToTable("market_candles");
            entity.HasKey(e => new { e.Id, e.Timestamp });
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.InstrumentId).HasColumnName("instrument_id").IsRequired();
            entity.Property(e => e.TimeframeMinutes).HasColumnName("timeframe_minutes").IsRequired();
            entity.Property(e => e.Timestamp).HasColumnName("timestamp").IsRequired();
            entity.Property(e => e.Open).HasColumnName("open").IsRequired().HasPrecision(18, 4);
            entity.Property(e => e.High).HasColumnName("high").IsRequired().HasPrecision(18, 4);
            entity.Property(e => e.Low).HasColumnName("low").IsRequired().HasPrecision(18, 4);
            entity.Property(e => e.Close).HasColumnName("close").IsRequired().HasPrecision(18, 4);
            entity.Property(e => e.Volume).HasColumnName("volume").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

            entity.HasIndex(e => new { e.InstrumentId, e.TimeframeMinutes, e.Timestamp }).HasDatabaseName("idx_market_candles_lookup");
            entity.HasIndex(e => e.InstrumentId).HasDatabaseName("idx_market_candles_instrument");

            entity.HasOne(e => e.Instrument)
                .WithMany()
                .HasForeignKey(e => e.InstrumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // InstrumentPrices
        modelBuilder.Entity<InstrumentPrice>(entity =>
        {
            entity.ToTable("instrument_prices");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.InstrumentId).HasColumnName("instrument_id").IsRequired();
            entity.Property(e => e.Timestamp).HasColumnName("timestamp").IsRequired();
            entity.Property(e => e.Open).HasColumnName("open").IsRequired().HasPrecision(18, 4);
            entity.Property(e => e.High).HasColumnName("high").IsRequired().HasPrecision(18, 4);
            entity.Property(e => e.Low).HasColumnName("low").IsRequired().HasPrecision(18, 4);
            entity.Property(e => e.Close).HasColumnName("close").IsRequired().HasPrecision(18, 4);
            entity.Property(e => e.Volume).HasColumnName("volume").IsRequired();
            entity.Property(e => e.Timeframe).HasColumnName("timeframe").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => new { e.InstrumentId, e.Timeframe, e.Timestamp }).IsUnique().HasDatabaseName("idx_instrument_prices_unique");
            entity.HasIndex(e => e.Timestamp).HasDatabaseName("idx_instrument_prices_timestamp");
            entity.HasIndex(e => new { e.InstrumentId, e.Timeframe }).HasDatabaseName("idx_instrument_prices_instrument_timeframe");

            entity.HasOne(e => e.Instrument)
                .WithMany(i => i.Prices)
                .HasForeignKey(e => e.InstrumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // IndicatorSnapshots
        modelBuilder.Entity<IndicatorSnapshot>(entity =>
        {
            entity.ToTable("indicator_snapshots");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.InstrumentId).HasColumnName("instrument_id").IsRequired();
            entity.Property(e => e.TimeframeMinutes).HasColumnName("timeframe_minutes").IsRequired();
            entity.Property(e => e.Timestamp).HasColumnName("timestamp").IsRequired();
            entity.Property(e => e.EMAFast).HasColumnName("ema_fast").HasPrecision(18, 4);
            entity.Property(e => e.EMASlow).HasColumnName("ema_slow").HasPrecision(18, 4);
            entity.Property(e => e.RSI).HasColumnName("rsi").HasPrecision(18, 4);
            entity.Property(e => e.MacdLine).HasColumnName("macd_line").HasPrecision(18, 4);
            entity.Property(e => e.MacdSignal).HasColumnName("macd_signal").HasPrecision(18, 4);
            entity.Property(e => e.MacdHistogram).HasColumnName("macd_histogram").HasPrecision(18, 4);
            entity.Property(e => e.ADX).HasColumnName("adx").HasPrecision(18, 4);
            entity.Property(e => e.PlusDI).HasColumnName("plus_di").HasPrecision(18, 4);
            entity.Property(e => e.MinusDI).HasColumnName("minus_di").HasPrecision(18, 4);
            entity.Property(e => e.ATR).HasColumnName("atr").HasPrecision(18, 4);
            entity.Property(e => e.BollingerUpper).HasColumnName("bollinger_upper").HasPrecision(18, 4);
            entity.Property(e => e.BollingerMiddle).HasColumnName("bollinger_middle").HasPrecision(18, 4);
            entity.Property(e => e.BollingerLower).HasColumnName("bollinger_lower").HasPrecision(18, 4);
            entity.Property(e => e.VWAP).HasColumnName("vwap").HasPrecision(18, 4);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            entity.HasIndex(e => new { e.InstrumentId, e.TimeframeMinutes, e.Timestamp }).HasDatabaseName("idx_indicator_snapshots_lookup");
            entity.HasIndex(e => e.InstrumentId).HasDatabaseName("idx_indicator_snapshots_instrument");

            entity.HasOne(e => e.Instrument)
                .WithMany()
                .HasForeignKey(e => e.InstrumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Trades
        modelBuilder.Entity<TradeRecord>(entity =>
        {
            entity.ToTable("trades");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.InstrumentId).HasColumnName("instrument_id").IsRequired();
            entity.Property(e => e.TradeType).HasColumnName("trade_type").IsRequired().HasMaxLength(20);
            entity.Property(e => e.EntryTime).HasColumnName("entry_time").IsRequired();
            entity.Property(e => e.ExitTime).HasColumnName("exit_time");
            entity.Property(e => e.EntryPrice).HasColumnName("entry_price").IsRequired().HasPrecision(18, 4);
            entity.Property(e => e.ExitPrice).HasColumnName("exit_price").HasPrecision(18, 4);
            entity.Property(e => e.Quantity).HasColumnName("quantity").IsRequired();
            entity.Property(e => e.StopLoss).HasColumnName("stop_loss").IsRequired().HasPrecision(18, 4);
            entity.Property(e => e.Target).HasColumnName("target").IsRequired().HasPrecision(18, 4);
            entity.Property(e => e.ATRAtEntry).HasColumnName("atr_at_entry").IsRequired().HasPrecision(18, 4);
            entity.Property(e => e.OptionSymbol).HasColumnName("option_symbol").HasMaxLength(100);
            entity.Property(e => e.OptionStrike).HasColumnName("option_strike").HasPrecision(18, 4);
            entity.Property(e => e.OptionEntryPrice).HasColumnName("option_entry_price").HasPrecision(18, 4);
            entity.Property(e => e.OptionExitPrice).HasColumnName("option_exit_price").HasPrecision(18, 4);
            entity.Property(e => e.EntryReason).HasColumnName("entry_reason").IsRequired();
            entity.Property(e => e.ExitReason).HasColumnName("exit_reason");
            entity.Property(e => e.Direction).HasColumnName("direction").IsRequired().HasMaxLength(10);
            entity.Property(e => e.State).HasColumnName("state").IsRequired().HasMaxLength(20);
            entity.Property(e => e.PnL).HasColumnName("pnl").HasPrecision(18, 4);
            entity.Property(e => e.PnLPercent).HasColumnName("pnl_percent").HasPrecision(18, 4);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => e.InstrumentId).HasDatabaseName("idx_trades_instrument");
            entity.HasIndex(e => e.EntryTime).HasDatabaseName("idx_trades_entry_time");
            entity.HasIndex(e => e.State).HasDatabaseName("idx_trades_state");

            entity.HasOne(e => e.Instrument)
                .WithMany()
                .HasForeignKey(e => e.InstrumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ScanSnapshots
        modelBuilder.Entity<ScanSnapshot>(entity =>
        {
            entity.ToTable("scan_snapshots");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.InstrumentId).HasColumnName("instrument_id").IsRequired();
            entity.Property(e => e.Timestamp).HasColumnName("timestamp");
            entity.Property(e => e.MarketState).HasColumnName("market_state").IsRequired().HasMaxLength(30);
            entity.Property(e => e.SetupScore).HasColumnName("setup_score");
            entity.Property(e => e.Bias).HasColumnName("bias").HasMaxLength(10);
            entity.Property(e => e.AdxScore).HasColumnName("adx_score");
            entity.Property(e => e.RsiScore).HasColumnName("rsi_score");
            entity.Property(e => e.EmaVwapScore).HasColumnName("ema_vwap_score");
            entity.Property(e => e.VolumeScore).HasColumnName("volume_score");
            entity.Property(e => e.BollingerScore).HasColumnName("bollinger_score");
            entity.Property(e => e.StructureScore).HasColumnName("structure_score");
            entity.Property(e => e.LastClose).HasColumnName("last_close").HasPrecision(18, 4);
            entity.Property(e => e.ATR).HasColumnName("atr").HasPrecision(18, 4);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            entity.HasIndex(e => new { e.InstrumentId, e.Timestamp }).HasDatabaseName("idx_scan_snapshots_lookup");
            entity.HasIndex(e => new { e.SetupScore, e.Timestamp }).HasDatabaseName("idx_scan_snapshots_score");
            entity.HasIndex(e => e.InstrumentId).HasDatabaseName("idx_scan_snapshots_instrument");

            entity.HasOne(e => e.Instrument)
                .WithMany()
                .HasForeignKey(e => e.InstrumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Recommendations
        modelBuilder.Entity<Recommendation>(entity =>
        {
            entity.ToTable("recommendations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.InstrumentId).HasColumnName("instrument_id").IsRequired();
            entity.Property(e => e.Timestamp).HasColumnName("timestamp");
            entity.Property(e => e.Direction).HasColumnName("direction").IsRequired().HasMaxLength(10);
            entity.Property(e => e.EntryPrice).HasColumnName("entry_price").IsRequired().HasPrecision(18, 4);
            entity.Property(e => e.StopLoss).HasColumnName("stop_loss").IsRequired().HasPrecision(18, 4);
            entity.Property(e => e.Target).HasColumnName("target").IsRequired().HasPrecision(18, 4);
            entity.Property(e => e.RiskRewardRatio).HasColumnName("risk_reward_ratio").HasPrecision(8, 2);
            entity.Property(e => e.Confidence).HasColumnName("confidence");
            entity.Property(e => e.OptionType).HasColumnName("option_type").HasMaxLength(10);
            entity.Property(e => e.OptionStrike).HasColumnName("option_strike").HasPrecision(18, 4);
            entity.Property(e => e.ExplanationText).HasColumnName("explanation_text");
            entity.Property(e => e.ReasoningPoints).HasColumnName("reasoning_points")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
            );
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");

            entity.HasIndex(e => new { e.InstrumentId, e.Timestamp }).HasDatabaseName("idx_recommendations_instrument");
            entity.HasIndex(e => new { e.IsActive, e.Timestamp }).HasDatabaseName("idx_recommendations_active");
            entity.HasIndex(e => new { e.Confidence, e.Timestamp }).HasDatabaseName("idx_recommendations_confidence");

            entity.HasOne(e => e.Instrument)
                .WithMany()
                .HasForeignKey(e => e.InstrumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // UserProfiles
        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.ToTable("user_profiles");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired().HasMaxLength(100);
            entity.Property(e => e.UpstoxAccessToken).HasColumnName("upstox_access_token");
            entity.Property(e => e.UpstoxRefreshToken).HasColumnName("upstox_refresh_token");
            entity.Property(e => e.TokenIssuedAt).HasColumnName("token_issued_at");
            entity.Property(e => e.CreatedOn).HasColumnName("created_on").IsRequired();
            entity.Property(e => e.UpdatedOn).HasColumnName("updated_on").IsRequired();

            entity.HasIndex(e => e.UserId).IsUnique().HasDatabaseName("idx_user_profiles_user_id");
            entity.HasIndex(e => e.UpdatedOn).HasDatabaseName("idx_user_profiles_updated_on");
        });

        // MarketSentiments
        modelBuilder.Entity<MarketSentiment>(entity =>
        {
            entity.ToTable("market_sentiments");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.Timestamp).HasColumnName("timestamp").IsRequired();
            entity.Property(e => e.Sentiment).HasColumnName("sentiment").IsRequired()
                .HasConversion<int>(); // Store enum as int
            entity.Property(e => e.SentimentScore).HasColumnName("sentiment_score").IsRequired()
                .HasPrecision(18, 2);
            entity.Property(e => e.VolatilityIndex).HasColumnName("volatility_index").IsRequired()
                .HasPrecision(18, 2);
            entity.Property(e => e.MarketBreadth).HasColumnName("market_breadth").IsRequired()
                .HasPrecision(18, 4);
            entity.Property(e => e.IndexPerformance).HasColumnName("index_performance").IsRequired()
                .HasColumnType("nvarchar(max)");
            entity.Property(e => e.SectorPerformance).HasColumnName("sector_performance").IsRequired()
                .HasColumnType("nvarchar(max)");
            entity.Property(e => e.KeyFactors).HasColumnName("key_factors")
                .HasColumnType("nvarchar(max)")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
                );
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

            entity.HasIndex(e => e.Timestamp).HasDatabaseName("idx_market_sentiments_timestamp");
            entity.HasIndex(e => e.Sentiment).HasDatabaseName("idx_market_sentiments_sentiment");
            entity.HasIndex(e => e.CreatedAt).HasDatabaseName("idx_market_sentiments_created_at");
        });
    }
}
