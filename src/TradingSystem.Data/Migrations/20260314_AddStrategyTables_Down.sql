-- =============================================
-- Undo Migration: Remove Strategy Signal and Performance Tables
-- =============================================

DROP TABLE IF EXISTS strategy_performances CASCADE;

DROP TABLE IF EXISTS strategy_signals CASCADE;