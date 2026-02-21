/*
  # Add timeframe and updated_at columns to instrument_prices

  1. Changes
    - Add `timeframe` column (text, default '1D') to instrument_prices
    - Add `updated_at` column (timestamptz) to instrument_prices
    - Drop existing unique index on (instrument_id, timestamp) if any
    - Create unique index on (instrument_id, timeframe, timestamp)
    - Add index on (instrument_id, timeframe) for query performance
    - Add trigger to auto-update updated_at on row changes

  2. Important Notes
    - Existing rows will have timeframe set to '1D' by default
    - updated_at will default to now() for existing rows
    - No data loss - only additive changes
*/

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'instrument_prices' AND column_name = 'timeframe'
  ) THEN
    ALTER TABLE instrument_prices ADD COLUMN timeframe TEXT NOT NULL DEFAULT '1D';
  END IF;
END $$;

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'instrument_prices' AND column_name = 'updated_at'
  ) THEN
    ALTER TABLE instrument_prices ADD COLUMN updated_at TIMESTAMPTZ DEFAULT now();
  END IF;
END $$;

DROP INDEX IF EXISTS idx_instrument_prices_unique;

CREATE UNIQUE INDEX IF NOT EXISTS idx_instrument_prices_unique
  ON instrument_prices(instrument_id, timeframe, timestamp);

CREATE INDEX IF NOT EXISTS idx_instrument_prices_instrument_timeframe
  ON instrument_prices(instrument_id, timeframe);

CREATE OR REPLACE FUNCTION update_instrument_prices_updated_at()
RETURNS TRIGGER AS $$
BEGIN
  NEW.updated_at = now();
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trigger_update_instrument_prices_updated_at ON instrument_prices;
CREATE TRIGGER trigger_update_instrument_prices_updated_at
  BEFORE UPDATE ON instrument_prices
  FOR EACH ROW
  EXECUTE FUNCTION update_instrument_prices_updated_at();
