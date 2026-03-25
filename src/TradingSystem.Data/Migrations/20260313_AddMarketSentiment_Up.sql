-- =============================================
-- Migration: Create market_sentiments table
-- =============================================

CREATE TABLE IF NOT EXISTS market_sentiments (
    id BIGSERIAL PRIMARY KEY,

    timestamp TIMESTAMPTZ NOT NULL,

    sentiment VARCHAR(20) NOT NULL,

    sentiment_score NUMERIC(5,2) NOT NULL,

    volatility_index NUMERIC(5,2) NOT NULL,

    market_breadth NUMERIC(10,4) NOT NULL,

    index_performance JSONB NOT NULL,

    sector_performance JSONB NOT NULL,

    key_factors TEXT[] NOT NULL,

    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,

    -- Constraints
    CONSTRAINT chk_sentiment_score 
        CHECK (sentiment_score BETWEEN -100 AND 100),

    CONSTRAINT chk_volatility_index 
        CHECK (volatility_index BETWEEN 0 AND 100),

    CONSTRAINT chk_market_breadth 
        CHECK (market_breadth > 0),

    CONSTRAINT chk_sentiment_values 
        CHECK (sentiment IN ('BEARISH', 'NEUTRAL', 'BULLISH'))
);

-- =============================================
-- INDEXES
-- =============================================

-- Time-based queries (VERY IMPORTANT for trading apps)
CREATE INDEX IF NOT EXISTS idx_market_sentiments_timestamp 
ON market_sentiments(timestamp DESC);

-- Filtering by sentiment
CREATE INDEX IF NOT EXISTS idx_market_sentiments_sentiment 
ON market_sentiments(sentiment);

-- JSONB indexes (for fast querying inside JSON)
CREATE INDEX IF NOT EXISTS idx_market_sentiments_index_performance
ON market_sentiments USING GIN (index_performance);

CREATE INDEX IF NOT EXISTS idx_market_sentiments_sector_performance
ON market_sentiments USING GIN (sector_performance);

-- Sorting / recent data queries
CREATE INDEX IF NOT EXISTS idx_market_sentiments_created_at 
ON market_sentiments(created_at DESC);

-- =============================================
-- OPTIONAL (HIGH VALUE) INDEXES
-- =============================================

-- Latest sentiment lookup (very common query)
CREATE INDEX IF NOT EXISTS idx_market_sentiments_latest 
ON market_sentiments (timestamp DESC, sentiment);

-- =============================================
-- DOCUMENTATION
-- =============================================

COMMENT ON TABLE market_sentiments
IS 'Stores market sentiment analysis data with BULLISH/NEUTRAL/BEARISH indicators';

COMMENT ON COLUMN market_sentiments.sentiment_score 
IS 'Range: -100 (extremely bearish) to +100 (extremely bullish)';

COMMENT ON COLUMN market_sentiments.volatility_index 
IS 'India VIX proxy (0–100)';

COMMENT ON COLUMN market_sentiments.market_breadth 
IS 'Advance/Decline ratio';

COMMENT ON COLUMN market_sentiments.index_performance 
IS 'JSON: { NIFTY50, BANKNIFTY, FINNIFTY, etc }';

COMMENT ON COLUMN market_sentiments.sector_performance 
IS 'JSON: sector-wise performance';