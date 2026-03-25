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

    -- Multi-period indicator columns
    rsi NUMERIC(8,4) NOT NULL DEFAULT 50,
    macd_histogram NUMERIC(18,4) NOT NULL DEFAULT 0,
    price_vs_20dma NUMERIC(8,4) NOT NULL DEFAULT 0,
    price_vs_50dma NUMERIC(8,4) NOT NULL DEFAULT 0,
    new_highs_52w INTEGER NOT NULL DEFAULT 0,
    new_lows_52w INTEGER NOT NULL DEFAULT 0,
    mclellan_oscillator NUMERIC(12,4) NOT NULL DEFAULT 0,
    vix_vs_20dma NUMERIC(8,4) NOT NULL DEFAULT 0,

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
        CHECK (market_breadth >= 0),

    CONSTRAINT chk_sentiment_values 
        CHECK (sentiment IN ('STRONGLY_BULLISH', 'BULLISH', 'NEUTRAL', 'BEARISH', 'STRONGLY_BEARISH')),

    CONSTRAINT chk_rsi
        CHECK (rsi BETWEEN 0 AND 100),

    CONSTRAINT chk_new_highs_52w
        CHECK (new_highs_52w >= 0),

    CONSTRAINT chk_new_lows_52w
        CHECK (new_lows_52w >= 0)
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
IS 'Stores market sentiment analysis data with multi-period technical indicators';

COMMENT ON COLUMN market_sentiments.sentiment_score 
IS 'Range: -100 (extremely bearish) to +100 (extremely bullish)';

COMMENT ON COLUMN market_sentiments.volatility_index 
IS 'India VIX proxy (0–100)';

COMMENT ON COLUMN market_sentiments.market_breadth 
IS 'Advance/Decline ratio';

COMMENT ON COLUMN market_sentiments.rsi 
IS 'Market-wide average RSI-14 (Wilder smoothing)';

COMMENT ON COLUMN market_sentiments.macd_histogram 
IS 'Average MACD histogram across major indices';

COMMENT ON COLUMN market_sentiments.price_vs_20dma 
IS '% above/below 20-day moving average (index average)';

COMMENT ON COLUMN market_sentiments.price_vs_50dma 
IS '% above/below 50-day moving average (index average)';

COMMENT ON COLUMN market_sentiments.new_highs_52w 
IS 'Count of stocks at or near 52-week highs today';

COMMENT ON COLUMN market_sentiments.new_lows_52w 
IS 'Count of stocks at or near 52-week lows today';

COMMENT ON COLUMN market_sentiments.mclellan_oscillator 
IS 'McClellan Oscillator: EMA(19) - EMA(39) of daily A/D difference';

COMMENT ON COLUMN market_sentiments.vix_vs_20dma 
IS 'VIX minus its 20-day SMA (positive = fear increasing)';

COMMENT ON COLUMN market_sentiments.index_performance 
IS 'JSON: { NIFTY50, BANKNIFTY, FINNIFTY, etc }';

COMMENT ON COLUMN market_sentiments.sector_performance 
IS 'JSON: sector-wise performance';