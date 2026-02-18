@echo off
REM Trading System Build Script for Windows

echo ========================================
echo Trading System - Build Script
echo ========================================
echo.

REM Check .NET SDK
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK is not installed
    echo Please install .NET 8.0 SDK from:
    echo https://dotnet.microsoft.com/download/dotnet/8.0
    exit /b 1
)

for /f "tokens=*" %%i in ('dotnet --version') do set DOTNET_VERSION=%%i
echo [OK] .NET SDK found: %DOTNET_VERSION%
echo.

REM Clean previous builds
echo Cleaning previous builds...
dotnet clean TradingSystem.sln --configuration Release
echo [OK] Clean complete
echo.

REM Restore NuGet packages
echo Restoring NuGet packages...
dotnet restore TradingSystem.sln
echo [OK] Restore complete
echo.

REM Build solution
echo Building solution...
dotnet build TradingSystem.sln --configuration Release --no-restore
if errorlevel 1 (
    echo.
    echo ========================================
    echo Build FAILED!
    echo ========================================
    exit /b 1
)
echo [OK] Build complete
echo.

REM Run tests (if test project exists)
if exist "tests\" (
    echo Running tests...
    dotnet test TradingSystem.sln --configuration Release --no-build
    echo [OK] Tests complete
    echo.
)

echo ========================================
echo Build succeeded!
echo ========================================
echo.
echo To run the trading engine:
echo   cd src\TradingSystem.Engine
echo   dotnet run --configuration Release
