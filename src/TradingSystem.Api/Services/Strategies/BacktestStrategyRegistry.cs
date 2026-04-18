namespace TradingSystem.Api.Services.Strategies;

public sealed class BacktestStrategyRegistry
{
    private readonly Dictionary<string, IBacktestStrategy> _strategies;

    public BacktestStrategyRegistry(IEnumerable<IBacktestStrategy> strategies)
    {
        _strategies = strategies
            .GroupBy(s => s.StrategyName.Trim().ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> StrategyNames => _strategies.Keys.ToList().AsReadOnly();

    public bool TryGet(string strategyName, out IBacktestStrategy? strategy)
    {
        strategy = null;
        if (string.IsNullOrWhiteSpace(strategyName))
            return false;

        return _strategies.TryGetValue(strategyName.Trim().ToUpperInvariant(), out strategy);
    }

    public IBacktestStrategy GetRequired(string strategyName)
    {
        if (!TryGet(strategyName, out var strategy) || strategy == null)
            throw new ArgumentException($"Unknown strategy: {strategyName}");

        return strategy;
    }
}
