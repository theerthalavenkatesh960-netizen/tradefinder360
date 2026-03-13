-- =============================================
-- Migration: Add Strategy Signal and Performance Tables
-- Description: Creates tables for storing strategy signals and performance metrics
-- Author: System
-- Date: 2026-03-14
-- =============================================

-- Create StrategySignals table
CREATE TABLE strategy_signals (
    id INT IDENTITY(1,1) NOT NULL,
    instrument_id INT NOT NULL,
    strategy_type INT NOT NULL, -- 0=MOMENTUM, 1=BREAKOUT, 2=MEAN_REVERSION, 3=SWING_TRADING
    timestamp DATETIMEOFFSET NOT NULL,
    is_valid BIT NOT NULL,
    score INT NOT NULL,
    direction NVARCHAR(10) NOT NULL,
    entry_price DECIMAL(18,4) NOT NULL,
    stop_loss DECIMAL(18,4) NOT NULL,
    target DECIMAL(18,4) NOT NULL,
    confidence DECIMAL(18,2) NOT NULL,
    risk_reward_ratio DECIMAL(8,2) NOT NULL,
    signals_json NVARCHAR(MAX) NOT NULL,
    metrics_json NVARCHAR(MAX) NOT NULL,
    explanation NVARCHAR(MAX) NULL,
    market_sentiment_id INT NULL,
    was_acted_upon BIT NOT NULL DEFAULT 0,
    related_trade_id INT NULL,
    created_at DATETIMEOFFSET NOT NULL DEFAULT GETUTCDATE(),
    expires_at DATETIMEOFFSET NULL,
    CONSTRAINT PK_strategy_signals PRIMARY KEY CLUSTERED (id ASC),
    CONSTRAINT FK_strategy_signals_instruments FOREIGN KEY (instrument_id) REFERENCES instruments(id) ON DELETE CASCADE,
    CONSTRAINT FK_strategy_signals_market_sentiments FOREIGN KEY (market_sentiment_id) REFERENCES market_sentiments(id) ON DELETE SET NULL,
    CONSTRAINT FK_strategy_signals_trades FOREIGN KEY (related_trade_id) REFERENCES trades(id) ON DELETE SET NULL
);
GO

-- Create indexes
CREATE NONCLUSTERED INDEX idx_strategy_signals_lookup 
    ON strategy_signals(instrument_id, strategy_type, timestamp DESC);
GO

CREATE NONCLUSTERED INDEX idx_strategy_signals_valid_score 
    ON strategy_signals(strategy_type, is_valid, score DESC);
GO

CREATE NONCLUSTERED INDEX idx_strategy_signals_timestamp 
    ON strategy_signals(timestamp DESC);
GO

CREATE NONCLUSTERED INDEX idx_strategy_signals_acted 
    ON strategy_signals(was_acted_upon, expires_at);
GO

-- Create StrategyPerformances table
CREATE TABLE strategy_performances (
    id INT IDENTITY(1,1) NOT NULL,
    strategy_type INT NOT NULL,
    period_start DATETIMEOFFSET NOT NULL,
    period_end DATETIMEOFFSET NOT NULL,
    total_signals INT NOT NULL DEFAULT 0,
    valid_signals INT NOT NULL DEFAULT 0,
    signals_acted_upon INT NOT NULL DEFAULT 0,
    average_score DECIMAL(18,2) NOT NULL DEFAULT 0,
    average_confidence DECIMAL(18,2) NOT NULL DEFAULT 0,
    winning_trades INT NOT NULL DEFAULT 0,
    losing_trades INT NOT NULL DEFAULT 0,
    win_rate DECIMAL(18,2) NOT NULL DEFAULT 0,
    average_pnl DECIMAL(18,4) NOT NULL DEFAULT 0,
    total_pnl DECIMAL(18,4) NOT NULL DEFAULT 0,
    best_trade DECIMAL(18,4) NOT NULL DEFAULT 0,
    worst_trade DECIMAL(18,4) NOT NULL DEFAULT 0,
    average_risk_reward DECIMAL(8,2) NOT NULL DEFAULT 0,
    created_at DATETIMEOFFSET NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT PK_strategy_performances PRIMARY KEY CLUSTERED (id ASC)
);
GO

-- Create indexes
CREATE NONCLUSTERED INDEX idx_strategy_performances_lookup 
    ON strategy_performances(strategy_type, period_end DESC);
GO

CREATE NONCLUSTERED INDEX idx_strategy_performances_winrate 
    ON strategy_performances(win_rate DESC);
GO

-- Add constraints
ALTER TABLE strategy_signals
    ADD CONSTRAINT CK_strategy_signals_score CHECK (score >= 0 AND score <= 100);
GO

ALTER TABLE strategy_signals
    ADD CONSTRAINT CK_strategy_signals_confidence CHECK (confidence >= 0 AND confidence <= 100);
GO

ALTER TABLE strategy_signals
    ADD CONSTRAINT CK_strategy_signals_direction CHECK (direction IN ('BUY', 'SELL'));
GO

ALTER TABLE strategy_performances
    ADD CONSTRAINT CK_strategy_performances_win_rate CHECK (win_rate >= 0 AND win_rate <= 100);
GO

PRINT 'Strategy tables created successfully with indexes and constraints.';
GO