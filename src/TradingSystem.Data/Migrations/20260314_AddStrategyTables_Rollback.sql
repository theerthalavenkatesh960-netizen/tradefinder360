-- =============================================
-- Rollback Migration: Remove Strategy Tables
-- Description: Drops strategy signal and performance tables
-- Author: System
-- Date: 2026-03-14
-- =============================================

-- Drop constraints
IF EXISTS (SELECT * FROM sys.check_constraints WHERE name = 'CK_strategy_performances_win_rate')
BEGIN
    ALTER TABLE strategy_performances DROP CONSTRAINT CK_strategy_performances_win_rate;
    PRINT 'Dropped constraint: CK_strategy_performances_win_rate';
END
GO

IF EXISTS (SELECT * FROM sys.check_constraints WHERE name = 'CK_strategy_signals_direction')
BEGIN
    ALTER TABLE strategy_signals DROP CONSTRAINT CK_strategy_signals_direction;
    PRINT 'Dropped constraint: CK_strategy_signals_direction';
END
GO

IF EXISTS (SELECT * FROM sys.check_constraints WHERE name = 'CK_strategy_signals_confidence')
BEGIN
    ALTER TABLE strategy_signals DROP CONSTRAINT CK_strategy_signals_confidence;
    PRINT 'Dropped constraint: CK_strategy_signals_confidence';
END
GO

IF EXISTS (SELECT * FROM sys.check_constraints WHERE name = 'CK_strategy_signals_score')
BEGIN
    ALTER TABLE strategy_signals DROP CONSTRAINT CK_strategy_signals_score;
    PRINT 'Dropped constraint: CK_strategy_signals_score';
END
GO

-- Drop indexes
IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_strategy_performances_winrate')
BEGIN
    DROP INDEX idx_strategy_performances_winrate ON strategy_performances;
    PRINT 'Dropped index: idx_strategy_performances_winrate';
END
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_strategy_performances_lookup')
BEGIN
    DROP INDEX idx_strategy_performances_lookup ON strategy_performances;
    PRINT 'Dropped index: idx_strategy_performances_lookup';
END
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_strategy_signals_acted')
BEGIN
    DROP INDEX idx_strategy_signals_acted ON strategy_signals;
    PRINT 'Dropped index: idx_strategy_signals_acted';
END
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_strategy_signals_timestamp')
BEGIN
    DROP INDEX idx_strategy_signals_timestamp ON strategy_signals;
    PRINT 'Dropped index: idx_strategy_signals_timestamp';
END
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_strategy_signals_valid_score')
BEGIN
    DROP INDEX idx_strategy_signals_valid_score ON strategy_signals;
    PRINT 'Dropped index: idx_strategy_signals_valid_score';
END
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_strategy_signals_lookup')
BEGIN
    DROP INDEX idx_strategy_signals_lookup ON strategy_signals;
    PRINT 'Dropped index: idx_strategy_signals_lookup';
END
GO

-- Drop tables
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'strategy_performances')
BEGIN
    DROP TABLE strategy_performances;
    PRINT 'Dropped table: strategy_performances';
END
GO

IF EXISTS (SELECT * FROM sys.tables WHERE name = 'strategy_signals')
BEGIN
    DROP TABLE strategy_signals;
    PRINT 'Dropped table: strategy_signals';
END
GO

PRINT 'Strategy tables and all related objects removed successfully.';
GO