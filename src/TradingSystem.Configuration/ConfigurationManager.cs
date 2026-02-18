using Microsoft.Extensions.Configuration;
using TradingSystem.Configuration.Models;

namespace TradingSystem.Configuration;

public class ConfigurationManager
{
    private readonly TradingConfig _config;
    private readonly IConfigurationRoot _configuration;

    public ConfigurationManager(string configFilePath = "appsettings.json")
    {
        _configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile(configFilePath, optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        _config = new TradingConfig();
        _configuration.GetSection("Trading").Bind(_config);

        ValidateConfiguration();
    }

    public TradingConfig GetConfig() => _config;

    private void ValidateConfiguration()
    {
        if (_config.Timeframe.ActiveTimeframeMinutes <= 0)
            throw new InvalidOperationException("ActiveTimeframeMinutes must be greater than 0");

        if (_config.Timeframe.BaseTimeframeMinutes <= 0)
            throw new InvalidOperationException("BaseTimeframeMinutes must be greater than 0");

        if (_config.Timeframe.ActiveTimeframeMinutes > _config.Timeframe.BaseTimeframeMinutes)
            throw new InvalidOperationException("ActiveTimeframeMinutes cannot be greater than BaseTimeframeMinutes");

        if (_config.Risk.StopLossATRMultiplier <= 0)
            throw new InvalidOperationException("StopLossATRMultiplier must be greater than 0");

        if (_config.Risk.TargetATRMultiplier <= _config.Risk.StopLossATRMultiplier)
            throw new InvalidOperationException("TargetATRMultiplier should be greater than StopLossATRMultiplier for positive risk-reward");

        if (_config.Limits.MaxTradesPerDay <= 0)
            throw new InvalidOperationException("MaxTradesPerDay must be greater than 0");

        if (string.IsNullOrWhiteSpace(_config.Instrument.ActiveInstrumentKey))
            throw new InvalidOperationException("ActiveInstrumentKey is required");
    }

    public void ReloadConfiguration()
    {
        _configuration.Reload();
        _configuration.GetSection("Trading").Bind(_config);
        ValidateConfiguration();
    }
}
