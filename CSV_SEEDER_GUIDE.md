# CSV Seeder Implementation Guide

## Overview

The CSV Seeder system allows you to populate the database with sectors and instruments from CSV files. This is a one-time operation controlled by a configuration flag.

## Features

- **One-Time Execution**: Job runs only once when enabled
- **Config-Based Control**: Enable/disable via `appsettings.json`
- **Bulk Operations**: Efficient bulk insert/update using repository pattern
- **Smart CSV Parsing**: Handles quoted fields and special characters
- **Sector Code Generation**: Automatically generates unique sector codes
- **ISIN Extraction**: Extracts ISIN from instrument keys
- **Comprehensive Logging**: Detailed logs for debugging

## Database Schema

### Sectors Table
```sql
CREATE TABLE sectors (
  id SERIAL PRIMARY KEY,
  name TEXT NOT NULL,
  code TEXT NOT NULL UNIQUE,
  description TEXT,
  is_active BOOLEAN DEFAULT true,
  created_at TIMESTAMPTZ DEFAULT now(),
  updated_at TIMESTAMPTZ DEFAULT now()
);
```

### Updated Instruments Table
```sql
ALTER TABLE instruments ADD COLUMN sector_id INT;
ALTER TABLE instruments ADD COLUMN industry TEXT;
ALTER TABLE instruments ADD COLUMN market_cap DECIMAL(18, 2);
ALTER TABLE instruments ADD COLUMN isin TEXT;
ALTER TABLE instruments ADD CONSTRAINT fk_instruments_sector
  FOREIGN KEY (sector_id) REFERENCES sectors(id) ON DELETE SET NULL;
```

## CSV File Format

### sectors.csv
```csv
Description,Sector
Aerospace & Defense,Aerospace & Defense
Sugar,Agricultural Food & other Products
Tea & Coffee,Agricultural Food & other Products
```

**Columns:**
- `Description`: Detailed description of the industry
- `Sector`: Sector name (used for grouping)

### stocks.csv
```csv
Symbol,Name,Exchange,instrument_key,industry,sector
ABB,ABB India Limited,2,BSE_EQ|INE117A01022,Heavy Electrical Equipment,Electrical Equipment
AEGISLOG,Aegis Logistics Ltd.,2,BSE_EQ|INE208C01025,Trading - Gas,Gas
```

**Columns:**
- `Symbol`: Stock symbol
- `Name`: Full company name
- `Exchange`: Exchange code (1=NSE, 2=BSE)
- `instrument_key`: Upstox instrument key (format: EXCHANGE|ISIN)
- `industry`: Industry classification
- `sector`: Sector name (must match sectors.csv)

## Configuration

### appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=trading;Username=postgres;Password=password",
    "QuartzDb": "Host=localhost;Database=trading;Username=postgres;Password=password"
  },
  "Seeder": {
    "EnableCsvSeeding": false
  }
}
```

**Important:**
- Set `EnableCsvSeeding` to `true` to run the seeder
- After successful execution, set it back to `false`
- Both connection strings should point to the same database

## Usage

### Step 1: Prepare CSV Files

Place your CSV files in the `Data` folder:
```
src/TradingSystem.WorkerService/
  Data/
    sectors.csv
    stocks.csv
