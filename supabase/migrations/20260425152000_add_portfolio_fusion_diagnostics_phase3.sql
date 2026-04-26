-- Phase 3: Persist fusion diagnostics per portfolio trade
ALTER TABLE IF EXISTS portfolio_manager_trades
    ADD COLUMN IF NOT EXISTS fusion_score numeric(8,4),
    ADD COLUMN IF NOT EXISTS fusion_news_signal numeric(8,4),
    ADD COLUMN IF NOT EXISTS fusion_technical_signal numeric(8,4),
    ADD COLUMN IF NOT EXISTS fusion_sector_signal numeric(8,4),
    ADD COLUMN IF NOT EXISTS fusion_direction_veto boolean,
    ADD COLUMN IF NOT EXISTS fusion_included boolean,
    ADD COLUMN IF NOT EXISTS fusion_evidence text;

CREATE INDEX IF NOT EXISTS idx_portfolio_manager_trades_fusion_score
    ON portfolio_manager_trades (fusion_score DESC);
