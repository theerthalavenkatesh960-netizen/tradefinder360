-- =============================================
-- Migration Rollback: Drop market_sentiment table
-- Date: 2026-03-13
-- =============================================

-- Drop indexes first
DROP INDEX IF EXISTS IX_MarketSentiment_CreatedAt;
DROP INDEX IF EXISTS IX_MarketSentiment_Sentiment;
DROP INDEX IF EXISTS IX_MarketSentiment_Timestamp;

-- Drop table
DROP TABLE IF EXISTS market_sentiment;