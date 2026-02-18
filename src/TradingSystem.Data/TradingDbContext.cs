using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;

namespace TradingSystem.Data;

public class TradingDbContext : DbContext
{
    public DbSet<TradingInstrument> Instruments { get; set; } = null!;
    public DbSet<MarketCandle> MarketCandles { get; set; } = null!;
    public DbSet<IndicatorSnapshot> IndicatorSnapshots { get; set; } = null!;
    public DbSet<TradeRecord> Trades { get; set; } = null!;

    public TradingDbContext(DbContextOptions<TradingDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TradingInstrument>(entity =>
        {
            entity.ToTable("instruments");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.InstrumentKey).IsUnique();
            entity.Property(e => e.InstrumentKey).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Exchange).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Symbol).IsRequired().HasMaxLength(50);
            entity.Property(e => e.TickSize).HasPrecision(18, 4);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<MarketCandle>(entity =>
        {
            entity.ToTable("market_candles");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.InstrumentKey, e.TimeframeMinutes, e.Timestamp });
            entity.Property(e => e.InstrumentKey).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Open).HasPrecision(18, 4);
            entity.Property(e => e.High).HasPrecision(18, 4);
            entity.Property(e => e.Low).HasPrecision(18, 4);
            entity.Property(e => e.Close).HasPrecision(18, 4);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<IndicatorSnapshot>(entity =>
        {
            entity.ToTable("indicator_snapshots");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.InstrumentKey, e.TimeframeMinutes, e.Timestamp });
            entity.Property(e => e.InstrumentKey).IsRequired().HasMaxLength(50);
            entity.Property(e => e.EMAFast).HasPrecision(18, 4);
            entity.Property(e => e.EMASlow).HasPrecision(18, 4);
            entity.Property(e => e.RSI).HasPrecision(18, 4);
            entity.Property(e => e.MacdLine).HasPrecision(18, 4);
            entity.Property(e => e.MacdSignal).HasPrecision(18, 4);
            entity.Property(e => e.MacdHistogram).HasPrecision(18, 4);
            entity.Property(e => e.ADX).HasPrecision(18, 4);
            entity.Property(e => e.PlusDI).HasPrecision(18, 4);
            entity.Property(e => e.MinusDI).HasPrecision(18, 4);
            entity.Property(e => e.ATR).HasPrecision(18, 4);
            entity.Property(e => e.BollingerUpper).HasPrecision(18, 4);
            entity.Property(e => e.BollingerMiddle).HasPrecision(18, 4);
            entity.Property(e => e.BollingerLower).HasPrecision(18, 4);
            entity.Property(e => e.VWAP).HasPrecision(18, 4);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<TradeRecord>(entity =>
        {
            entity.ToTable("trades");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.InstrumentKey);
            entity.HasIndex(e => e.EntryTime);
            entity.Property(e => e.InstrumentKey).IsRequired().HasMaxLength(50);
            entity.Property(e => e.TradeType).IsRequired().HasMaxLength(20);
            entity.Property(e => e.EntryPrice).HasPrecision(18, 4);
            entity.Property(e => e.ExitPrice).HasPrecision(18, 4);
            entity.Property(e => e.StopLoss).HasPrecision(18, 4);
            entity.Property(e => e.Target).HasPrecision(18, 4);
            entity.Property(e => e.ATRAtEntry).HasPrecision(18, 4);
            entity.Property(e => e.OptionStrike).HasPrecision(18, 4);
            entity.Property(e => e.OptionEntryPrice).HasPrecision(18, 4);
            entity.Property(e => e.OptionExitPrice).HasPrecision(18, 4);
            entity.Property(e => e.PnL).HasPrecision(18, 4);
            entity.Property(e => e.PnLPercent).HasPrecision(18, 4);
            entity.Property(e => e.Direction).IsRequired().HasMaxLength(10);
            entity.Property(e => e.State).IsRequired().HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });
    }
}
