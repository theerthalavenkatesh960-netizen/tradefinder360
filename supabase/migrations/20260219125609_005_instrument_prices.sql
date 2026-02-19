/*
  # Add Instrument Prices Table and Clean Up

  1. Changes
    - Add `name` column to instruments table
    - Create `instrument_prices` table for storing daily/historical price data
    - Drop unused tables: stocks, stock_prices, stock_metrics, sectors, tokens
    - Add indexes for performance optimization

  2. New Table: instrument_prices
    - `id` (bigserial, primary key)
    - `instrument_id` (int, foreign key to instruments)
    - `timestamp` (timestamptz) - Price data timestamp
    - `open` (decimal) - Opening price
    - `high` (decimal) - High price
    - `low` (decimal) - Low price
    - `close` (decimal) - Close price
    - `volume` (bigint) - Trading volume
    - `timeframe` (text) - Timeframe (1D, 1H, etc)
    - `created_at` (timestamptz) - Record creation time
    - `updated_at` (timestamptz) - Last update time

  3. Security
    - Enable RLS on instrument_prices table
    - Add policies for authenticated access

  4. Important Notes
    - Unique constraint on (instrument_id, timeframe, timestamp)
    - Cascading delete when instrument is removed
    - Indexes for fast price queries
*/

-- Add name column to instruments if not exists
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'instruments' AND column_name = 'name'
  ) THEN
    ALTER TABLE instruments ADD COLUMN name TEXT NOT NULL DEFAULT '';
  END IF;
END $$;

-- Drop old stock-related tables if they exist
DROP TABLE IF EXISTS stock_metrics CASCADE;
DROP TABLE IF EXISTS stock_prices CASCADE;
DROP TABLE IF EXISTS stocks CASCADE;
DROP TABLE IF EXISTS sectors CASCADE;
DROP TABLE IF EXISTS tokens CASCADE;

-- Create instrument_prices table
CREATE TABLE IF NOT EXISTS instrument_prices (
  id BIGSERIAL PRIMARY KEY,
  instrument_id INT NOT NULL,
  timestamp TIMESTAMPTZ NOT NULL,
  open DECIMAL(18, 4) NOT NULL,
  high DECIMAL(18, 4) NOT NULL,
  low DECIMAL(18, 4) NOT NULL,
  close DECIMAL(18, 4) NOT NULL,
  volume BIGINT NOT NULL DEFAULT 0,
  timeframe TEXT NOT NULL DEFAULT '1D',
  created_at TIMESTAMPTZ DEFAULT now(),
  updated_at TIMESTAMPTZ DEFAULT now(),
  CONSTRAINT fk_instrument_prices_instrument
    FOREIGN KEY (instrument_id)
    REFERENCES instruments(id)
    ON DELETE CASCADE
);

-- Create indexes for instrument_prices
CREATE UNIQUE INDEX IF NOT EXISTS idx_instrument_prices_unique
  ON instrument_prices(instrument_id, timeframe, timestamp);

CREATE INDEX IF NOT EXISTS idx_instrument_prices_timestamp
  ON instrument_prices(timestamp);

CREATE INDEX IF NOT EXISTS idx_instrument_prices_instrument_timeframe
  ON instrument_prices(instrument_id, timeframe);

-- Add index on instruments.symbol if not exists
CREATE INDEX IF NOT EXISTS idx_instruments_symbol
  ON instruments(symbol);

-- Enable Row Level Security
ALTER TABLE instrument_prices ENABLE ROW LEVEL SECURITY;

-- RLS Policies for instrument_prices
DO $$ BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_policies
    WHERE tablename = 'instrument_prices'
    AND policyname = 'Authenticated users can read instrument prices'
  ) THEN
    CREATE POLICY "Authenticated users can read instrument prices"
      ON instrument_prices FOR SELECT
      TO authenticated
      USING (true);
  END IF;
END $$;

DO $$ BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_policies
    WHERE tablename = 'instrument_prices'
    AND policyname = 'Service role can insert instrument prices'
  ) THEN
    CREATE POLICY "Service role can insert instrument prices"
      ON instrument_prices FOR INSERT
      TO authenticated
      WITH CHECK (true);
  END IF;
END $$;

DO $$ BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_policies
    WHERE tablename = 'instrument_prices'
    AND policyname = 'Service role can update instrument prices'
  ) THEN
    CREATE POLICY "Service role can update instrument prices"
      ON instrument_prices FOR UPDATE
      TO authenticated
      USING (true)
      WITH CHECK (true);
  END IF;
END $$;

DO $$ BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_policies
    WHERE tablename = 'instrument_prices'
    AND policyname = 'Service role can delete instrument prices'
  ) THEN
    CREATE POLICY "Service role can delete instrument prices"
      ON instrument_prices FOR DELETE
      TO authenticated
      USING (true);
  END IF;
END $$;

-- Add trigger to update updated_at timestamp
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
