-- =============================================
-- Migration Rollback: Remove portfolio fusion diagnostics columns
-- Date: 2026-04-25
-- =============================================

DROP INDEX IF EXISTS idx_portfolio_manager_trades_fusion_score;

ALTER TABLE IF EXISTS portfolio_manager_trades
    DROP COLUMN IF EXISTS fusion_evidence,
    DROP COLUMN IF EXISTS fusion_included,
    DROP COLUMN IF EXISTS fusion_direction_veto,
    DROP COLUMN IF EXISTS fusion_sector_signal,
    DROP COLUMN IF EXISTS fusion_technical_signal,
    DROP COLUMN IF EXISTS fusion_news_signal,
    DROP COLUMN IF EXISTS fusion_score;
