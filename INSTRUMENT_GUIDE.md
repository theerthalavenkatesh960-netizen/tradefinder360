# Multi-Instrument Trading Guide

## Overview

The system now supports trading any instrument on NSE:
- **Indices**: NIFTY, BANKNIFTY, FINNIFTY, MIDCPNIFTY
- **Stocks**: Any NSE-listed equity
- **Trading Modes**: Options or Equity
- **Flexibility**: Per-instrument configuration

## Key Concepts

### Instrument Abstraction

Every tradable asset is defined by:
- `InstrumentKey`: Unique identifier (e.g., "NSE:NIFTY")
- `Symbol`: Trading symbol
- `Exchange`: NSE, BSE, etc.
- `InstrumentType`: INDEX or STOCK
- `LotSize`: Standard lot size
- `IsDerivativesEnabled`: Whether options are available
- `DefaultTradingMode`: OPTIONS or EQUITY

### Trading Flow

```
1. Select Instrument → 2. Fetch Data → 3. Calculate Indicators
                                                ↓
6. Execute Trade ← 5. Risk Check ← 4. Generate Signal
```

The strategy logic remains the same across all instruments.

## Configuration Examples

### Example 1: NIFTY Options (Default)

```json
{
  "Instrument": {
    "ActiveInstrumentKey": "NSE:NIFTY",
    "TradingMode": "OPTIONS"
  },
  "Risk": {
    "StopLossATRMultiplier": 1.5,
    "TargetATRMultiplier": 2.0,
    "MaxDailyLossAmount": 10000,
    "MaxPositionSizePercent": 20
  },
  "Execution": {
    "UseWeeklyOptions": true
  }
}
```

### Example 2: BANKNIFTY with Custom Risk

```json
{
  "Instrument": {
    "ActiveInstrumentKey": "NSE:BANKNIFTY",
    "TradingMode": "OPTIONS",
    "InstrumentOverrides": {
      "NSE:BANKNIFTY": {
        "StopLossATRMultiplier": 2.0,
        "TargetATRMultiplier": 2.5,
        "MaxTradesPerDay": 2,
        "MaxDailyLossAmount": 15000
      }
    }
  }
}
```

### Example 3: Stock Trading (Equity)

```json
{
  "Instrument": {
    "ActiveInstrumentKey": "NSE:RELIANCE",
    "TradingMode": "EQUITY",
    "InstrumentOverrides": {
      "NSE:RELIANCE": {
        "StopLossATRMultiplier": 1.8,
        "TargetATRMultiplier": 3.0,
        "MaxTradesPerDay": 5
      }
    }
  }
}
```

### Example 4: Multiple Instruments with Overrides

```json
{
  "Instrument": {
    "ActiveInstrumentKey": "NSE:NIFTY",
    "TradingMode": "OPTIONS",
    "InstrumentOverrides": {
      "NSE:NIFTY": {
        "MaxTradesPerDay": 3,
        "MaxDailyLossAmount": 10000
      },
      "NSE:BANKNIFTY": {
        "StopLossATRMultiplier": 2.0,
        "MaxTradesPerDay": 2,
        "MaxDailyLossAmount": 15000
      },
      "NSE:RELIANCE": {
        "TradingMode": "EQUITY",
        "StopLossATRMultiplier": 1.8,
        "MaxTradesPerDay": 5
      },
      "NSE:TCS": {
        "TradingMode": "EQUITY",
        "StopLossATRMultiplier": 2.0,
        "MaxTradesPerDay": 4
      }
    }
  }
}
```

## Supported Instruments

### NSE Indices (Pre-configured)

| Instrument Key | Lot Size | Derivatives | Default Mode |
|---------------|----------|-------------|--------------|
| NSE:NIFTY | 50 | Yes | OPTIONS |
| NSE:BANKNIFTY | 25 | Yes | OPTIONS |

### Adding New Instruments

#### Step 1: Database Entry

```sql
INSERT INTO instruments
  (instrument_key, exchange, symbol, instrument_type, lot_size, tick_size, is_derivatives_enabled, default_trading_mode, is_active)
VALUES
  -- Example: Stock with derivatives
  ('NSE:RELIANCE', 'NSE', 'RELIANCE', 'STOCK', 1, 0.05, true, 'EQUITY', true),

  -- Example: Stock without derivatives
  ('NSE:WIPRO', 'NSE', 'WIPRO', 'STOCK', 1, 0.05, false, 'EQUITY', true),

  -- Example: New index
  ('NSE:FINNIFTY', 'NSE', 'FINNIFTY', 'INDEX', 40, 0.05, true, 'OPTIONS', true);
```

#### Step 2: Configure Risk Parameters

```json
{
  "InstrumentOverrides": {
    "NSE:RELIANCE": {
      "StopLossATRMultiplier": 1.8,
      "TargetATRMultiplier": 3.0,
      "MaxTradesPerDay": 5,
      "MaxDailyLossAmount": 8000
    }
  }
}
```

#### Step 3: Activate

```json
{
  "Instrument": {
    "ActiveInstrumentKey": "NSE:RELIANCE"
  }
}
```

## Strategy Behavior

### Same Algorithm, Different Instruments

