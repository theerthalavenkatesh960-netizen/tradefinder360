# Trading System Architecture v2.0

## Overview

This document describes the refactored architecture using the Repository Pattern with clean separation of concerns.

## Architecture Layers

### 1. Core Layer (`TradingSystem.Core`)
Contains domain models and business entities:
- `TradingInstrument` - Stock/Index instruments
- `InstrumentPrice` - Historical and daily price data
- `MarketCandle` - Real-time candle data
- `Trade`, `TradeRecord` - Trade execution records
- `Recommendation` - Trade recommendations
- `ScanSnapshot` - Market scanner results

### 2. Data Layer (`TradingSystem.Data`)

#### Repository Pattern Implementation

**ICommonRepository<T>** - Generic repository interface with standard CRUD operations:
```csharp
- GetByIdAsync(id)
- FirstOrDefaultAsync(predicate)
- GetAllAsync()
- FindAsync(predicate)
- AddAsync(entity)
- AddRangeAsync(entities)
- UpdateAsync(entity)
- UpdateRangeAsync(entities)
- DeleteAsync(entity)
- DeleteRangeAsync(entities)
- CountAsync(predicate)
- ExistsAsync(predicate)
- Query() - Returns IQueryable for complex queries
```

**CommonRepository<T>** - Base repository implementation using EF Core.

**Specialized Repositories**:
- `IInstrumentRepository` / `InstrumentRepository`
  - GetBySymbolAsync()
  - GetByInstrumentKeyAsync()
  - GetActiveInstrumentsAsync()
  - GetByExchangeAsync()
  - BulkUpsertAsync() - Efficient bulk insert/update

- `IInstrumentPriceRepository` / `InstrumentPriceRepository`
  - GetByInstrumentIdAsync()
  - GetLatestPriceAsync()
  - GetLatestPricesForInstrumentsAsync()
  - BulkUpsertAsync() - Efficient price data management

#### Services Layer
Higher-level business logic services built on top of repositories:
- `InstrumentService` - Instrument management
- `CandleService` - Candle data operations
- `IndicatorService` - Technical indicators
- `TradeService` - Trade management
- `ScanService` - Scanner operations
- `RecommendationService` - Recommendation management

### 3. Integration Layer (`TradingSystem.Upstox`)

#### Upstox Services
- `IUpstoxInstrumentService` / `UpstoxInstrumentService`
  - FetchInstrumentsAsync(exchange)
  - FetchAllEquityInstrumentsAsync()

- `IUpstoxPriceService` / `UpstoxPriceService`
  - FetchHistoricalPricesAsync()
  - FetchBulkHistoricalPricesAsync() - Batch processing with rate limiting

- `UpstoxClient` - HTTP client with rate limiting and retry logic

### 4. Worker Service (`TradingSystem.WorkerService`)

Scheduled background jobs using Quartz.NET:

**DailyPriceUpdateJob**
- Schedule: Daily at 6:30 PM (after market close)
- Fetches historical prices for all active instruments
- Batch processing: 50 instruments per batch
- Upserts price data (handles duplicates gracefully)

**InstrumentSyncJob**
- Schedule: Daily at 2:00 AM
- Syncs instruments from Upstox API
- Updates instrument metadata
- Handles bulk upsert operations

### 5. API Layer (`TradingSystem.Api`)

RESTful API endpoints:
- `/api/instrument` - Instrument CRUD operations
- `/api/radar` - Market scanner/radar
- `/api/recommendations` - Trade recommendations

## Database Schema

### instruments
Primary table for stocks/indices:
```sql
- id (PK)
- instrument_key (unique)
- exchange (NSE/BSE)
- symbol
- name
- instrument_type (STOCK/INDEX)
- lot_size
- tick_size
- is_derivatives_enabled
- default_trading_mode
- is_active
- created_at
- updated_at
```

### instrument_prices
Historical price data:
```sql
- id (PK)
- instrument_id (FK -> instruments)
- timestamp
- open, high, low, close
- volume
- timeframe (1D, 1H, 5m, etc)
- created_at
- updated_at

UNIQUE INDEX: (instrument_id, timeframe, timestamp)
```

## Benefits of Repository Pattern

1. **Separation of Concerns**: Data access logic isolated from business logic
2. **Testability**: Easy to mock repositories for unit testing
3. **Maintainability**: Centralized data access code
4. **Flexibility**: Easy to switch data sources or ORMs
5. **Reusability**: Common operations inherited from base repository
6. **Performance**: Optimized bulk operations
7. **Type Safety**: Strong typing with generic constraints

## Data Flow

### Instrument & Price Sync Flow
```
Upstox API
    ↓
UpstoxInstrumentService/UpstoxPriceService
    ↓
WorkerService Jobs (Scheduled)
    ↓
Repository Layer (BulkUpsert)
    ↓
Database (PostgreSQL/Supabase)
```

### API Request Flow
```
HTTP Request
    ↓
API Controller
    ↓
Service Layer
    ↓
Repository Layer
    ↓
Database
    ↓
Response
```

## Configuration

### appsettings.json
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "..."
  },
  "Upstox": {
    "ApiKey": "...",
    "ApiSecret": "...",
    "AccessToken": "...",
    "BaseUrl": "https://api.upstox.com/v2",
    "MaxRetries": 3,
    "RetryDelayMs": 1000,
    "RateLimitPerSecond": 10
  }
}
```

## Running the Worker Service

```bash
cd TradingSystem.WorkerService
dotnet run
```

The service will:
1. Start Quartz scheduler
2. Register scheduled jobs
3. Execute jobs at configured times
4. Log all operations

## Deployment

1. **API**: Deploy to web server (IIS, Kestrel, Docker)
2. **Worker**: Deploy as Windows Service or Linux daemon
3. **Database**: Supabase (PostgreSQL with RLS)

## Best Practices

1. Always use repositories for data access
2. Keep business logic in service layer
3. Use async/await for all I/O operations
4. Implement proper logging
5. Handle cancellation tokens
6. Use bulk operations for large datasets
7. Implement proper error handling
8. Follow SOLID principles
9. Use dependency injection
10. Keep migrations in single location (Data/Migrations)

## Migration Management

All database migrations are stored in:
- `src/TradingSystem.Data/Migrations/` (source)
- `supabase/migrations/` (deployment)

Use numbered prefixes (001_, 002_, etc.) for ordering.

## Future Enhancements

1. Add caching layer (Redis)
2. Implement event sourcing
3. Add message queue (RabbitMQ/Kafka)
4. Implement circuit breaker pattern
5. Add comprehensive monitoring
6. Implement GraphQL API
7. Add real-time WebSocket support
