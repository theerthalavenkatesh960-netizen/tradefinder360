-- =============================================
-- Undo Migration: Remove MarketSentiment Table
-- Description: Drops MarketSentiment table and related objects
-- =============================================

DROP TABLE IF EXISTS market_sentiments CASCADE;