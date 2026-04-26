# Database Migrations

This directory contains all database migration scripts for the TradingSystem.

## Naming Convention

All migration files follow this naming pattern:
- **Up Script**: `YYYYMMDD_MigrationName_Up.sql`
- **Down Script**: `YYYYMMDD_MigrationName_Down.sql`

Where:
- `YYYYMMDD` is the migration creation date
- `MigrationName` describes what the migration does (PascalCase)
- `_Up` scripts apply changes
- `_Down` scripts rollback changes

## Available Migrations

### 20260313_AddMarketSentiment
**Date**: 2026-03-13  
**Description**: Creates the MarketSentiment table for storing market sentiment analysis data.

**Files**:
- `20260313_AddMarketSentiment_Up.sql`
- `20260313_AddMarketSentiment_Down.sql`

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
- Sentiment enum values: BEARISH, NEUTRAL, BULLISH

---

### 20260314_AddFeatureStore
**Date**: 2026-03-14  
**Description**: Creates the feature_store table for storing pre-computed ML features.

**Files**:
- `20260314_AddFeatureStore_Up.sql`
- `20260314_AddFeatureStore_Down.sql`

**Features**:
- Stores pre-computed machine learning feature vectors (120+ quantitative factors)
- JSON serialized feature dictionary
- Feature versioning support
- Instrument and symbol indexing

**Indexes**:
- `idx_feature_store_instrument_time` - Composite index for instrument + timestamp queries
- `idx_feature_store_symbol` - For symbol-based lookups
- `idx_feature_store_timestamp` - For time-based queries
- `idx_feature_store_version` - For version tracking

**Constraints**:
- Foreign key to instruments table with CASCADE delete

---

### 20260425_AddPortfolioLearning
**Date**: 2026-04-25  
**Description**: Creates portfolio learning tables for fusion tuning and historical performance tracking.

**Files**:
- `20260425_AddPortfolioLearning_Up.sql`
- `20260425_AddPortfolioLearning_Down.sql`

**Features**:
- `portfolio_performance_histories` for learning metrics snapshots per portfolio session
- `fusion_learning_configs` for fusion weight iteration audit trail
- Time-based and lookup indexes for efficient learning history retrieval

**Constraints**:
- Foreign key from `portfolio_performance_histories.session_id` to `portfolio_manager_sessions.id`

---

### 20260425_AddPortfolioManagerPhase1
**Date**: 2026-04-25  
**Description**: Adds user preference columns and Portfolio Manager Phase 1 tables.

**Files**:
- `20260425_AddPortfolioManagerPhase1_Up.sql`
- `20260425_AddPortfolioManagerPhase1_Down.sql`

---

### 20260425_AddNewsIngestionPhase2
**Date**: 2026-04-25  
**Description**: Adds news ingestion tables for articles, keywords, and impacts.

**Files**:
- `20260425_AddNewsIngestionPhase2_Up.sql`
- `20260425_AddNewsIngestionPhase2_Down.sql`

---

### 20260425_AddPortfolioFusionDiagnosticsPhase3
**Date**: 2026-04-25  
**Description**: Adds fusion diagnostics columns to `portfolio_manager_trades`.

**Files**:
- `20260425_AddPortfolioFusionDiagnosticsPhase3_Up.sql`
- `20260425_AddPortfolioFusionDiagnosticsPhase3_Down.sql`

---

## How to Apply Migrations

### Using psql (PostgreSQL)