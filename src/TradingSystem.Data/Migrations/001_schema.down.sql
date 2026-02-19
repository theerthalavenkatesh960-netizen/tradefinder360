/*
  # Trading System — Full Schema (DOWN / REVERT)

  Drops all tables and indexes created by 001_schema.up.sql.

  WARNING: This is destructive. All trading data will be permanently deleted.
  Run only in development or when explicitly resetting the database.

  Order matters — drop dependent tables before referenced ones.
*/

DROP TABLE IF EXISTS recommendations;
DROP TABLE IF EXISTS scan_snapshots;
DROP TABLE IF EXISTS trades;
DROP TABLE IF EXISTS indicator_snapshots;
DROP TABLE IF EXISTS market_candles;
DROP TABLE IF EXISTS instruments;
