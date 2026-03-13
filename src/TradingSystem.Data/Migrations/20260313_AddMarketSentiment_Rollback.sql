-- =============================================
-- Rollback Migration: Remove MarketSentiment Table
-- Description: Drops MarketSentiment table and all related objects
-- Author: System
-- Date: 2026-03-13
-- =============================================

-- Drop check constraints
IF EXISTS (SELECT * FROM sys.check_constraints WHERE name = 'CK_MarketSentiment_Sentiment')
BEGIN
    ALTER TABLE MarketSentiment DROP CONSTRAINT CK_MarketSentiment_Sentiment;
    PRINT 'Dropped constraint: CK_MarketSentiment_Sentiment';
END
GO

IF EXISTS (SELECT * FROM sys.check_constraints WHERE name = 'CK_MarketSentiment_MarketBreadth')
BEGIN
    ALTER TABLE MarketSentiment DROP CONSTRAINT CK_MarketSentiment_MarketBreadth;
    PRINT 'Dropped constraint: CK_MarketSentiment_MarketBreadth';
END
GO

IF EXISTS (SELECT * FROM sys.check_constraints WHERE name = 'CK_MarketSentiment_VolatilityIndex')
BEGIN
    ALTER TABLE MarketSentiment DROP CONSTRAINT CK_MarketSentiment_VolatilityIndex;
    PRINT 'Dropped constraint: CK_MarketSentiment_VolatilityIndex';
END
GO

IF EXISTS (SELECT * FROM sys.check_constraints WHERE name = 'CK_MarketSentiment_SentimentScore')
BEGIN
    ALTER TABLE MarketSentiment DROP CONSTRAINT CK_MarketSentiment_SentimentScore;
    PRINT 'Dropped constraint: CK_MarketSentiment_SentimentScore';
END
GO

-- Drop indexes
IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MarketSentiment_CreatedAt')
BEGIN
    DROP INDEX IX_MarketSentiment_CreatedAt ON MarketSentiment;
    PRINT 'Dropped index: IX_MarketSentiment_CreatedAt';
END
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MarketSentiment_Sentiment')
BEGIN
    DROP INDEX IX_MarketSentiment_Sentiment ON MarketSentiment;
    PRINT 'Dropped index: IX_MarketSentiment_Sentiment';
END
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MarketSentiment_Timestamp')
BEGIN
    DROP INDEX IX_MarketSentiment_Timestamp ON MarketSentiment;
    PRINT 'Dropped index: IX_MarketSentiment_Timestamp';
END
GO

-- Drop table
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'MarketSentiment')
BEGIN
    DROP TABLE MarketSentiment;
    PRINT 'Dropped table: MarketSentiment';
END
GO

PRINT 'MarketSentiment table and all related objects removed successfully.';
GO