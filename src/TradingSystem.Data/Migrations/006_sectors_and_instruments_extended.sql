/*
  # Add Sectors Table and Extended Instrument Fields

  1. New Tables
    - `sectors`
      - `id` (serial, primary key)
      - `name` (text) - Sector name
      - `code` (text, unique) - Sector code
      - `description` (text) - Sector description
      - `is_active` (boolean) - Active status
      - `created_at` (timestamptz) - Creation timestamp
      - `updated_at` (timestamptz) - Update timestamp

  2. Changes to instruments table
    - Add `sector_id` (int, foreign key to sectors)
    - Add `industry` (text) - Industry classification
    - Add `market_cap` (decimal) - Market capitalization
    - Add `isin` (text) - ISIN code

  3. Security
    - Enable RLS on sectors table
    - Add policies for authenticated access

  4. Important Notes
    - Unique constraint on sector code
    - Foreign key relationship with instruments
    - Indexes for performance
*/

-- Create sectors table
CREATE TABLE IF NOT EXISTS sectors (
  id SERIAL PRIMARY KEY,
  name TEXT NOT NULL,
  code TEXT NOT NULL UNIQUE,
  description TEXT DEFAULT '',
  is_active BOOLEAN DEFAULT true,
  created_at TIMESTAMPTZ DEFAULT now(),
  updated_at TIMESTAMPTZ DEFAULT now()
);

-- Create indexes for sectors
CREATE INDEX IF NOT EXISTS idx_sectors_name ON sectors(name);
CREATE UNIQUE INDEX IF NOT EXISTS idx_sectors_code ON sectors(code);

-- Add new columns to instruments table
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'instruments' AND column_name = 'sector_id'
  ) THEN
    ALTER TABLE instruments ADD COLUMN sector_id INT;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'instruments' AND column_name = 'industry'
  ) THEN
    ALTER TABLE instruments ADD COLUMN industry TEXT DEFAULT '';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'instruments' AND column_name = 'market_cap'
  ) THEN
    ALTER TABLE instruments ADD COLUMN market_cap DECIMAL(18, 2);
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'instruments' AND column_name = 'isin'
  ) THEN
    ALTER TABLE instruments ADD COLUMN isin TEXT DEFAULT '';
  END IF;
END $$;

-- Add foreign key constraint
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM information_schema.table_constraints
    WHERE constraint_name = 'fk_instruments_sector'
    AND table_name = 'instruments'
  ) THEN
    ALTER TABLE instruments
      ADD CONSTRAINT fk_instruments_sector
      FOREIGN KEY (sector_id)
      REFERENCES sectors(id)
      ON DELETE SET NULL;
  END IF;
END $$;

-- Add index on sector_id
CREATE INDEX IF NOT EXISTS idx_instruments_sector_id ON instruments(sector_id);

-- Enable Row Level Security for sectors
ALTER TABLE sectors ENABLE ROW LEVEL SECURITY;

-- RLS Policies for sectors
DO $$ BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_policies
    WHERE tablename = 'sectors'
    AND policyname = 'Authenticated users can read sectors'
  ) THEN
    CREATE POLICY "Authenticated users can read sectors"
      ON sectors FOR SELECT
      TO authenticated
      USING (true);
  END IF;
END $$;

DO $$ BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_policies
    WHERE tablename = 'sectors'
    AND policyname = 'Service role can insert sectors'
  ) THEN
    CREATE POLICY "Service role can insert sectors"
      ON sectors FOR INSERT
      TO authenticated
      WITH CHECK (true);
  END IF;
END $$;

DO $$ BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_policies
    WHERE tablename = 'sectors'
    AND policyname = 'Service role can update sectors'
  ) THEN
    CREATE POLICY "Service role can update sectors"
      ON sectors FOR UPDATE
      TO authenticated
      USING (true)
      WITH CHECK (true);
  END IF;
END $$;

DO $$ BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_policies
    WHERE tablename = 'sectors'
    AND policyname = 'Service role can delete sectors'
  ) THEN
    CREATE POLICY "Service role can delete sectors"
      ON sectors FOR DELETE
      TO authenticated
      USING (true);
  END IF;
END $$;

-- Add trigger to update updated_at timestamp for sectors
CREATE OR REPLACE FUNCTION update_sectors_updated_at()
RETURNS TRIGGER AS $$
BEGIN
  NEW.updated_at = now();
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trigger_update_sectors_updated_at ON sectors;
CREATE TRIGGER trigger_update_sectors_updated_at
  BEFORE UPDATE ON sectors
  FOR EACH ROW
  EXECUTE FUNCTION update_sectors_updated_at();
