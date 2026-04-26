-- =============================================
-- Migration Rollback: Remove portfolio manager phase 1 schema
-- Date: 2026-04-25
-- =============================================

DROP INDEX IF EXISTS idx_portfolio_manager_trades_created_at;
DROP INDEX IF EXISTS idx_portfolio_manager_trades_instrument;
DROP INDEX IF EXISTS idx_portfolio_manager_trades_session_status;

DROP INDEX IF EXISTS idx_portfolio_manager_sessions_updated_at;
DROP INDEX IF EXISTS idx_portfolio_manager_sessions_user_status;
DROP INDEX IF EXISTS idx_portfolio_manager_sessions_user_id;

DROP TABLE IF EXISTS portfolio_manager_trades;
DROP TABLE IF EXISTS portfolio_manager_sessions;

DO $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'user_profiles' AND column_name = 'auto_rebalance_enabled'
  ) THEN
    ALTER TABLE user_profiles DROP COLUMN auto_rebalance_enabled;
  END IF;
END $$;

DO $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'user_profiles' AND column_name = 'preferred_themes'
  ) THEN
    ALTER TABLE user_profiles DROP COLUMN preferred_themes;
  END IF;
END $$;

DO $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'user_profiles' AND column_name = 'preferred_sectors'
  ) THEN
    ALTER TABLE user_profiles DROP COLUMN preferred_sectors;
  END IF;
END $$;

DO $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'user_profiles' AND column_name = 'preferred_risk_profile'
  ) THEN
    ALTER TABLE user_profiles DROP COLUMN preferred_risk_profile;
  END IF;
END $$;

DO $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'user_profiles' AND column_name = 'preferred_budget'
  ) THEN
    ALTER TABLE user_profiles DROP COLUMN preferred_budget;
  END IF;
END $$;
