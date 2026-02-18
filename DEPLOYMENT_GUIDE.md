# Deployment Guide: Multi-Asset Trading System

## System Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Trading System                            │
├─────────────────────────────────────────────────────────────┤
│                                                               │
│  ┌──────────────┐     ┌──────────────┐     ┌─────────────┐ │
│  │ Upstox API   │────▶│ Market Data  │────▶│  Candles    │ │
│  │   Client     │     │   Engine     │     │  Store      │ │
│  └──────────────┘     └──────────────┘     └─────────────┘ │
│                              │                      │         │
│                              ▼                      ▼         │
│                       ┌──────────────┐     ┌─────────────┐  │
│                       │  Indicator   │────▶│ PostgreSQL  │  │
│                       │   Engine     │     │  Database   │  │
│                       └──────────────┘     └─────────────┘  │
│                              │                               │
│                              ▼                               │
│                       ┌──────────────┐                       │
│                       │   Market     │                       │
│                       │    State     │                       │
│                       └──────────────┘                       │
│                              │                               │
│                              ▼                               │
│                       ┌──────────────┐                       │
│                       │  Strategy    │                       │
│                       │   Engine     │                       │
│                       └──────────────┘                       │
│                              │                               │
│                              ▼                               │
│                       ┌──────────────┐                       │
│                       │  Execution   │                       │
│                       │   Engine     │                       │
│                       └──────────────┘                       │
│                                                               │
└─────────────────────────────────────────────────────────────┘
```

## Prerequisites

1. **.NET 8.0 SDK**
   ```bash
   dotnet --version  # Should be 8.0.x
   ```

2. **PostgreSQL 15+**
   ```bash
   psql --version
   ```

3. **Upstox Account**
   - Active trading account
   - API access enabled
   - Access token generated

## Initial Setup

### 1. Clone and Build

```bash
git clone <repository-url>
cd TradingSystem
dotnet build TradingSystem.sln
```

### 2. Database Setup

```bash
# Create database
createdb trading

# Run migration
psql -d trading -f src/TradingSystem.Data/Migrations/001_InitialSchema.sql

# Verify
psql -d trading -c "SELECT * FROM instruments;"
```

### 3. Configuration

Create or update `src/TradingSystem.Engine/appsettings.json`:

```json
{
  "Trading": {
    "Instrument": {
      "ActiveInstrumentKey": "NSE:NIFTY",
      "TradingMode": "OPTIONS"
    },
    "Database": {
      "ConnectionString": "Host=localhost;Database=trading;Username=postgres;Password=yourpassword"
    },
    "Upstox": {
      "AccessToken": "YOUR_ACCESS_TOKEN_HERE"
    }
  }
}
```

### 4. Upstox API Setup

1. Log in to [Upstox Developer Console](https://api.upstox.com/)
2. Create a new app
3. Get your API credentials
4. Generate an access token
5. Update `appsettings.json` with your token

## Running the System

### Development Mode

```bash
cd src/TradingSystem.Engine
dotnet run
```

### Production Mode

```bash
cd src/TradingSystem.Engine
dotnet publish -c Release -o /opt/trading
cd /opt/trading
./TradingSystem.Engine
```

## Configuration Options

### Instrument Selection

Switch between instruments by changing `ActiveInstrumentKey`:

```json
{
  "Instrument": {
    "ActiveInstrumentKey": "NSE:BANKNIFTY"  // or "NSE:RELIANCE", etc.
  }
}
```

### Timeframe Switching

Change from 15-min to 5-min without code changes:

```json
{
  "Timeframe": {
    "ActiveTimeframeMinutes": 5,
    "BaseTimeframeMinutes": 15
  }
}
```

### Risk Parameters

Customize per instrument:

```json
{
  "InstrumentOverrides": {
    "NSE:NIFTY": {
      "StopLossATRMultiplier": 1.5,
      "TargetATRMultiplier": 2.0,
      "MaxTradesPerDay": 3
    },
    "NSE:BANKNIFTY": {
      "StopLossATRMultiplier": 2.0,
      "MaxTradesPerDay": 2,
      "MaxDailyLossAmount": 15000
    }
  }
}
```

## Adding New Instruments

### 1. Add to Database

```sql
INSERT INTO instruments
  (instrument_key, exchange, symbol, instrument_type, lot_size, tick_size, is_derivatives_enabled, default_trading_mode)
