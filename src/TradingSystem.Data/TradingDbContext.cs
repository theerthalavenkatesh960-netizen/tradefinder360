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
    public DbSet<PortfolioManagerSession> PortfolioManagerSessions { get; set; } = null!;
    public DbSet<PortfolioManagerTrade> PortfolioManagerTrades { get; set; } = null!;
    public DbSet<NewsArticle> NewsArticles { get; set; } = null!;
    public DbSet<NewsKeyword> NewsKeywords { get; set; } = null!;
    public DbSet<NewsImpact> NewsImpacts { get; set; } = null!;
    public DbSet<MarketSentiment> MarketSentiments { get; set; } = null!;
    public DbSet<FeatureStore> FeatureStore { get; set; } = null!;
    public DbSet<TradeOutcome> TradeOutcomes { get; set; } = null!;
    public DbSet<AIModelVersion> AIModelVersions { get; set; } = null!;
    public DbSet<FactorPerformanceTracking> FactorPerformanceTracking { get; set; } = null!;
    public DbSet<PortfolioPerformanceHistory> PortfolioPerformanceHistories { get; set; } = null!;
    public DbSet<FusionLearningConfig> FusionLearningConfigs { get; set; } = null!;

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

        // MarketCandles - TIERED PARTITIONED (1m, 15m, 1d)
        modelBuilder.Entity<MarketCandle>(entity =>
        {
            entity.ToTable("market_candles");
            
            // Updated composite primary key to include timeframe_minutes for sub-partitioning
            entity.HasKey(e => new { e.Id, e.Timestamp, e.TimeframeMinutes });
            
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

            // Optimized index for chart queries (partition pruning enabled)
            entity.HasIndex(e => new { e.InstrumentId, e.TimeframeMinutes, e.Timestamp })
                .HasDatabaseName("idx_market_candles_chart_query");
            
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

            // Ensure TIMESTAMPTZ type in PostgreSQL
            entity.Property(e => e.Timestamp)
                .HasColumnName("timestamp")
                .HasColumnType("timestamptz")
                .IsRequired();

            entity.Property(e => e.Open).HasColumnName("open").IsRequired().HasPrecision(18, 4);
            entity.Property(e => e.High).HasColumnName("high").IsRequired().HasPrecision(18, 4);
            entity.Property(e => e.Low).HasColumnName("low").IsRequired().HasPrecision(18, 4);
            entity.Property(e => e.Close).HasColumnName("close").IsRequired().HasPrecision(18, 4);
            entity.Property(e => e.Volume).HasColumnName("volume").IsRequired();
            entity.Property(e => e.Timeframe).HasColumnName("timeframe").IsRequired();

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamptz");

            entity.Property(e => e.UpdatedAt)
                .HasColumnName("updated_at")
                .HasColumnType("timestamptz");

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
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
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
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
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
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired().HasMaxLength(100);
            entity.Property(e => e.UpstoxAccessToken).HasColumnName("upstox_access_token");
            entity.Property(e => e.UpstoxRefreshToken).HasColumnName("upstox_refresh_token");
            entity.Property(e => e.TokenIssuedAt).HasColumnName("token_issued_at");
            entity.Property(e => e.PreferredBudget).HasColumnName("preferred_budget").HasPrecision(18, 2);
            entity.Property(e => e.PreferredRiskProfile).HasColumnName("preferred_risk_profile").HasMaxLength(32);
            entity.Property(e => e.PreferredSectors).HasColumnName("preferred_sectors")
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
                );
            entity.Property(e => e.PreferredThemes).HasColumnName("preferred_themes")
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
                );
            entity.Property(e => e.AutoRebalanceEnabled).HasColumnName("auto_rebalance_enabled");
            entity.Property(e => e.CreatedOn).HasColumnName("created_on").IsRequired();
            entity.Property(e => e.UpdatedOn).HasColumnName("updated_on").IsRequired();

            entity.HasIndex(e => e.UserId).IsUnique().HasDatabaseName("idx_user_profiles_user_id");
            entity.HasIndex(e => e.UpdatedOn).HasDatabaseName("idx_user_profiles_updated_on");
        });

        // Portfolio Manager Sessions
        modelBuilder.Entity<PortfolioManagerSession>(entity =>
        {
            entity.ToTable("portfolio_manager_sessions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired().HasMaxLength(100);
            entity.Property(e => e.SessionName).HasColumnName("session_name").IsRequired().HasMaxLength(150);
            entity.Property(e => e.InitialCapital).HasColumnName("initial_capital").IsRequired().HasPrecision(18, 2);
            entity.Property(e => e.RiskProfile).HasColumnName("risk_profile").IsRequired().HasMaxLength(32);
            entity.Property(e => e.PreferredSectors).HasColumnName("preferred_sectors")
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
                );
            entity.Property(e => e.PreferredThemes).HasColumnName("preferred_themes")
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
                );
            entity.Property(e => e.AutoRebalanceEnabled).HasColumnName("auto_rebalance_enabled");
            entity.Property(e => e.MaxPositions).HasColumnName("max_positions").IsRequired();
            entity.Property(e => e.TimeframeMinutes).HasColumnName("timeframe_minutes").IsRequired();
            entity.Property(e => e.MinConfidence).HasColumnName("min_confidence").IsRequired();
            entity.Property(e => e.Mode).HasColumnName("mode").HasConversion<string>().IsRequired().HasMaxLength(20);
            entity.Property(e => e.Status).HasColumnName("status").HasConversion<string>().IsRequired().HasMaxLength(20);
            entity.Property(e => e.LastProvider).HasColumnName("last_provider").HasMaxLength(50);
            entity.Property(e => e.LastModel).HasColumnName("last_model").HasMaxLength(100);
            entity.Property(e => e.LastRunAt).HasColumnName("last_run_at");
            entity.Property(e => e.NextRunAt).HasColumnName("next_run_at");
            entity.Property(e => e.TotalRuns).HasColumnName("total_runs");
            entity.Property(e => e.AllocatedCapital).HasColumnName("allocated_capital").HasPrecision(18, 2);
            entity.Property(e => e.RealizedPnl).HasColumnName("realized_pnl").HasPrecision(18, 2);
            entity.Property(e => e.UnrealizedPnl).HasColumnName("unrealized_pnl").HasPrecision(18, 2);
            entity.Property(e => e.WinRatePercent).HasColumnName("win_rate_percent").HasPrecision(8, 4);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

            entity.HasIndex(e => e.UserId).HasDatabaseName("idx_portfolio_manager_sessions_user_id");
            entity.HasIndex(e => new { e.UserId, e.Status }).HasDatabaseName("idx_portfolio_manager_sessions_user_status");
            entity.HasIndex(e => e.UpdatedAt).HasDatabaseName("idx_portfolio_manager_sessions_updated_at");

            entity.HasMany(e => e.Trades)
                .WithOne(t => t.Session)
                .HasForeignKey(t => t.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Portfolio Manager Trades
        modelBuilder.Entity<PortfolioManagerTrade>(entity =>
        {
            entity.ToTable("portfolio_manager_trades");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.SessionId).HasColumnName("session_id").IsRequired();
            entity.Property(e => e.InstrumentId).HasColumnName("instrument_id").IsRequired();
            entity.Property(e => e.Symbol).HasColumnName("symbol").IsRequired().HasMaxLength(50);
            entity.Property(e => e.InstrumentName).HasColumnName("instrument_name").IsRequired();
            entity.Property(e => e.Sector).HasColumnName("sector").HasMaxLength(100);
            entity.Property(e => e.Strategy).HasColumnName("strategy").HasMaxLength(50);
            entity.Property(e => e.Direction).HasColumnName("direction").IsRequired().HasMaxLength(10);
            entity.Property(e => e.EntryPrice).HasColumnName("entry_price").IsRequired().HasPrecision(18, 4);
            entity.Property(e => e.ExitPrice).HasColumnName("exit_price").HasPrecision(18, 4);
            entity.Property(e => e.CurrentPrice).HasColumnName("current_price").HasPrecision(18, 4);
            entity.Property(e => e.Quantity).HasColumnName("quantity").IsRequired();
            entity.Property(e => e.AllocatedCapital).HasColumnName("allocated_capital").HasPrecision(18, 2);
            entity.Property(e => e.AllocationPercent).HasColumnName("allocation_percent").HasPrecision(8, 4);
            entity.Property(e => e.Confidence).HasColumnName("confidence").HasPrecision(8, 4);
            entity.Property(e => e.StopLoss).HasColumnName("stop_loss").HasPrecision(18, 4);
            entity.Property(e => e.Target).HasColumnName("target").HasPrecision(18, 4);
            entity.Property(e => e.FusionScore).HasColumnName("fusion_score").HasPrecision(8, 4);
            entity.Property(e => e.FusionNewsSignal).HasColumnName("fusion_news_signal").HasPrecision(8, 4);
            entity.Property(e => e.FusionTechnicalSignal).HasColumnName("fusion_technical_signal").HasPrecision(8, 4);
            entity.Property(e => e.FusionSectorSignal).HasColumnName("fusion_sector_signal").HasPrecision(8, 4);
            entity.Property(e => e.FusionDirectionVeto).HasColumnName("fusion_direction_veto");
            entity.Property(e => e.FusionIncluded).HasColumnName("fusion_included");
            entity.Property(e => e.FusionEvidence).HasColumnName("fusion_evidence");
            entity.Property(e => e.Pnl).HasColumnName("pnl").HasPrecision(18, 2);
            entity.Property(e => e.PnlPercent).HasColumnName("pnl_percent").HasPrecision(8, 4);
            entity.Property(e => e.EntryReasoning).HasColumnName("entry_reasoning").IsRequired();
            entity.Property(e => e.ExitReasoning).HasColumnName("exit_reasoning");
            entity.Property(e => e.Signals).HasColumnName("signals")
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
                );
            entity.Property(e => e.ModelProvider).HasColumnName("model_provider").HasMaxLength(50);
            entity.Property(e => e.ModelName).HasColumnName("model_name").HasMaxLength(100);
            entity.Property(e => e.Status).HasColumnName("status").HasConversion<string>().IsRequired().HasMaxLength(20);
            entity.Property(e => e.OpenedAt).HasColumnName("opened_at").IsRequired();
            entity.Property(e => e.ClosedAt).HasColumnName("closed_at");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

            entity.HasIndex(e => new { e.SessionId, e.Status }).HasDatabaseName("idx_portfolio_manager_trades_session_status");
            entity.HasIndex(e => e.InstrumentId).HasDatabaseName("idx_portfolio_manager_trades_instrument");
            entity.HasIndex(e => e.CreatedAt).HasDatabaseName("idx_portfolio_manager_trades_created_at");

            entity.HasOne(e => e.Session)
                .WithMany(s => s.Trades)
                .HasForeignKey(e => e.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Instrument)
                .WithMany()
                .HasForeignKey(e => e.InstrumentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // News Articles
        modelBuilder.Entity<NewsArticle>(entity =>
        {
            entity.ToTable("news_articles");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.ExternalId).HasColumnName("external_id").IsRequired().HasMaxLength(256);
            entity.Property(e => e.Source).HasColumnName("source").IsRequired().HasMaxLength(128);
            entity.Property(e => e.Url).HasColumnName("url").IsRequired();
            entity.Property(e => e.Headline).HasColumnName("headline").IsRequired();
            entity.Property(e => e.Summary).HasColumnName("summary").IsRequired();
            entity.Property(e => e.PublishedAt).HasColumnName("published_at").IsRequired();
            entity.Property(e => e.IngestedAt).HasColumnName("ingested_at").IsRequired();
            entity.Property(e => e.Sentiment).HasColumnName("sentiment").HasConversion<string>().IsRequired().HasMaxLength(16);
            entity.Property(e => e.SentimentScore).HasColumnName("sentiment_score").HasPrecision(8, 4);

            entity.HasIndex(e => e.ExternalId).IsUnique().HasDatabaseName("idx_news_articles_external_id");
            entity.HasIndex(e => e.PublishedAt).HasDatabaseName("idx_news_articles_published_at");
            entity.HasIndex(e => e.Source).HasDatabaseName("idx_news_articles_source");

            entity.HasMany(e => e.Keywords)
                .WithOne(k => k.Article)
                .HasForeignKey(k => k.ArticleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Impacts)
                .WithOne(i => i.Article)
                .HasForeignKey(i => i.ArticleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // News Keywords
        modelBuilder.Entity<NewsKeyword>(entity =>
        {
            entity.ToTable("news_keywords");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.ArticleId).HasColumnName("article_id").IsRequired();
            entity.Property(e => e.Keyword).HasColumnName("keyword").IsRequired().HasMaxLength(128);
            entity.Property(e => e.RelevanceScore).HasColumnName("relevance_score").HasPrecision(8, 4);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

            entity.HasIndex(e => e.ArticleId).HasDatabaseName("idx_news_keywords_article_id");
            entity.HasIndex(e => e.Keyword).HasDatabaseName("idx_news_keywords_keyword");
        });

        // News Impacts
        modelBuilder.Entity<NewsImpact>(entity =>
        {
            entity.ToTable("news_impacts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.ArticleId).HasColumnName("article_id").IsRequired();
            entity.Property(e => e.InstrumentId).HasColumnName("instrument_id");
            entity.Property(e => e.SectorId).HasColumnName("sector_id");
            entity.Property(e => e.Direction).HasColumnName("direction").HasConversion<string>().IsRequired().HasMaxLength(16);
            entity.Property(e => e.ImpactScore).HasColumnName("impact_score").HasPrecision(8, 4);
            entity.Property(e => e.Confidence).HasColumnName("confidence").HasPrecision(8, 4);
            entity.Property(e => e.Rationale).HasColumnName("rationale").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

            entity.HasIndex(e => e.ArticleId).HasDatabaseName("idx_news_impacts_article_id");
            entity.HasIndex(e => e.InstrumentId).HasDatabaseName("idx_news_impacts_instrument_id");
            entity.HasIndex(e => e.SectorId).HasDatabaseName("idx_news_impacts_sector_id");
            entity.HasIndex(e => e.CreatedAt).HasDatabaseName("idx_news_impacts_created_at");

            entity.HasOne(e => e.Instrument)
                .WithMany()
                .HasForeignKey(e => e.InstrumentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Sector)
                .WithMany()
                .HasForeignKey(e => e.SectorId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // MarketSentiments
        modelBuilder.Entity<MarketSentiment>(entity =>
        {
            entity.ToTable("market_sentiments");

            // Primary Key
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .ValueGeneratedOnAdd();

            entity.Property(e => e.Timestamp)
                .HasColumnName("timestamp")
                .IsRequired();

            // ✅ FIXED: Store enum as STRING (matches VARCHAR in DB)
            entity.Property(e => e.Sentiment)
                .HasColumnName("sentiment")
                .HasConversion<string>()
                .IsRequired();

            entity.Property(e => e.SentimentScore)
                .HasColumnName("sentiment_score")
                .HasPrecision(5, 2)
                .IsRequired();

            entity.Property(e => e.VolatilityIndex)
                .HasColumnName("volatility_index")
                .HasPrecision(5, 2)
                .IsRequired();

            entity.Property(e => e.MarketBreadth)
                .HasColumnName("market_breadth")
                .HasPrecision(10, 4)
                .IsRequired();

            // ── Multi-period indicator columns ───────────────────────────────
            entity.Property(e => e.RSI)
                .HasColumnName("rsi")
                .HasPrecision(8, 4)
                .IsRequired();

            entity.Property(e => e.MacdHistogram)
                .HasColumnName("macd_histogram")
                .HasPrecision(18, 4)
                .IsRequired();

            entity.Property(e => e.PriceVs20DMA)
                .HasColumnName("price_vs_20dma")
                .HasPrecision(8, 4)
                .IsRequired();

            entity.Property(e => e.PriceVs50DMA)
                .HasColumnName("price_vs_50dma")
                .HasPrecision(8, 4)
                .IsRequired();

            entity.Property(e => e.NewHighs52W)
                .HasColumnName("new_highs_52w")
                .IsRequired();

            entity.Property(e => e.NewLows52W)
                .HasColumnName("new_lows_52w")
                .IsRequired();

            entity.Property(e => e.MclellanOscillator)
                .HasColumnName("mclellan_oscillator")
                .HasPrecision(12, 4)
                .IsRequired();

            entity.Property(e => e.VixVs20DMA)
                .HasColumnName("vix_vs_20dma")
                .HasPrecision(8, 4)
                .IsRequired();

            // ✅ FIXED: JSONB mapping (NO nvarchar)
            entity.Property(e => e.IndexPerformance)
                .HasColumnName("index_performance")
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<IndexPerformance>>(v, (JsonSerializerOptions?)null)!
                );

            entity.Property(e => e.SectorPerformance)
                .HasColumnName("sector_performance")
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<SectorPerformance>>(v, (JsonSerializerOptions?)null)!
                );

            // ✅ FIXED: PostgreSQL native array (NO JSON conversion)
            entity.Property(e => e.KeyFactors)
                .HasColumnName("key_factors")
                .HasColumnType("text[]")
                .IsRequired();

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Indexes
            entity.HasIndex(e => e.Timestamp)
                .HasDatabaseName("idx_market_sentiments_timestamp");

            entity.HasIndex(e => e.Sentiment)
                .HasDatabaseName("idx_market_sentiments_sentiment");

            entity.HasIndex(e => e.CreatedAt)
                .HasDatabaseName("idx_market_sentiments_created_at");
        });

        // FeatureStore - ML Feature Storage
        modelBuilder.Entity<FeatureStore>(entity =>
        {
            entity.ToTable("feature_store");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.InstrumentId).HasColumnName("instrument_id").IsRequired();
            entity.Property(e => e.Symbol).HasColumnName("symbol").IsRequired().HasMaxLength(50);
            entity.Property(e => e.Timestamp).HasColumnName("timestamp").IsRequired();
            entity.Property(e => e.FeaturesJson).HasColumnName("features_json").IsRequired()
                .HasColumnType("jsonb"); // PostgreSQL JSONB for efficient querying
            entity.Property(e => e.FeatureCount).HasColumnName("feature_count").IsRequired();
            entity.Property(e => e.FeatureVersion).HasColumnName("feature_version").IsRequired()
                .HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

            // Indexes for fast lookups
            entity.HasIndex(e => new { e.InstrumentId, e.Timestamp })
                .HasDatabaseName("idx_feature_store_instrument_time");
            entity.HasIndex(e => e.Timestamp).HasDatabaseName("idx_feature_store_timestamp");
            entity.HasIndex(e => e.Symbol).HasDatabaseName("idx_feature_store_symbol");
            entity.HasIndex(e => e.FeatureVersion).HasDatabaseName("idx_feature_store_version");

            // Foreign key relationship
            entity.HasOne(e => e.Instrument)
                .WithMany()
                .HasForeignKey(e => e.InstrumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // TradeOutcomes
        modelBuilder.Entity<TradeOutcome>(entity =>
        {
            entity.ToTable("trade_outcomes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.InstrumentId).HasColumnName("instrument_id").IsRequired();
            entity.Property(e => e.Symbol).HasColumnName("symbol").IsRequired().HasMaxLength(50);
            entity.Property(e => e.EntryTime).HasColumnName("entry_time").IsRequired();
            entity.Property(e => e.ExitTime).HasColumnName("exit_time");
            entity.Property(e => e.EntryPrice).HasColumnName("entry_price").IsRequired().HasPrecision(18, 4);
            entity.Property(e => e.ExitPrice).HasColumnName("exit_price").HasPrecision(18, 4);
            entity.Property(e => e.Direction).HasColumnName("direction").IsRequired().HasMaxLength(10);
            entity.Property(e => e.Quantity).HasColumnName("quantity").IsRequired().HasPrecision(18, 4);
            entity.Property(e => e.PredictedReturn).HasColumnName("predicted_return").IsRequired();
            entity.Property(e => e.PredictedSuccessProbability).HasColumnName("predicted_success_probability").IsRequired();
            entity.Property(e => e.PredictedRiskScore).HasColumnName("predicted_risk_score").IsRequired();
            entity.Property(e => e.ModelVersion).HasColumnName("model_version").IsRequired().HasMaxLength(20);
            entity.Property(e => e.MetaFactorsJson).HasColumnName("meta_factors_json").HasColumnType("jsonb");
            entity.Property(e => e.MarketRegimeAtEntry).HasColumnName("market_regime_at_entry").HasMaxLength(50);
            entity.Property(e => e.RegimeConfidence).HasColumnName("regime_confidence");
            entity.Property(e => e.ActualReturn).HasColumnName("actual_return");
            entity.Property(e => e.ProfitLoss).HasColumnName("profit_loss").HasPrecision(18, 4);
            entity.Property(e => e.ProfitLossPercent).HasColumnName("profit_loss_percent");
            entity.Property(e => e.IsSuccessful).HasColumnName("is_successful");
            entity.Property(e => e.PredictionError).HasColumnName("prediction_error");
            entity.Property(e => e.PredictionAccuracyScore).HasColumnName("prediction_accuracy_score");
            entity.Property(e => e.FailureReason).HasColumnName("failure_reason");
            entity.Property(e => e.LearningTags).HasColumnName("learning_tags").HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
                );
            entity.Property(e => e.Strategy).HasColumnName("strategy").HasMaxLength(50);
            entity.Property(e => e.Sector).HasColumnName("sector").HasMaxLength(100);
            entity.Property(e => e.Status).HasColumnName("status").IsRequired().HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

            entity.HasIndex(e => e.InstrumentId).HasDatabaseName("idx_trade_outcomes_instrument");
            entity.HasIndex(e => e.EntryTime).HasDatabaseName("idx_trade_outcomes_entry_time");
            entity.HasIndex(e => e.ModelVersion).HasDatabaseName("idx_trade_outcomes_model_version");
            entity.HasIndex(e => e.Status).HasDatabaseName("idx_trade_outcomes_status");
            entity.HasIndex(e => new { e.MarketRegimeAtEntry, e.IsSuccessful }).HasDatabaseName("idx_trade_outcomes_regime_success");

            entity.HasOne(e => e.Instrument)
                .WithMany()
                .HasForeignKey(e => e.InstrumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AIModelVersions
        modelBuilder.Entity<AIModelVersion>(entity =>
        {
            entity.ToTable("ai_model_versions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.Version).HasColumnName("version").IsRequired().HasMaxLength(20);
            entity.Property(e => e.ModelType).HasColumnName("model_type").IsRequired().HasMaxLength(50);
            entity.Property(e => e.TrainingDate).HasColumnName("training_date").IsRequired();
            entity.Property(e => e.TrainingDatasetSize).HasColumnName("training_dataset_size");
            entity.Property(e => e.ValidationDatasetSize).HasColumnName("validation_dataset_size");
            entity.Property(e => e.TrainingDuration).HasColumnName("training_duration");
            entity.Property(e => e.HyperparametersJson).HasColumnName("hyperparameters_json").HasColumnType("jsonb");
            entity.Property(e => e.FeatureImportanceJson).HasColumnName("feature_importance_json").HasColumnType("jsonb");
            entity.Property(e => e.TrainingAccuracy).HasColumnName("training_accuracy");
            entity.Property(e => e.ValidationAccuracy).HasColumnName("validation_accuracy");
            entity.Property(e => e.WinRate).HasColumnName("win_rate");
            entity.Property(e => e.ProfitFactor).HasColumnName("profit_factor");
            entity.Property(e => e.SharpeRatio).HasColumnName("sharpe_ratio");
            entity.Property(e => e.MaxDrawdown).HasColumnName("max_drawdown");
            entity.Property(e => e.AveragePredictionError).HasColumnName("average_prediction_error");
            entity.Property(e => e.TotalPredictions).HasColumnName("total_predictions");
            entity.Property(e => e.SuccessfulPredictions).HasColumnName("successful_predictions");
            entity.Property(e => e.ProductionAccuracy).HasColumnName("production_accuracy");
            entity.Property(e => e.ProductionSharpeRatio).HasColumnName("production_sharpe_ratio");
            entity.Property(e => e.TotalPnL).HasColumnName("total_pnl").HasPrecision(18, 4);
            entity.Property(e => e.Status).HasColumnName("status").IsRequired().HasMaxLength(20);
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.Property(e => e.DeprecationReason).HasColumnName("deprecation_reason");
            entity.Property(e => e.ModelFilePath).HasColumnName("model_file_path");
            entity.Property(e => e.CheckpointPath).HasColumnName("checkpoint_path");
            entity.Property(e => e.ChangeLog).HasColumnName("change_log");
            entity.Property(e => e.ImprovementNotes).HasColumnName("improvement_notes").HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
                );
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(e => e.ActivatedAt).HasColumnName("activated_at");
            entity.Property(e => e.DeprecatedAt).HasColumnName("deprecated_at");

            entity.HasIndex(e => new { e.Version, e.ModelType }).IsUnique().HasDatabaseName("idx_ai_model_versions_unique");
            entity.HasIndex(e => e.IsActive).HasDatabaseName("idx_ai_model_versions_active");
            entity.HasIndex(e => e.Status).HasDatabaseName("idx_ai_model_versions_status");
        });

        // FactorPerformanceTracking
        modelBuilder.Entity<FactorPerformanceTracking>(entity =>
        {
            entity.ToTable("factor_performance_tracking");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.PeriodStart).HasColumnName("period_start").IsRequired();
            entity.Property(e => e.PeriodEnd).HasColumnName("period_end").IsRequired();
            entity.Property(e => e.MomentumWeight).HasColumnName("momentum_weight");
            entity.Property(e => e.TrendWeight).HasColumnName("trend_weight");
            entity.Property(e => e.VolatilityWeight).HasColumnName("volatility_weight");
            entity.Property(e => e.LiquidityWeight).HasColumnName("liquidity_weight");
            entity.Property(e => e.RelativeStrengthWeight).HasColumnName("relative_strength_weight");
            entity.Property(e => e.SentimentWeight).HasColumnName("sentiment_weight");
            entity.Property(e => e.RiskWeight).HasColumnName("risk_weight");
            entity.Property(e => e.MomentumWinRate).HasColumnName("momentum_win_rate");
            entity.Property(e => e.MomentumAvgReturn).HasColumnName("momentum_avg_return");
            entity.Property(e => e.MomentumTradeCount).HasColumnName("momentum_trade_count");
            entity.Property(e => e.TrendWinRate).HasColumnName("trend_win_rate");
            entity.Property(e => e.TrendAvgReturn).HasColumnName("trend_avg_return");
            entity.Property(e => e.TrendTradeCount).HasColumnName("trend_trade_count");
            entity.Property(e => e.SentimentWinRate).HasColumnName("sentiment_win_rate");
            entity.Property(e => e.SentimentAvgReturn).HasColumnName("sentiment_avg_return");
            entity.Property(e => e.SentimentTradeCount).HasColumnName("sentiment_trade_count");
            entity.Property(e => e.TotalTrades).HasColumnName("total_trades");
            entity.Property(e => e.OverallWinRate).HasColumnName("overall_win_rate");
            entity.Property(e => e.OverallSharpeRatio).HasColumnName("overall_sharpe_ratio");
            entity.Property(e => e.RecommendedAdjustmentsJson).HasColumnName("recommended_adjustments_json").HasColumnType("jsonb");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

            entity.HasIndex(e => new { e.PeriodStart, e.PeriodEnd }).HasDatabaseName("idx_factor_performance_period");
        });

        // Portfolio Performance History
        modelBuilder.Entity<PortfolioPerformanceHistory>(entity =>
        {
            entity.ToTable("portfolio_performance_histories");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.SessionId).HasColumnName("session_id").IsRequired();
            entity.Property(e => e.WinRate).HasColumnName("win_rate").HasPrecision(8, 4);
            entity.Property(e => e.SharpeRatio).HasColumnName("sharpe_ratio").HasPrecision(10, 4);
            entity.Property(e => e.MaxDrawdown).HasColumnName("max_drawdown").HasPrecision(8, 4);
            entity.Property(e => e.ProfitFactor).HasColumnName("profit_factor").HasPrecision(10, 4);
            entity.Property(e => e.AverageHoldDays).HasColumnName("average_hold_days").HasPrecision(8, 2);
            entity.Property(e => e.AverageHoldEfficiency).HasColumnName("average_hold_efficiency").HasPrecision(12, 4);
            entity.Property(e => e.AverageFusionScore).HasColumnName("average_fusion_score").HasPrecision(8, 4);
            entity.Property(e => e.VetoRejectionRate).HasColumnName("veto_rejection_rate").HasPrecision(8, 4);
            entity.Property(e => e.TotalTrades).HasColumnName("total_trades").IsRequired();
            entity.Property(e => e.WinningTrades).HasColumnName("winning_trades").IsRequired();
            entity.Property(e => e.LosingTrades).HasColumnName("losing_trades").IsRequired();
            entity.Property(e => e.TotalPnL).HasColumnName("total_pnl").HasPrecision(18, 2);
            entity.Property(e => e.ActiveFusionLearningConfigIteration).HasColumnName("active_fusion_learning_config_iteration");
            entity.Property(e => e.RecordedAt).HasColumnName("recorded_at").IsRequired();

            entity.HasIndex(e => e.SessionId).HasDatabaseName("idx_portfolio_perf_history_session_id");
            entity.HasIndex(e => e.RecordedAt).HasDatabaseName("idx_portfolio_perf_history_recorded_at");

            entity.HasOne(e => e.Session)
                .WithMany()
                .HasForeignKey(e => e.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Fusion Learning Config
        modelBuilder.Entity<FusionLearningConfig>(entity =>
        {
            entity.ToTable("fusion_learning_configs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.Iteration).HasColumnName("iteration").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(e => e.AppliedAt).HasColumnName("applied_at");
            entity.Property(e => e.TechnicalWeight).HasColumnName("technical_weight").HasPrecision(5, 4).IsRequired();
            entity.Property(e => e.NewsWeight).HasColumnName("news_weight").HasPrecision(5, 4).IsRequired();
            entity.Property(e => e.SectorWeight).HasColumnName("sector_weight").HasPrecision(5, 4).IsRequired();
            entity.Property(e => e.MinimumFusionScore).HasColumnName("minimum_fusion_score").HasPrecision(8, 4).IsRequired();
            entity.Property(e => e.NewsNegativeBoundary).HasColumnName("news_negative_boundary").HasPrecision(5, 4).IsRequired();
            entity.Property(e => e.NewsPositiveBoundary).HasColumnName("news_positive_boundary").HasPrecision(5, 4).IsRequired();
            entity.Property(e => e.PriorPerformanceMetricsJson).HasColumnName("prior_performance_metrics_json").HasColumnType("jsonb");
            entity.Property(e => e.PriorConfigJson).HasColumnName("prior_config_json").HasColumnType("jsonb");
            entity.Property(e => e.ReasoningText).HasColumnName("reasoning_text");
            entity.Property(e => e.SessionsAnalyzed).HasColumnName("sessions_analyzed").IsRequired();
            entity.Property(e => e.RiskAssessment).HasColumnName("risk_assessment").HasMaxLength(20).IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            entity.Property(e => e.RolledBackAt).HasColumnName("rolled_back_at");
            entity.Property(e => e.SessionsCompletedUnderThisConfig).HasColumnName("sessions_completed_under_this_config").IsRequired();
            entity.Property(e => e.PerformanceUnderThisConfigJson).HasColumnName("performance_under_this_config_json").HasColumnType("jsonb");

            entity.HasIndex(e => e.Iteration).IsUnique().HasDatabaseName("idx_fusion_learning_config_iteration");
            entity.HasIndex(e => e.Status).HasDatabaseName("idx_fusion_learning_config_status");
            entity.HasIndex(e => e.AppliedAt).HasDatabaseName("idx_fusion_learning_config_applied_at");
            entity.HasIndex(e => e.CreatedAt).HasDatabaseName("idx_fusion_learning_config_created_at");
        });
    }
}
