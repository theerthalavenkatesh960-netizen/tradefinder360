# Migration Guide: Supabase to PostgreSQL + EF Core

This guide explains the transformation from Supabase-based storage to a professional PostgreSQL + Entity Framework Core setup.

## What Changed

### 1. Database Layer
- **Before**: Supabase client with Postgrest
- **After**: PostgreSQL with Entity Framework Core 8.0
- **Benefits**:
  - Type-safe LINQ queries
  - Automatic migrations
  - Better performance
  - Full control over database operations

### 2. Data Access
- **Before**: Direct Supabase repository calls
- **After**: Repository pattern with clean interfaces
- **Benefits**:
  - Testable code
  - Separation of concerns
  - Easier to mock for testing

### 3. Configuration
- **Before**: Hardcoded `NIFTY` symbol
- **After**: Multi-instrument configuration
- **Benefits**:
  - Support for any NSE instrument
  - Per-instrument risk overrides
  - Flexible trading modes (OPTIONS/EQUITY)

## Database Setup

### Step 1: Install PostgreSQL
```bash
# macOS
brew install postgresql@15
brew services start postgresql@15

# Ubuntu/Debian
sudo apt-get install postgresql-15

# Windows
# Download from https://www.postgresql.org/download/windows/
```

### Step 2: Create Database
```bash
psql -U postgres
CREATE DATABASE trading;
\q
```

### Step 3: Run Migration Script
```bash
psql -U postgres -d trading -f src/TradingSystem.Data/Migrations/001_InitialSchema.sql
```

### Step 4: Update Connection String
Edit `src/TradingSystem.Engine/appsettings.json`:
```json
{
  "Trading": {
    "Database": {
      "ConnectionString": "Host=localhost;Database=trading;Username=postgres;Password=yourpassword"
    }
  }
}
```

Or set environment variable:
```bash
export DATABASE_URL="Host=localhost;Database=trading;Username=postgres;Password=yourpassword"
```

## Configuration Changes

### Old Configuration (Supabase)
```json
{
  "Execution": {
    "UnderlyingSymbol": "NIFTY",
    "DefaultLotSize": 50
  },
  "Database": {
    "SupabaseUrl": "...",
    "SupabaseKey": "..."
  }
}
```

### New Configuration (Multi-Asset)
```json
{
  "Instrument": {
    "ActiveInstrumentKey": "NSE:NIFTY",
    "TradingMode": "OPTIONS",
    "InstrumentOverrides": {
      "NSE:BANKNIFTY": {
        "MaxTradesPerDay": 2
      }
    }
  },
  "Database": {
    "ConnectionString": "Host=localhost;Database=trading;Username=postgres;Password=postgres"
  },
  "Upstox": {
    "AccessToken": "your_token_here"
  }
}
```

## Code Changes

### Old Way (Direct Supabase)
```csharp
var repo = new SupabaseRepository(config);
await repo.SaveTrade(trade);
```

### New Way (Repository Pattern)
```csharp
var dataService = serviceProvider.GetRequiredService<TradingDataService>();
await dataService.SaveTradeAsync(instrumentKey, trade);
```

## Adding New Instruments

### Step 1: Add to Database
```sql
INSERT INTO instruments (instrument_key, exchange, symbol, instrument_type, lot_size, is_derivatives_enabled, default_trading_mode)
VALUES ('NSE:RELIANCE', 'NSE', 'RELIANCE', 'STOCK', 1, true, 'EQUITY');
```

### Step 2: Configure in appsettings.json
```json
{
  "Instrument": {
    "ActiveInstrumentKey": "NSE:RELIANCE"
  }
}
```

### Step 3: Add Risk Overrides (Optional)
```json
{
  "InstrumentOverrides": {
    "NSE:RELIANCE": {
      "StopLossATRMultiplier": 2.0,
      "MaxTradesPerDay": 5
    }
  }
}
```

## Data Flow

### Old Flow
```
StrategyEngine → SupabaseRepository → Supabase Cloud
```

### New Flow
```
StrategyEngine → TradingDataService → Repository → EF Core → PostgreSQL
                                                    ↑
                                            UpstoxClient → Upstox API
```

## Testing the Migration

1. Verify database connection:
```bash
cd src/TradingSystem.Engine
dotnet run
```

2. Check for connection success message
3. Verify tables were created:
```sql
\dt  -- List all tables
SELECT * FROM instruments;
```

## Rollback (If Needed)

If you need to revert:
1. Keep the old Supabase configuration files
2. Restore `SupabaseRepository.cs`
3. Revert `TradingSystem.Data.csproj` NuGet packages
4. Update DI container in Program.cs

## Benefits Summary

1. **No Vendor Lock-in**: Use any PostgreSQL provider
2. **Better Performance**: Direct database access, no API overhead
3. **Type Safety**: Compile-time query validation
4. **Testability**: Easy to mock repositories
5. **Flexibility**: Support any instrument, any exchange
6. **Cost**: No Supabase subscription fees
7. **Control**: Full schema control, custom indexes
