# Build Instructions

This is a **.NET Core 8.0** project. The build process uses the .NET SDK, not Node.js/npm.

## Prerequisites

- **.NET 8.0 SDK** or higher
- Download from: https://dotnet.microsoft.com/download/dotnet/8.0

Verify installation:
```bash
dotnet --version
```

## Quick Build

### Linux/macOS
```bash
./build.sh
```

### Windows
```cmd
build.bat
```

## Manual Build Steps

### 1. Clean Previous Builds
```bash
dotnet clean TradingSystem.sln --configuration Release
```

### 2. Restore NuGet Packages
```bash
dotnet restore TradingSystem.sln
```

### 3. Build Solution
```bash
dotnet build TradingSystem.sln --configuration Release
```

### 4. Run the Trading Engine
```bash
cd src/TradingSystem.Engine
dotnet run --configuration Release
```

## Build Output

Binaries will be placed in:
```
src/TradingSystem.Engine/bin/Release/net8.0/
```

## Troubleshooting

### Error: "The SDK 'Microsoft.NET.Sdk' specified could not be found"
**Solution**: Install .NET 8.0 SDK

### Error: "Package restore failed"
**Solution**: Check internet connection and NuGet sources:
```bash
dotnet nuget list source
```

### Error: "Project file is incomplete"
**Solution**: Ensure all `.csproj` files are present in each project directory

## Project Structure

```
TradingSystem/
├── TradingSystem.sln                    # Solution file
├── src/
│   ├── TradingSystem.Core/
│   │   └── TradingSystem.Core.csproj
│   ├── TradingSystem.Configuration/
│   │   └── TradingSystem.Configuration.csproj
│   ├── TradingSystem.MarketData/
│   │   └── TradingSystem.MarketData.csproj
│   ├── TradingSystem.Indicators/
│   │   └── TradingSystem.Indicators.csproj
│   ├── TradingSystem.MarketState/
│   │   └── TradingSystem.MarketState.csproj
│   ├── TradingSystem.Strategy/
│   │   └── TradingSystem.Strategy.csproj
│   ├── TradingSystem.Risk/
│   │   └── TradingSystem.Risk.csproj
│   ├── TradingSystem.Execution/
│   │   └── TradingSystem.Execution.csproj
│   ├── TradingSystem.Data/
│   │   └── TradingSystem.Data.csproj
│   ├── TradingSystem.Logging/
│   │   └── TradingSystem.Logging.csproj
│   └── TradingSystem.Engine/
│       └── TradingSystem.Engine.csproj   # Main executable
└── build.sh / build.bat                  # Build scripts
```

## Build Configurations

### Debug (default)
```bash
dotnet build TradingSystem.sln --configuration Debug
```
- Includes debug symbols
- No optimizations
- Useful for development

### Release
```bash
dotnet build TradingSystem.sln --configuration Release
```
- Optimized code
- No debug symbols
- Recommended for production

## Continuous Integration

### GitHub Actions Example

```yaml
name: Build

on: [push, pull_request]

jobs:
  build:
    runs-on: ubuntu-latest

    steps:
    - uses: actions/checkout@v3

    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: 8.0.x

    - name: Restore dependencies
      run: dotnet restore

    - name: Build
      run: dotnet build --configuration Release --no-restore

    - name: Test
      run: dotnet test --configuration Release --no-build
```

## Publishing for Deployment

### Self-Contained Deployment
```bash
dotnet publish src/TradingSystem.Engine/TradingSystem.Engine.csproj \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained true \
  --output ./publish
```

### Framework-Dependent Deployment
```bash
dotnet publish src/TradingSystem.Engine/TradingSystem.Engine.csproj \
  --configuration Release \
  --output ./publish
```

## Build Performance

Typical build times:
- **Clean build**: 15-30 seconds
- **Incremental build**: 2-5 seconds
- **Full rebuild**: 20-40 seconds

Build output size:
- **Debug**: ~15 MB
- **Release**: ~8 MB
- **Self-contained**: ~70 MB (includes runtime)

## Dependencies

The project uses these NuGet packages:

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.Extensions.Configuration | 8.0.0 | Configuration management |
| Microsoft.Extensions.Configuration.Json | 8.0.0 | JSON config support |
| Serilog | 3.1.1 | Logging framework |
| Serilog.Sinks.Console | 5.0.1 | Console logging |
| Serilog.Sinks.File | 5.0.0 | File logging |
| Supabase | 1.0.3 | Database client |
| Postgrest | 4.0.2 | PostgreSQL REST API |

All dependencies are automatically restored during build.

## Verification

After building, verify the system:

```bash
# Check if executable exists
ls -lh src/TradingSystem.Engine/bin/Release/net8.0/TradingSystem.Engine.dll

# Run the system
cd src/TradingSystem.Engine
dotnet run --configuration Release

# Should see output like:
# === TRADING SYSTEM STARTED ===
# Professional Intraday Options Trading Algorithm
# ...
```

## Notes

- This is **NOT a Node.js/npm project**
- Do not run `npm install` or `npm run build`
- All React/TypeScript files have been removed
- This is a pure .NET Core solution
- Build requires .NET SDK, not Node.js

## Support

For build issues:
1. Ensure .NET 8.0 SDK is installed
2. Check all `.csproj` files are present
3. Try `dotnet clean` then rebuild
4. Check for NuGet package restore errors
5. Verify internet connectivity for package downloads