VALUES
  ('NSE:TCS', 'NSE', 'TCS', 'STOCK', 1, 0.05, true, 'EQUITY');
```

### 2. Configure Risk Parameters

```json
{
  "InstrumentOverrides": {
    "NSE:TCS": {
      "StopLossATRMultiplier": 1.8,
      "MaxTradesPerDay": 4
    }
  }
}
```

### 3. Switch Active Instrument

```json
{
  "Instrument": {
    "ActiveInstrumentKey": "NSE:TCS",
    "TradingMode": "EQUITY"
  }
}
```

## Monitoring

### Database Queries

```sql
-- Today's trades
SELECT * FROM trades
WHERE entry_time >= CURRENT_DATE
ORDER BY entry_time DESC;

-- Recent market data
SELECT * FROM market_candles
WHERE instrument_key = 'NSE:NIFTY'
ORDER BY timestamp DESC
LIMIT 10;

-- Latest indicators
SELECT * FROM indicator_snapshots
WHERE instrument_key = 'NSE:NIFTY'
ORDER BY timestamp DESC
LIMIT 1;

-- Performance summary
SELECT
  instrument_key,
  COUNT(*) as total_trades,
  SUM(pnl) as total_pnl,
  AVG(pnl) as avg_pnl
FROM trades
WHERE entry_time >= CURRENT_DATE - INTERVAL '7 days'
GROUP BY instrument_key;
```

### Logs

The system uses structured logging. Check console output for:
- Market data updates
- Trade entries/exits
- Risk violations
- System errors

## Security Considerations

1. **API Credentials**: Never commit `appsettings.json` with real tokens
2. **Database**: Use strong passwords and restrict access
3. **Network**: Run on secure, private network
4. **Backups**: Regular database backups recommended

## Production Checklist

- [ ] PostgreSQL configured and secured
- [ ] Database migration completed
- [ ] Upstox API credentials configured
- [ ] Trading hours configured correctly
- [ ] Risk parameters reviewed and approved
- [ ] Test with paper trading first
- [ ] Monitor first few trades closely
- [ ] Set up database backups
- [ ] Configure logging/alerting
- [ ] Document incident response procedures

## Troubleshooting

### Database Connection Failed
```bash
# Check PostgreSQL is running
pg_isready

# Verify credentials
psql -U postgres -d trading

# Check connection string in appsettings.json
```

### No Market Data
```bash
# Verify Upstox token is valid
# Check token expiry
# Ensure market hours
# Verify instrument key format
```

### Build Errors
```bash
# Clean and rebuild
dotnet clean
dotnet restore
dotnet build
```

## System Requirements

### Minimum
- CPU: 2 cores
- RAM: 2GB
- Storage: 10GB
- Network: Stable internet connection

### Recommended
- CPU: 4+ cores
- RAM: 4GB+
- Storage: 50GB SSD
- Network: Low-latency connection (<50ms to Upstox servers)

## Backup Strategy

### Database Backups
```bash
# Daily backup
pg_dump trading > backup_$(date +%Y%m%d).sql

# Restore
psql trading < backup_20240101.sql
```

### Configuration Backups
```bash
# Backup config
cp appsettings.json appsettings.json.backup

# Version control recommended
git add appsettings.json.template
```

## Support

For issues or questions:
1. Check logs for error messages
2. Verify configuration settings
3. Review database state
4. Check Upstox API status
5. Consult documentation
