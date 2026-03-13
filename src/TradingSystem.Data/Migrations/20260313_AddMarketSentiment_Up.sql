-- =============================================
-- Migration: Create market_sentiment table
-- Date: 2026-03-13
-- =============================================

-- Create table
CREATE TABLE IF NOT EXISTS market_sentiment (
    id BIGSERIAL PRIMARY KEY,
    timestamp TIMESTAMPTZ NOT NULL,
    sentiment VARCHAR(20) NOT NULL,
    sentiment_score DECIMAL(5,2) NOT NULL,
    volatility_index DECIMAL(5,2) NOT NULL,
    market_breadth DECIMAL(10,4) NOT NULL,
    index_performance JSONB NOT NULL,
    sector_performance JSONB NOT NULL,
    key_factors TEXT[] NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    CONSTRAINT chk_sentiment_score CHECK (sentiment_score BETWEEN -100 AND 100),
    CONSTRAINT chk_volatility_index CHECK (volatility_index BETWEEN 0 AND 100),
    CONSTRAINT chk_market_breadth CHECK (market_breadth > 0),
    CONSTRAINT chk_sentiment_values CHECK (sentiment IN ('BEARISH', 'NEUTRAL', 'BULLISH'))
);

-- Indexes
CREATE INDEX IF NOT EXISTS IX_MarketSentiment_Timestamp 
ON market_sentiment(timestamp);

CREATE INDEX IF NOT EXISTS IX_MarketSentiment_Sentiment 
ON market_sentiment(sentiment);

CREATE INDEX IF NOT EXISTS IX_MarketSentiment_CreatedAt 
ON market_sentiment(created_at);

-- Documentation
COMMENT ON TABLE market_sentiment 
IS 'Stores market sentiment analysis data with BULLISH/NEUTRAL/BEARISH indicators';

COMMENT ON COLUMN market_sentiment.sentiment_score 
IS 'Sentiment score ranging from -100 (extremely bearish) to +100 (extremely bullish)';

COMMENT ON COLUMN market_sentiment.volatility_index 
IS 'Volatility index (India VIX) ranging from 0 to 100';

COMMENT ON COLUMN market_sentiment.market_breadth 
IS 'Advance/Decline ratio indicating market breadth';

COMMENT ON COLUMN market_sentiment.index_performance 
IS 'JSON containing index performance data (NIFTY, BANKNIFTY, etc.)';

COMMENT ON COLUMN market_sentiment.sector_performance 
IS 'JSON containing sector-wise performance metrics';