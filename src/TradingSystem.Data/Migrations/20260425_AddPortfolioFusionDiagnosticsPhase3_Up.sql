-- =============================================
-- Migration: Add portfolio fusion diagnostics columns
-- Date: 2026-04-25
-- =============================================

ALTER TABLE IF EXISTS portfolio_manager_trades
    ADD COLUMN IF NOT EXISTS fusion_score NUMERIC(8,4),
    ADD COLUMN IF NOT EXISTS fusion_news_signal NUMERIC(8,4),
    ADD COLUMN IF NOT EXISTS fusion_technical_signal NUMERIC(8,4),
    ADD COLUMN IF NOT EXISTS fusion_sector_signal NUMERIC(8,4),
    ADD COLUMN IF NOT EXISTS fusion_direction_veto BOOLEAN,
    ADD COLUMN IF NOT EXISTS fusion_included BOOLEAN,
    ADD COLUMN IF NOT EXISTS fusion_evidence TEXT;

CREATE INDEX IF NOT EXISTS idx_portfolio_manager_trades_fusion_score
    ON portfolio_manager_trades(fusion_score DESC);
