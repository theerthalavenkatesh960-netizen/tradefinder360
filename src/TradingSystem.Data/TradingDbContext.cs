using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;

namespace TradingSystem.Data;

public class TradingDbContext : DbContext
{
    public DbSet<TradingInstrument> Instruments { get; set; } = null!;
    public DbSet<InstrumentPrice> InstrumentPrices { get; set; } = null!;
    public DbSet<MarketCandle> MarketCandles { get; set; } = null!;
    public DbSet<IndicatorSnapshot> IndicatorSnapshots { get; set; } = null!;
    public DbSet<TradeRecord> Trades { get; set; } = null!;
    public DbSet<ScanSnapshot> ScanSnapshots { get; set; } = null!;
    public DbSet<Recommendation> Recommendations { get; set; } = null!;

    public TradingDbContext(DbContextOptions<TradingDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TradingInstrument>(e =>
        {
            e.ToTable("instruments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.InstrumentKey).HasColumnName("instrument_key").IsRequired();
            e.Property(x => x.Exchange).HasColumnName("exchange").IsRequired();
            e.Property(x => x.Symbol).HasColumnName("symbol").IsRequired();
            e.Property(x => x.Name).HasColumnName("name").IsRequired();
            e.Property(x => x.InstrumentType).HasColumnName("instrument_type").IsRequired();
            e.Property(x => x.LotSize).HasColumnName("lot_size");
            e.Property(x => x.TickSize).HasColumnName("tick_size").HasPrecision(18, 4);
            e.Property(x => x.IsDerivativesEnabled).HasColumnName("is_derivatives_enabled");
            e.Property(x => x.DefaultTradingMode).HasColumnName("default_trading_mode");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => x.InstrumentKey).IsUnique();
            e.HasIndex(x => x.Symbol);
            e.HasMany(x => x.Prices)
                .WithOne(x => x.Instrument)
                .HasForeignKey(x => x.InstrumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InstrumentPrice>(e =>
        {
            e.ToTable("instrument_prices");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.InstrumentId).HasColumnName("instrument_id").IsRequired();
            e.Property(x => x.Timestamp).HasColumnName("timestamp").IsRequired();
            e.Property(x => x.Open).HasColumnName("open").HasPrecision(18, 4);
            e.Property(x => x.High).HasColumnName("high").HasPrecision(18, 4);
            e.Property(x => x.Low).HasColumnName("low").HasPrecision(18, 4);
            e.Property(x => x.Close).HasColumnName("close").HasPrecision(18, 4);
            e.Property(x => x.Volume).HasColumnName("volume");
            e.Property(x => x.Timeframe).HasColumnName("timeframe").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.InstrumentId, x.Timeframe, x.Timestamp }).IsUnique();
            e.HasIndex(x => x.Timestamp);
        });

        modelBuilder.Entity<MarketCandle>(e =>
        {
            e.ToTable("market_candles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.InstrumentKey).HasColumnName("instrument_key").IsRequired();
            e.Property(x => x.TimeframeMinutes).HasColumnName("timeframe_minutes");
            e.Property(x => x.Timestamp).HasColumnName("timestamp");
            e.Property(x => x.Open).HasColumnName("open").HasPrecision(18, 4);
            e.Property(x => x.High).HasColumnName("high").HasPrecision(18, 4);
            e.Property(x => x.Low).HasColumnName("low").HasPrecision(18, 4);
            e.Property(x => x.Close).HasColumnName("close").HasPrecision(18, 4);
            e.Property(x => x.Volume).HasColumnName("volume");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => new { x.InstrumentKey, x.TimeframeMinutes, x.Timestamp });
        });

        modelBuilder.Entity<IndicatorSnapshot>(e =>
        {
            e.ToTable("indicator_snapshots");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.InstrumentKey).HasColumnName("instrument_key").IsRequired();
            e.Property(x => x.TimeframeMinutes).HasColumnName("timeframe_minutes");
            e.Property(x => x.Timestamp).HasColumnName("timestamp");
            e.Property(x => x.EMAFast).HasColumnName("ema_fast").HasPrecision(18, 4);
            e.Property(x => x.EMASlow).HasColumnName("ema_slow").HasPrecision(18, 4);
            e.Property(x => x.RSI).HasColumnName("rsi").HasPrecision(18, 4);
            e.Property(x => x.MacdLine).HasColumnName("macd_line").HasPrecision(18, 4);
            e.Property(x => x.MacdSignal).HasColumnName("macd_signal").HasPrecision(18, 4);
            e.Property(x => x.MacdHistogram).HasColumnName("macd_histogram").HasPrecision(18, 4);
            e.Property(x => x.ADX).HasColumnName("adx").HasPrecision(18, 4);
            e.Property(x => x.PlusDI).HasColumnName("plus_di").HasPrecision(18, 4);
            e.Property(x => x.MinusDI).HasColumnName("minus_di").HasPrecision(18, 4);
            e.Property(x => x.ATR).HasColumnName("atr").HasPrecision(18, 4);
            e.Property(x => x.BollingerUpper).HasColumnName("bollinger_upper").HasPrecision(18, 4);
            e.Property(x => x.BollingerMiddle).HasColumnName("bollinger_middle").HasPrecision(18, 4);
            e.Property(x => x.BollingerLower).HasColumnName("bollinger_lower").HasPrecision(18, 4);
            e.Property(x => x.VWAP).HasColumnName("vwap").HasPrecision(18, 4);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => new { x.InstrumentKey, x.TimeframeMinutes, x.Timestamp });
        });

        modelBuilder.Entity<TradeRecord>(e =>
        {
            e.ToTable("trades");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.InstrumentKey).HasColumnName("instrument_key").IsRequired();
            e.Property(x => x.TradeType).HasColumnName("trade_type");
            e.Property(x => x.EntryTime).HasColumnName("entry_time");
            e.Property(x => x.ExitTime).HasColumnName("exit_time");
            e.Property(x => x.EntryPrice).HasColumnName("entry_price").HasPrecision(18, 4);
            e.Property(x => x.ExitPrice).HasColumnName("exit_price").HasPrecision(18, 4);
            e.Property(x => x.Quantity).HasColumnName("quantity");
            e.Property(x => x.StopLoss).HasColumnName("stop_loss").HasPrecision(18, 4);
            e.Property(x => x.Target).HasColumnName("target").HasPrecision(18, 4);
            e.Property(x => x.ATRAtEntry).HasColumnName("atr_at_entry").HasPrecision(18, 4);
            e.Property(x => x.OptionSymbol).HasColumnName("option_symbol");
            e.Property(x => x.OptionStrike).HasColumnName("option_strike").HasPrecision(18, 4);
            e.Property(x => x.OptionEntryPrice).HasColumnName("option_entry_price").HasPrecision(18, 4);
            e.Property(x => x.OptionExitPrice).HasColumnName("option_exit_price").HasPrecision(18, 4);
            e.Property(x => x.EntryReason).HasColumnName("entry_reason");
            e.Property(x => x.ExitReason).HasColumnName("exit_reason");
            e.Property(x => x.Direction).HasColumnName("direction");
            e.Property(x => x.State).HasColumnName("state");
            e.Property(x => x.PnL).HasColumnName("pnl").HasPrecision(18, 4);
            e.Property(x => x.PnLPercent).HasColumnName("pnl_percent").HasPrecision(18, 4);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.InstrumentKey, x.EntryTime });
        });

        modelBuilder.Entity<ScanSnapshot>(e =>
        {
            e.ToTable("scan_snapshots");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.InstrumentKey).HasColumnName("instrument_key").IsRequired();
            e.Property(x => x.Timestamp).HasColumnName("timestamp");
            e.Property(x => x.MarketState).HasColumnName("market_state");
            e.Property(x => x.SetupScore).HasColumnName("setup_score");
            e.Property(x => x.Bias).HasColumnName("bias");
            e.Property(x => x.AdxScore).HasColumnName("adx_score");
            e.Property(x => x.RsiScore).HasColumnName("rsi_score");
            e.Property(x => x.EmaVwapScore).HasColumnName("ema_vwap_score");
            e.Property(x => x.VolumeScore).HasColumnName("volume_score");
            e.Property(x => x.BollingerScore).HasColumnName("bollinger_score");
            e.Property(x => x.StructureScore).HasColumnName("structure_score");
            e.Property(x => x.LastClose).HasColumnName("last_close").HasPrecision(18, 4);
            e.Property(x => x.ATR).HasColumnName("atr").HasPrecision(18, 4);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => new { x.InstrumentKey, x.Timestamp });
        });

        modelBuilder.Entity<Recommendation>(e =>
        {
            e.ToTable("recommendations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.InstrumentKey).HasColumnName("instrument_key").IsRequired();
            e.Property(x => x.Timestamp).HasColumnName("timestamp");
            e.Property(x => x.Direction).HasColumnName("direction");
            e.Property(x => x.EntryPrice).HasColumnName("entry_price").HasPrecision(18, 4);
            e.Property(x => x.StopLoss).HasColumnName("stop_loss").HasPrecision(18, 4);
            e.Property(x => x.Target).HasColumnName("target").HasPrecision(18, 4);
            e.Property(x => x.RiskRewardRatio).HasColumnName("risk_reward_ratio").HasPrecision(18, 4);
            e.Property(x => x.Confidence).HasColumnName("confidence");
            e.Property(x => x.OptionType).HasColumnName("option_type");
            e.Property(x => x.OptionStrike).HasColumnName("option_strike").HasPrecision(18, 4);
            e.Property(x => x.ExplanationText).HasColumnName("explanation_text");
            e.Property(x => x.ReasoningPoints).HasColumnName("reasoning_points")
                .HasColumnType("jsonb");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.HasIndex(x => new { x.InstrumentKey, x.IsActive });
        });
    }
}