The pullback strategy works identically across all instruments:
1. Identifies trend using EMA/ADX/MACD
2. Waits for pullback
3. Confirms entry with strong candle
4. Sets ATR-based stops and targets

### Instrument-Specific Adjustments

What changes per instrument:
- ATR values (volatility-based)
- Lot sizes
- Strike selection (for options)
- Risk amounts

What stays the same:
- Trend detection logic
- Pullback detection logic
- Entry confirmation rules

## Trading Modes

### OPTIONS Mode

- Trades ATM call/put options
- Uses weekly expiries by default
- Requires `IsDerivativesEnabled = true`
- Higher leverage, higher risk

**Best For:**
- Indices: NIFTY, BANKNIFTY
- High-volatility stocks
- Directional plays

### EQUITY Mode

- Trades underlying spot
- No expiry concerns
- Lower leverage
- Position sizing based on capital

**Best For:**
- Stocks without derivatives
- Lower volatility stocks
- Longer holding periods

## Risk Management

### Per-Instrument Risk

Each instrument can have custom risk parameters:

```json
{
  "NSE:NIFTY": {
    "StopLossATRMultiplier": 1.5,    // Tighter stops
    "TargetATRMultiplier": 2.0,
    "MaxTradesPerDay": 3,
    "MaxDailyLossAmount": 10000
  },
  "NSE:BANKNIFTY": {
    "StopLossATRMultiplier": 2.0,    // Wider stops (more volatile)
    "TargetATRMultiplier": 2.5,
    "MaxTradesPerDay": 2,             // Fewer trades
    "MaxDailyLossAmount": 15000       // Higher loss limit
  }
}
```

### Global Risk Defaults

If no override is specified, uses global config:

```json
{
  "Risk": {
    "StopLossATRMultiplier": 1.5,
    "TargetATRMultiplier": 2.0,
    "MaxDailyLossAmount": 10000,
    "MaxDailyLossPercent": 5.0
  }
}
```

## Switching Instruments

### Method 1: Configuration Change

1. Edit `appsettings.json`
2. Change `ActiveInstrumentKey`
3. Restart application

### Method 2: Multiple Configurations

Create separate config files:

```bash
appsettings.nifty.json
appsettings.banknifty.json
appsettings.reliance.json
```

Run with specific config:
```bash
dotnet run --configuration Nifty
```

## Data Requirements

### Historical Data

Each instrument needs:
- Minimum 200 candles of historical data
- Fetched from Upstox on startup
- Stored in `market_candles` table

### Live Data

- Real-time price updates from Upstox
- Candle building for active timeframe
- Indicator calculation on each candle

## Performance Considerations

### Indices vs Stocks

**Indices (NIFTY, BANKNIFTY):**
- Higher liquidity
- Tighter spreads
- Better for intraday
- More predictable volatility

**Stocks:**
- Wider spreads
- Company-specific news impact
- May need wider stops
- Better for swing trading

### Timeframe Selection

**5-minute:**
- More signals
- More noise
- Requires quick decisions
- Higher transaction costs

**15-minute:**
- Cleaner signals
- Less noise
- Better for trending moves
- Lower transaction costs

## Best Practices

### 1. Start with One Instrument
- Master NIFTY before adding others
- Understand its behavior
- Optimize parameters

### 2. Backtest Before Live
- Test on historical data
- Validate risk parameters
- Verify execution logic

### 3. Monitor Performance
- Track per-instrument P&L
- Identify best-performing setups
- Adjust parameters accordingly

### 4. Risk Diversification
- Don't over-allocate to one instrument
- Balance between indices and stocks
- Consider correlation

### 5. Market Conditions
- Some instruments trend better
- Others are more range-bound
- Adjust strategy accordingly

## Example Queries

### Check Active Instruments

```sql
SELECT instrument_key, symbol, instrument_type, default_trading_mode, is_active
FROM instruments
WHERE is_active = true;
```

### Performance by Instrument

```sql
SELECT
  instrument_key,
  COUNT(*) as trades,
  SUM(CASE WHEN pnl > 0 THEN 1 ELSE 0 END) as wins,
  SUM(CASE WHEN pnl < 0 THEN 1 ELSE 0 END) as losses,
  SUM(pnl) as total_pnl,
  AVG(pnl) as avg_pnl
FROM trades
WHERE entry_time >= CURRENT_DATE - INTERVAL '30 days'
GROUP BY instrument_key
ORDER BY total_pnl DESC;
```

### Recent Market Data

```sql
SELECT instrument_key, timestamp, close, volume
FROM market_candles
WHERE instrument_key = 'NSE:NIFTY'
  AND timeframe_minutes = 15
ORDER BY timestamp DESC
LIMIT 10;
```

## Troubleshooting

### Instrument Not Found
- Check database for instrument entry
- Verify `instrument_key` format
- Ensure `is_active = true`

### No Historical Data
- Verify Upstox token
- Check instrument key in Upstox
- Ensure market hours for data fetch

### Risk Parameters Not Applied
- Check override configuration
- Verify JSON syntax
- Restart application after config change

## Summary

The system is now truly multi-asset:
- Any NSE instrument supported
- Options or equity trading
- Per-instrument risk controls
- Same strategy, different markets
- Database-driven, not hardcoded
- Production-ready architecture