```

### Step 2: Enable Seeding

Update `appsettings.json`:
```json
{
  "Seeder": {
    "EnableCsvSeeding": true
  }
}
```

### Step 3: Run the Worker Service

```bash
cd src/TradingSystem.WorkerService
dotnet run
```

### Step 4: Verify Execution

Check the logs:
```
[INFO] Starting CSV data seeding job at 2026-02-19 14:30:00
[INFO] Parsed 45 sectors from CSV
[INFO] Successfully seeded 45 sectors
[INFO] Parsed 523 instruments from CSV
[INFO] Successfully seeded 523 instruments
[INFO] CSV data seeding completed successfully. Total: 45 sectors, 523 instruments
```

### Step 5: Disable Seeding

Update `appsettings.json`:
```json
{
  "Seeder": {
    "EnableCsvSeeding": false
  }
}
```

## How It Works

### Sector Code Generation

The system automatically generates unique sector codes:

- **Single Word**: Takes first 10 characters
  - "Banking" → "BANKING"

- **Multiple Words**: Takes first 3 letters of each word
  - "Information Technology" → "INF_TEC"
  - "Pharmaceuticals & Biotechnology" → "PHA_BIO"

### ISIN Extraction

Extracts ISIN from instrument_key:
- Input: `BSE_EQ|INE117A01022`
- Extracted ISIN: `INE117A01022`

### Upsert Logic

**Sectors:**
- Match by `code`
- Update: `name`, `description`, `is_active`, `updated_at`
- Insert: New sectors with all fields

**Instruments:**
- Match by `instrument_key`
- Update: All fields except `id`, `created_at`
- Insert: New instruments with all fields

## Architecture Components

### CsvSeedService
Main service for parsing and seeding data:
- `SeedSectorsFromCsvAsync()` - Seeds sectors
- `SeedInstrumentsFromCsvAsync()` - Seeds instruments
- `ParseCsvLine()` - Handles quoted CSV fields
- `GenerateSectorCode()` - Creates unique codes
- `ExtractISIN()` - Extracts ISIN from keys

### CsvDataSeederJob
Quartz job for scheduled execution:
- Runs once when enabled
- Reads CSV files from Data folder
- Logs all operations
- Handles errors gracefully

### Repository Pattern
Uses specialized repositories:
- `ISectorRepository` - Sector operations
- `IInstrumentRepository` - Instrument operations
- Both support bulk upsert for performance

## Error Handling

The system handles:
- Missing CSV files
- Malformed CSV lines
- Duplicate sectors
- Invalid instrument data
- Database errors

All errors are logged with context.

## Performance

- **Bulk Operations**: Uses `BulkUpsertAsync` for efficiency
- **Single Query**: Loads existing data once
- **In-Memory Matching**: Fast duplicate detection
- **Batch Processing**: Processes all records in batches

## Best Practices

1. **Backup First**: Backup database before seeding
2. **Test Data**: Verify CSV format with small dataset
3. **Monitor Logs**: Watch logs during execution
4. **Disable After Use**: Set `EnableCsvSeeding=false` after success
5. **Version Control**: Keep CSV files in version control
6. **Update Carefully**: When re-running, existing records are updated

## Troubleshooting

### Job Not Running
- Check `EnableCsvSeeding` is `true`
- Verify Quartz is configured correctly
- Check logs for startup errors

### CSV Not Found
- Verify files in `Data` folder
- Check file names: `sectors.csv`, `stocks.csv`
- Ensure files are copied to output directory

### No Records Inserted
- Check CSV format matches expected structure
- Look for parsing errors in logs
- Verify column order matches specification

### Sector Not Matched
- Check sector names match exactly
- Verify case sensitivity
- Review sector code generation logs

## Integration with Worker Service

The seeder integrates seamlessly:

```
Startup
  ↓
Load Configuration (EnableCsvSeeding)
  ↓
Register Quartz Jobs
  ↓
If EnableCsvSeeding == true:
  - Register CsvDataSeederJob
  - Set trigger to run immediately once
  ↓
Run Worker Service
  ↓
Execute CsvDataSeederJob
  ↓
Seed Sectors → Seed Instruments
  ↓
Log Results
  ↓
Continue with other scheduled jobs
```

## Migration Location

Migration `006_sectors_and_instruments_extended.sql` is located at:
- `src/TradingSystem.Data/Migrations/006_sectors_and_instruments_extended.sql`

This migration has been applied to Supabase with:
- sectors table creation
- Extended instruments columns
- Foreign key relationships
- Indexes for performance
- RLS policies for security
- Update triggers

## Next Steps

After successful seeding:
1. Verify data in database
2. Test instrument queries
3. Run DailyPriceUpdateJob to fetch prices
4. Monitor scheduled jobs
5. Build analytics/reports using sector data

## Example Queries

### Get All Sectors
```sql
SELECT * FROM sectors WHERE is_active = true ORDER BY name;
```

### Get Instruments by Sector
```sql
SELECT i.symbol, i.name, s.name as sector
FROM instruments i
JOIN sectors s ON i.sector_id = s.id
WHERE s.code = 'BANKING';
```

### Get Sector Distribution
```sql
SELECT s.name, COUNT(i.id) as instrument_count
FROM sectors s
LEFT JOIN instruments i ON s.id = i.sector_id
GROUP BY s.id, s.name
ORDER BY instrument_count DESC;
```
