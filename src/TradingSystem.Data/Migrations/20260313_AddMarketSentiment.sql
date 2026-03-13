-- =============================================
-- Migration: Add MarketSentiment Table
-- Description: Creates table to store market sentiment analysis data
-- Author: System
-- Date: 2026-03-13
-- =============================================

-- Create MarketSentiment table
CREATE TABLE MarketSentiment (
    Id INT IDENTITY(1,1) NOT NULL,
    Timestamp DATETIMEOFFSET NOT NULL,
    Sentiment INT NOT NULL, -- 0=BEARISH, 1=NEUTRAL, 2=BULLISH
    SentimentScore DECIMAL(18,2) NOT NULL,
    VolatilityIndex DECIMAL(18,2) NOT NULL,
    MarketBreadth DECIMAL(18,4) NOT NULL,
    IndexPerformance NVARCHAR(MAX) NOT NULL, -- JSON data
    SectorPerformance NVARCHAR(MAX) NOT NULL, -- JSON data
    KeyFactors NVARCHAR(MAX) NULL, -- JSON array
    CreatedAt DATETIMEOFFSET NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT PK_MarketSentiment PRIMARY KEY CLUSTERED (Id ASC)
);
GO

-- Create index on Timestamp for efficient querying
CREATE NONCLUSTERED INDEX IX_MarketSentiment_Timestamp 
    ON MarketSentiment(Timestamp DESC);
GO

-- Create index on Sentiment for filtering by sentiment type
CREATE NONCLUSTERED INDEX IX_MarketSentiment_Sentiment 
    ON MarketSentiment(Sentiment);
GO

-- Create index on CreatedAt for historical queries
CREATE NONCLUSTERED INDEX IX_MarketSentiment_CreatedAt 
    ON MarketSentiment(CreatedAt DESC);
GO

-- Add check constraint for SentimentScore range
ALTER TABLE MarketSentiment
    ADD CONSTRAINT CK_MarketSentiment_SentimentScore 
    CHECK (SentimentScore >= -100 AND SentimentScore <= 100);
GO

-- Add check constraint for VolatilityIndex (reasonable VIX range)
ALTER TABLE MarketSentiment
    ADD CONSTRAINT CK_MarketSentiment_VolatilityIndex 
    CHECK (VolatilityIndex >= 0 AND VolatilityIndex <= 100);
GO

-- Add check constraint for MarketBreadth (should be positive)
ALTER TABLE MarketSentiment
    ADD CONSTRAINT CK_MarketSentiment_MarketBreadth 
    CHECK (MarketBreadth >= 0);
GO

-- Add check constraint for Sentiment enum values
ALTER TABLE MarketSentiment
    ADD CONSTRAINT CK_MarketSentiment_Sentiment 
    CHECK (Sentiment IN (0, 1, 2)); -- BEARISH=0, NEUTRAL=1, BULLISH=2
GO

PRINT 'MarketSentiment table created successfully with indexes and constraints.';
GO