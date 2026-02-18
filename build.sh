#!/bin/bash

# Trading System Build Script
# This script builds the entire .NET Core solution

set -e

echo "========================================"
echo "Trading System - Build Script"
echo "========================================"
echo ""

# Check .NET SDK
if ! command -v dotnet &> /dev/null
then
    echo "ERROR: .NET SDK is not installed"
    echo "Please install .NET 8.0 SDK from:"
    echo "https://dotnet.microsoft.com/download/dotnet/8.0"
    exit 1
fi

echo "✓ .NET SDK found: $(dotnet --version)"
echo ""

# Clean previous builds
echo "Cleaning previous builds..."
dotnet clean TradingSystem.sln --configuration Release
echo "✓ Clean complete"
echo ""

# Restore NuGet packages
echo "Restoring NuGet packages..."
dotnet restore TradingSystem.sln
echo "✓ Restore complete"
echo ""

# Build solution
echo "Building solution..."
dotnet build TradingSystem.sln --configuration Release --no-restore
echo "✓ Build complete"
echo ""

# Run tests (if test project exists)
if [ -d "tests" ]; then
    echo "Running tests..."
    dotnet test TradingSystem.sln --configuration Release --no-build
    echo "✓ Tests complete"
    echo ""
fi

echo "========================================"
echo "Build succeeded!"
echo "========================================"
echo ""
echo "To run the trading engine:"
echo "  cd src/TradingSystem.Engine"
echo "  dotnet run --configuration Release"
