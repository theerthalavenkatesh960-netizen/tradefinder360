# Trading System - Complete Setup Guide

## Step-by-Step Setup

### 1. Prerequisites

Ensure you have the following installed:

```bash
# Check .NET SDK version (must be 8.0 or higher)
dotnet --version

# If not installed, download from:
# https://dotnet.microsoft.com/download/dotnet/8.0
```

### 2. Initial Setup

```bash
# Navigate to the project directory
cd TradingSystem

# Restore all NuGet packages
dotnet restore

# Build the entire solution
dotnet build --configuration Release
```

### 3. Supabase Database Setup

#### Step 3.1: Create Supabase Project

1. Go to [https://supabase.com](https://supabase.com)
2. Sign up or log in
3. Click "New Project"
4. Fill in project details:
   - Project Name: `TradingSystem`
   - Database Password: (choose a strong password)
   - Region: (select closest to you)
5. Wait for project initialization (~2 minutes)

#### Step 3.2: Run Database Schema

1. In Supabase Dashboard, go to "SQL Editor"
2. Click "New Query"
3. Copy entire contents of `src/TradingSystem.Data/DatabaseSchema.sql`
4. Paste into SQL Editor
5. Click "Run" to create tables

#### Step 3.3: Get API Credentials

1. Go to Project Settings → API
2. Copy these values:
   - **Project URL** (e.g., `https://xxxxx.supabase.co`)
   - **anon public** key

### 4. Configure the Application

Edit `src/TradingSystem.Engine/appsettings.json`:

```json
{
  "Trading": {
    "Timeframe": {
      "ActiveTimeframeMinutes": 15,
      "BaseTimeframeMinutes": 15,
      "MaxCandleHistory": 200
    },
    "Indicators": {
      "BaseEmaFastLength": 20,
      "BaseEmaSlowLength": 50,
      "BaseRsiLength": 14,
      "BaseMacdFast": 12,
      "BaseMacdSlow": 26,
      "BaseMacdSignal": 9,
      "BaseAdxLength": 14,
      "BaseAtrLength": 14,
      "BaseBollingerLength": 20,
      "BollingerStdDev": 2.0
    },
    "Risk": {
      "StopLossATRMultiplier": 1.5,
      "TargetATRMultiplier": 2.0,
      "MaxDailyLossAmount": 10000,
      "MaxDailyLossPercent": 5,
      "CooldownMinutesAfterLoss": 30,
      "MaxPositionSizePercent": 20
    },
    "Limits": {
      "MaxTradesPerDay": 3,
      "MaxConsecutiveLosses": 2,
      "TradingStartTime": "09:30:00",
      "TradingEndTime": "15:15:00",
      "NoNewTradesAfter": "14:30:00"
    },
    "MarketState": {
      "SidewaysAdxThreshold": 20,
      "TrendingAdxThreshold": 25,
      "BullishRsiThreshold": 55,
      "BearishRsiThreshold": 45,
      "SidewaysRsiLower": 40,
      "SidewaysRsiUpper": 60,
      "BollingerNarrowThreshold": 0.02,
      "MinCandlesForTrend": 3
    },
    "Execution": {
      "UnderlyingSymbol": "NIFTY",
      "DefaultLotSize": 50,
      "MaxSlippagePercent": 0.5,
      "OrderTimeoutSeconds": 30,
      "UseWeeklyOptions": true
    },
    "Database": {
      "SupabaseUrl": "PASTE_YOUR_SUPABASE_URL_HERE",
      "SupabaseKey": "PASTE_YOUR_SUPABASE_ANON_KEY_HERE",
      "EnablePersistence": true
    }
  }
}
```

### 5. Run the System

```bash
# Navigate to engine directory
cd src/TradingSystem.Engine

# Run the application
dotnet run
```

You should see output like:

```
=== TRADING SYSTEM STARTED ===
Professional Intraday Options Trading Algorithm
===============================================

Engine initialized successfully!
Waiting for market data...

Starting market data simulation...

[09:15] Price: 22045.32 | State: WAIT | Market: SIDEWAYS
[09:30] Price: 22067.89 | State: WAIT | Market: TRENDING_BULLISH
...
```

### 6. Verify Installation

Check that:
- ✅ Application starts without errors
- ✅ Console shows candle data
- ✅ Logs are created in `logs/` directory
- ✅ Data appears in Supabase tables (`candles`, `trades`, `market_states`)

## Switching to 5-Minute Timeframe

### Option 1: Modify Main Config

Edit `appsettings.json`:

```json
{
  "Trading": {
    "Timeframe": {
      "ActiveTimeframeMinutes": 5,
      "BaseTimeframeMinutes": 15
    },
    "Limits": {
      "MaxTradesPerDay": 2
    }
  }
}
```

### Option 2: Use Separate Config File

```bash
# Copy the 5-minute preset
cp appsettings.5min.json appsettings.json

# Or run with specific config
dotnet run --configuration appsettings.5min.json
```

## Broker Integration

### Example: Integrating with Zerodha Kite

1. Create `ZerodhaAdapter.cs`:

```csharp
using KiteConnect;
using TradingSystem.Execution.Interfaces;

public class ZerodhaAdapter : IBrokerAdapter
{
    private readonly Kite _kite;

    public ZerodhaAdapter(string apiKey, string accessToken)
    {
        _kite = new Kite(apiKey, AccessToken: accessToken);
    }

    public async Task<List<Option>> GetOptionChain(string underlying, DateTime? expiry = null)
    {
        var instruments = await _kite.GetInstruments("NFO");
        // Filter and map to Option objects
        // ...
    }

    public async Task<string> PlaceOrder(Option option, int quantity, string orderType)
    {
        var orderId = await _kite.PlaceOrder(
            Exchange: "NFO",
            TradingSymbol: option.Symbol,
            TransactionType: "BUY",
            Quantity: quantity,
            OrderType: orderType
        );
        return orderId;
    }

    // Implement other methods...
}
```

2. Update `TradingEngine.cs`:

```csharp
// Replace this line:
IBrokerAdapter broker = new MockBrokerAdapter();

// With:
IBrokerAdapter broker = new ZerodhaAdapter(apiKey, accessToken);
```

## Troubleshooting

### Error: "SupabaseUrl is required"

**Solution**: Make sure you've updated `appsettings.json` with your actual Supabase credentials.

### Error: "Unable to connect to Supabase"

**Solution**:
1. Check your internet connection
2. Verify Supabase URL and key are correct
3. Ensure Supabase project is active

### Error: "Table 'trades' does not exist"

**Solution**: Run the database schema SQL file in Supabase SQL Editor.

### No trades being placed in simulation

**Reason**: This is normal! The system is conservative:
- Market must be trending (not sideways)
- Must have valid pullback setup
- All risk checks must pass

To test more actively, adjust thresholds in `appsettings.json`:
```json
{
  "MarketState": {
    "TrendingAdxThreshold": 20  // Lower from 25
  }
}
```

### Logs not appearing

**Solution**: Ensure write permissions for `logs/` directory:
```bash
mkdir -p logs
chmod 755 logs
```

## Testing Checklist

Before going live:

- [ ] System builds without errors
- [ ] Database schema created successfully
- [ ] Configuration file populated with correct values
- [ ] Simulation runs and shows market states
- [ ] Logs are being written
- [ ] Data persists to Supabase
- [ ] Indicator calculations verified (compare with TradingView)
- [ ] Risk limits respected (max trades, max loss)
- [ ] Broker adapter tested with paper trading account

## Next Steps

1. **Paper Trading**: Connect to broker's paper trading API
2. **Backtest**: Load historical data and analyze performance
3. **Monitor**: Watch logs and database for a few days
4. **Optimize**: Adjust parameters based on results
5. **Go Live**: Start with minimal position size

## Support

For issues:
1. Check logs in `logs/` directory
2. Review Supabase dashboard for data issues
3. Verify all configuration parameters
4. Test each module independently

---

**Important**: This is a real trading system. Always test thoroughly before risking real capital.
