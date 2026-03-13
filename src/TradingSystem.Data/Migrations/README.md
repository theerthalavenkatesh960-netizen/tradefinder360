
## Available Migrations

### 20260313_AddMarketSentiment
**Description**: Creates the MarketSentiment table for storing market sentiment analysis data.

**Features**:
- Stores market sentiment snapshots (BULLISH/NEUTRAL/BEARISH)
- Tracks sentiment score (-100 to +100)
- Stores volatility index (India VIX)
- Tracks market breadth (advance/decline ratio)
- Stores index performance data (JSON)
- Stores sector performance data (JSON)
- Includes key market factors

**Indexes**:
- `IX_MarketSentiment_Timestamp` - For efficient time-based queries
- `IX_MarketSentiment_Sentiment` - For filtering by sentiment type
- `IX_MarketSentiment_CreatedAt` - For historical analysis

**Constraints**:
- SentimentScore must be between -100 and +100
- VolatilityIndex must be between 0 and 100
- MarketBreadth must be positive
- Sentiment enum values: 0 (BEARISH), 1 (NEUTRAL), 2 (BULLISH)

## How to Apply Migrations

### Using SQL Server Management Studio (SSMS)
1. Open SSMS and connect to your database
2. Open the migration script
3. Execute the script

### Using sqlcmd