using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TradingSystem.AI.Services;
using TradingSystem.Api.DTOs;
using TradingSystem.Core.Models;
using TradingSystem.Data;
using TradingSystem.Scanner.Services;

namespace TradingSystem.Api.Services;

public class PortfolioManagerService : IPortfolioManagerService
{
    private readonly TradingDbContext _dbContext;
    private readonly PortfolioOptimizationService _portfolioOptimizationService;
    private readonly SectorIntelligenceService _sectorIntelligenceService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public PortfolioManagerService(
        TradingDbContext dbContext,
        PortfolioOptimizationService portfolioOptimizationService,
        SectorIntelligenceService sectorIntelligenceService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _dbContext = dbContext;
        _portfolioOptimizationService = portfolioOptimizationService;
        _sectorIntelligenceService = sectorIntelligenceService;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<PortfolioSessionSummaryDto> CreateSessionAsync(
        string userId,
        CreatePortfolioSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Budget <= 0)
        {
            throw new ArgumentException("Budget must be greater than 0.");
        }

        var normalizedRiskProfile = NormalizeRiskProfile(request.RiskProfile);
        var normalizedSectors = NormalizeList(request.PreferredSectors);
        var normalizedThemes = NormalizeList(request.PreferredThemes);

        var existingProfile = await _dbContext.UserProfiles
            .AsTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        if (existingProfile != null)
        {
            existingProfile.PreferredBudget = request.Budget;
            existingProfile.PreferredRiskProfile = normalizedRiskProfile;
            existingProfile.PreferredSectors = normalizedSectors;
            existingProfile.PreferredThemes = normalizedThemes;
            existingProfile.AutoRebalanceEnabled = request.AutoRebalanceEnabled;
            existingProfile.UpdatedOn = DateTime.UtcNow;
        }
        else
        {
            await _dbContext.UserProfiles.AddAsync(new UserProfile
            {
                UserId = userId,
                PreferredBudget = request.Budget,
                PreferredRiskProfile = normalizedRiskProfile,
                PreferredSectors = normalizedSectors,
                PreferredThemes = normalizedThemes,
                AutoRebalanceEnabled = request.AutoRebalanceEnabled,
                CreatedOn = DateTime.UtcNow,
                UpdatedOn = DateTime.UtcNow
            }, cancellationToken);
        }

        var session = new PortfolioManagerSession
        {
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        ApplySessionInputs(
            session,
            string.IsNullOrWhiteSpace(request.SessionName) ? "My Portfolio" : request.SessionName.Trim(),
            request.Budget,
            normalizedRiskProfile,
            normalizedSectors,
            normalizedThemes,
            request.AutoRebalanceEnabled,
            request.MaxPositions,
            request.TimeframeMinutes,
            request.MinConfidence);

        await _dbContext.PortfolioManagerSessions.AddAsync(session, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await BuildSessionSummaryAsync(session, cancellationToken);
    }

    public async Task<PortfolioSessionSummaryDto?> UpdateSessionAsync(
        string userId,
        long sessionId,
        UpdatePortfolioSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Budget <= 0)
        {
            throw new ArgumentException("Budget must be greater than 0.");
        }

        var session = await _dbContext.PortfolioManagerSessions
            .AsTracking()
            .FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, cancellationToken);

        if (session == null)
        {
            return null;
        }

        var normalizedRiskProfile = NormalizeRiskProfile(request.RiskProfile);
        var normalizedSectors = NormalizeList(request.PreferredSectors);
        var normalizedThemes = NormalizeList(request.PreferredThemes);

        ApplySessionInputs(
            session,
            string.IsNullOrWhiteSpace(request.SessionName) ? session.SessionName : request.SessionName.Trim(),
            request.Budget,
            normalizedRiskProfile,
            normalizedSectors,
            normalizedThemes,
            request.AutoRebalanceEnabled,
            request.MaxPositions,
            request.TimeframeMinutes,
            request.MinConfidence);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await BuildSessionSummaryAsync(session, cancellationToken);
    }

    public async Task<PortfolioSessionSummaryDto?> CloneSessionAsync(
        string userId,
        long sourceSessionId,
        ClonePortfolioSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var sourceSession = await _dbContext.PortfolioManagerSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sourceSessionId && x.UserId == userId, cancellationToken);

        if (sourceSession == null)
        {
            return null;
        }

        var cloneName = string.IsNullOrWhiteSpace(request.SessionName)
            ? $"{sourceSession.SessionName} (Copy)"
            : request.SessionName.Trim();

        var cloned = new PortfolioManagerSession
        {
            UserId = userId,
            LastProvider = string.Empty,
            LastModel = string.Empty,
            LastRunAt = null,
            NextRunAt = null,
            TotalRuns = 0,
            AllocatedCapital = 0,
            RealizedPnl = 0,
            UnrealizedPnl = 0,
            WinRatePercent = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        ApplySessionInputs(
            cloned,
            cloneName,
            sourceSession.InitialCapital,
            sourceSession.RiskProfile,
            sourceSession.PreferredSectors,
            sourceSession.PreferredThemes,
            false,
            sourceSession.MaxPositions,
            sourceSession.TimeframeMinutes,
            sourceSession.MinConfidence);

        await _dbContext.PortfolioManagerSessions.AddAsync(cloned, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await BuildSessionSummaryAsync(cloned, cancellationToken);
    }

    public async Task<bool> DeleteSessionAsync(
        string userId,
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.PortfolioManagerSessions
            .AsTracking()
            .FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, cancellationToken);

        if (session == null)
        {
            return false;
        }

        var trades = await _dbContext.PortfolioManagerTrades
            .Where(x => x.SessionId == sessionId)
            .ToListAsync(cancellationToken);

        if (trades.Count > 0)
        {
            _dbContext.PortfolioManagerTrades.RemoveRange(trades);
        }

        _dbContext.PortfolioManagerSessions.Remove(session);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<PortfolioSessionSummaryDto>> GetSessionsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var sessions = await _dbContext.PortfolioManagerSessions
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(cancellationToken);

        var sessionIds = sessions.Select(x => x.Id).ToList();
        var tradeStats = await _dbContext.PortfolioManagerTrades
            .AsNoTracking()
            .Where(x => sessionIds.Contains(x.SessionId))
            .GroupBy(x => x.SessionId)
            .Select(g => new
            {
                SessionId = g.Key,
                OpenPositions = g.Count(x => x.Status == PortfolioTradeStatus.OPEN),
                ClosedPositions = g.Count(x => x.Status == PortfolioTradeStatus.CLOSED)
            })
            .ToDictionaryAsync(x => x.SessionId, cancellationToken);

        return sessions.Select(session =>
        {
            tradeStats.TryGetValue(session.Id, out var stats);
            return MapSummary(session, stats?.OpenPositions ?? 0, stats?.ClosedPositions ?? 0);
        }).ToList();
    }

    public async Task<PortfolioSessionDetailDto?> GetSessionDetailAsync(
        string userId,
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.PortfolioManagerSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, cancellationToken);

        if (session == null)
        {
            return null;
        }

        var trades = await _dbContext.PortfolioManagerTrades
            .AsNoTracking()
            .Where(x => x.SessionId == sessionId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var openTrades = trades.Where(x => x.Status == PortfolioTradeStatus.OPEN).ToList();
        var closedTrades = trades.Where(x => x.Status != PortfolioTradeStatus.OPEN).ToList();

        return new PortfolioSessionDetailDto
        {
            Summary = MapSummary(session, openTrades.Count, closedTrades.Count),
            PreferredSectors = session.PreferredSectors,
            PreferredThemes = session.PreferredThemes,
            OpenPositions = openTrades.Select(MapTrade).ToList(),
            ClosedPositions = closedTrades.Select(MapTrade).ToList()
        };
    }

    public async Task<PortfolioRunResponseDto?> RunSessionAsync(
        string userId,
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        var runAt = DateTime.UtcNow;
        var session = await _dbContext.PortfolioManagerSessions
            .AsTracking()
            .FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, cancellationToken);

        if (session == null)
        {
            return null;
        }

        var idempotencySeconds = int.TryParse(_configuration["PortfolioManager:RunIdempotencySeconds"], out var parsedIdempotencySeconds)
            ? Math.Clamp(parsedIdempotencySeconds, 30, 3600)
            : 120;

        if (session.LastRunAt.HasValue && session.LastRunAt.Value >= runAt.AddSeconds(-idempotencySeconds))
        {
            var openPositions = await _dbContext.PortfolioManagerTrades
                .AsNoTracking()
                .CountAsync(x => x.SessionId == session.Id && x.Status == PortfolioTradeStatus.OPEN, cancellationToken);

            return new PortfolioRunResponseDto
            {
                SessionId = session.Id,
                OpenPositions = openPositions,
                AllocatedCapital = session.AllocatedCapital,
                UnrealizedPnl = session.UnrealizedPnl,
                Provider = session.LastProvider,
                Model = session.LastModel,
                RunAt = session.LastRunAt.Value
            };
        }

        var openTrades = await _dbContext.PortfolioManagerTrades
            .AsTracking()
            .Where(x => x.SessionId == sessionId && x.Status == PortfolioTradeStatus.OPEN)
            .ToListAsync(cancellationToken);

        foreach (var existingTrade in openTrades)
        {
            existingTrade.Status = PortfolioTradeStatus.CLOSED;
            existingTrade.ExitPrice = existingTrade.CurrentPrice;
            existingTrade.ClosedAt = DateTime.UtcNow;
            existingTrade.ExitReasoning = "Position closed due to portfolio rebalance.";
            existingTrade.Pnl = (existingTrade.CurrentPrice - existingTrade.EntryPrice) * existingTrade.Quantity;
            existingTrade.PnlPercent = existingTrade.EntryPrice == 0
                ? 0
                : (existingTrade.Pnl / (existingTrade.EntryPrice * existingTrade.Quantity)) * 100m;
            existingTrade.UpdatedAt = DateTime.UtcNow;
        }

        var optimizationRequest = BuildOptimizationRequest(session);
        var optimizedPortfolio = await _portfolioOptimizationService.OptimizePortfolioAsync(optimizationRequest, cancellationToken);

        var candidatePositions = FilterByPreferredSectors(optimizedPortfolio.Positions, session.PreferredSectors)
            .Take(session.MaxPositions)
            .ToList();

        if (!candidatePositions.Any())
        {
            candidatePositions = optimizedPortfolio.Positions
                .Take(session.MaxPositions)
                .ToList();
        }

        string lastProvider = string.Empty;
        string lastModel = string.Empty;

        var selectedPositions = new List<(OptimizedPosition Position, FusionDecision Decision)>();
        foreach (var position in candidatePositions)
        {
            var decision = await EvaluateFusionDecisionAsync(position, session, cancellationToken);
            if (decision.ShouldInclude)
            {
                selectedPositions.Add((position, decision));
            }

            if (selectedPositions.Count >= session.MaxPositions)
            {
                break;
            }
        }

        if (selectedPositions.Count == 0 && candidatePositions.Count > 0)
        {
            var fallbackPosition = candidatePositions
                .OrderByDescending(x => x.Confidence)
                .First();

            selectedPositions.Add((
                fallbackPosition,
                new FusionDecision(
                    true,
                    0m,
                    0m,
                    Math.Clamp(fallbackPosition.Confidence / 100m, 0m, 1m),
                    session.PreferredSectors.Any()
                        ? (session.PreferredSectors.Contains(fallbackPosition.Sector, StringComparer.OrdinalIgnoreCase) ? 1m : 0.15m)
                        : 0.5m,
                    false,
                    "Fallback include: no candidate crossed fusion threshold in this run.")));
        }

        foreach (var selection in selectedPositions)
        {
            var position = selection.Position;
            var reasoningResult = await GenerateReasoningAsync(position, session, selection.Decision.Evidence, cancellationToken);

            if (string.IsNullOrWhiteSpace(lastProvider))
            {
                lastProvider = reasoningResult.Provider;
                lastModel = reasoningResult.Model;
            }

            await _dbContext.PortfolioManagerTrades.AddAsync(new PortfolioManagerTrade
            {
                SessionId = session.Id,
                InstrumentId = position.InstrumentId,
                Symbol = position.Symbol,
                InstrumentName = position.InstrumentName,
                Sector = position.Sector,
                Strategy = position.Strategy.ToString(),
                Direction = position.Direction,
                EntryPrice = position.EntryPrice,
                CurrentPrice = position.EntryPrice,
                Quantity = position.Quantity,
                AllocatedCapital = position.AllocatedCapital,
                AllocationPercent = position.AllocationPercent,
                Confidence = position.Confidence,
                StopLoss = position.StopLoss,
                Target = position.Target,
                FusionScore = selection.Decision.Score,
                FusionNewsSignal = selection.Decision.NewsSignal,
                FusionTechnicalSignal = selection.Decision.TechnicalSignal,
                FusionSectorSignal = selection.Decision.SectorSignal,
                FusionDirectionVeto = selection.Decision.DirectionalVeto,
                FusionIncluded = selection.Decision.ShouldInclude,
                FusionEvidence = selection.Decision.Evidence,
                EntryReasoning = string.IsNullOrWhiteSpace(selection.Decision.Evidence)
                    ? reasoningResult.Reasoning
                    : $"{reasoningResult.Reasoning}{Environment.NewLine}{Environment.NewLine}Fusion: {selection.Decision.Evidence}",
                Signals = position.Signals,
                ModelProvider = reasoningResult.Provider,
                ModelName = reasoningResult.Model,
                Status = PortfolioTradeStatus.OPEN,
                OpenedAt = runAt,
                CreatedAt = runAt,
                UpdatedAt = runAt
            }, cancellationToken);
        }

        var allTrades = await _dbContext.PortfolioManagerTrades
            .AsNoTracking()
            .Where(x => x.SessionId == session.Id)
            .ToListAsync(cancellationToken);

        var sessionOpenTrades = allTrades.Where(x => x.Status == PortfolioTradeStatus.OPEN).ToList();
        var sessionClosedTrades = allTrades.Where(x => x.Status == PortfolioTradeStatus.CLOSED).ToList();

        session.AllocatedCapital = sessionOpenTrades.Sum(x => x.AllocatedCapital);
        session.UnrealizedPnl = sessionOpenTrades.Sum(CalculateUnrealizedPnl);
        session.RealizedPnl = sessionClosedTrades.Sum(x => x.Pnl ?? 0m);
        session.WinRatePercent = CalculateWinRate(sessionClosedTrades);
        session.LastProvider = lastProvider;
        session.LastModel = lastModel;
        session.LastRunAt = runAt;
        session.TotalRuns += 1;
        session.Status = session.AutoRebalanceEnabled ? PortfolioSessionStatus.RUNNING : PortfolioSessionStatus.READY;
        session.Mode = session.AutoRebalanceEnabled ? PortfolioSessionMode.SCHEDULED : PortfolioSessionMode.MANUAL;
        session.NextRunAt = session.AutoRebalanceEnabled ? runAt.AddHours(1) : null;
        session.UpdatedAt = runAt;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new PortfolioRunResponseDto
        {
            SessionId = session.Id,
            OpenPositions = sessionOpenTrades.Count,
            AllocatedCapital = session.AllocatedCapital,
            UnrealizedPnl = session.UnrealizedPnl,
            Provider = session.LastProvider,
            Model = session.LastModel,
            RunAt = runAt
        };
    }

    public async Task<PortfolioSessionSummaryDto?> SetScheduledStateAsync(
        string userId,
        long sessionId,
        bool isRunning,
        CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.PortfolioManagerSessions
            .AsTracking()
            .FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, cancellationToken);

        if (session == null)
        {
            return null;
        }

        session.AutoRebalanceEnabled = isRunning;
        session.Mode = isRunning ? PortfolioSessionMode.SCHEDULED : PortfolioSessionMode.MANUAL;
        session.Status = isRunning ? PortfolioSessionStatus.RUNNING : PortfolioSessionStatus.STOPPED;
        session.NextRunAt = isRunning ? DateTime.UtcNow.AddHours(1) : null;
        session.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await BuildSessionSummaryAsync(session, cancellationToken);
    }

    public async Task<byte[]?> ExportSessionCsvAsync(
        string userId,
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.PortfolioManagerSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, cancellationToken);

        if (session == null)
        {
            return null;
        }

        var trades = await _dbContext.PortfolioManagerTrades
            .AsNoTracking()
            .Where(x => x.SessionId == sessionId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var builder = new StringBuilder();
        builder.AppendLine("tradeId,symbol,sector,strategy,direction,status,quantity,entryPrice,currentPrice,exitPrice,pnl,pnlPercent,confidence,openedAt,closedAt,provider,model,entryReasoning");

        foreach (var trade in trades)
        {
            builder.AppendLine(string.Join(",",
                trade.Id,
                EscapeCsv(trade.Symbol),
                EscapeCsv(trade.Sector),
                EscapeCsv(trade.Strategy),
                EscapeCsv(trade.Direction),
                EscapeCsv(trade.Status.ToString()),
                trade.Quantity,
                trade.EntryPrice,
                trade.CurrentPrice,
                trade.ExitPrice,
                trade.Pnl,
                trade.PnlPercent,
                trade.Confidence,
                trade.OpenedAt.ToString("O"),
                trade.ClosedAt?.ToString("O"),
                EscapeCsv(trade.ModelProvider),
                EscapeCsv(trade.ModelName),
                EscapeCsv(trade.EntryReasoning)
            ));
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public async Task<IReadOnlyList<PortfolioNewsItemDto>> GetSessionNewsAsync(
        string userId,
        long sessionId,
        int hoursBack = 24,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.PortfolioManagerSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, cancellationToken);

        if (session == null)
        {
            return Array.Empty<PortfolioNewsItemDto>();
        }

        var trackedSymbols = await _dbContext.PortfolioManagerTrades
            .AsNoTracking()
            .Where(x => x.SessionId == sessionId)
            .Select(x => x.Symbol)
            .Distinct()
            .ToListAsync(cancellationToken);

        var symbolSet = trackedSymbols
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sectorSet = session.PreferredSectors
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var since = DateTime.UtcNow.AddHours(-Math.Abs(hoursBack));
        var impacts = await _dbContext.NewsImpacts
            .AsNoTracking()
            .Include(x => x.Article)
                .ThenInclude(a => a!.Keywords)
            .Include(x => x.Instrument)
            .Include(x => x.Sector)
            .Where(x => x.Article != null && x.Article.PublishedAt >= since)
            .OrderByDescending(x => x.Article!.PublishedAt)
            .Take(Math.Clamp(limit * 3, 30, 1000))
            .ToListAsync(cancellationToken);

        var result = impacts
            .Where(x =>
                (x.Instrument != null && symbolSet.Contains(x.Instrument.Symbol))
                || (x.Sector != null && sectorSet.Contains(x.Sector.Name))
                || (x.InstrumentId == null && x.SectorId == null))
            .Select(x => new PortfolioNewsItemDto
            {
                ArticleId = x.ArticleId,
                Source = x.Article!.Source,
                Headline = x.Article.Headline,
                Summary = x.Article.Summary,
                PublishedAt = x.Article.PublishedAt,
                Sentiment = x.Article.Sentiment.ToString(),
                SentimentScore = x.Article.SentimentScore,
                Direction = x.Direction.ToString(),
                ImpactScore = x.ImpactScore,
                Confidence = x.Confidence,
                Symbol = x.Instrument?.Symbol ?? string.Empty,
                Sector = x.Sector?.Name ?? string.Empty,
                Keywords = x.Article.Keywords.Select(k => k.Keyword).Take(8).ToList()
            })
            .OrderByDescending(x => x.PublishedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToList();

        return result;
    }

    public async Task<PortfolioEventTriggerResultDto> TriggerEventDrivenRunsAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var minMinutesBetweenRuns = int.TryParse(_configuration["PortfolioManager:EventTrigger:MinMinutesBetweenRuns"], out var parsedMinMinutes)
            ? Math.Clamp(parsedMinMinutes, 5, 180)
            : 20;
        var priceMoveThreshold = decimal.TryParse(_configuration["PortfolioManager:EventTrigger:PriceMoveThreshold"], out var parsedPriceMove)
            ? Math.Clamp(parsedPriceMove, 0.005m, 0.25m)
            : 0.03m;
        var volumeSpikeMultiple = decimal.TryParse(_configuration["PortfolioManager:EventTrigger:VolumeSpikeMultiple"], out var parsedVolumeSpike)
            ? Math.Clamp(parsedVolumeSpike, 1.1m, 10m)
            : 2.0m;
        var newsImpactThreshold = decimal.TryParse(_configuration["PortfolioManager:EventTrigger:NewsImpactThreshold"], out var parsedNewsImpact)
            ? Math.Clamp(parsedNewsImpact, 0.1m, 1.0m)
            : 0.65m;
        var sectorShiftThreshold = decimal.TryParse(_configuration["PortfolioManager:EventTrigger:SectorShiftThreshold"], out var parsedSectorShift)
            ? Math.Clamp(parsedSectorShift, 0.1m, 1.0m)
            : 0.55m;

        var sessions = await _dbContext.PortfolioManagerSessions
            .AsNoTracking()
            .Where(x => x.AutoRebalanceEnabled)
            .Where(x => x.Status == PortfolioSessionStatus.RUNNING || x.Status == PortfolioSessionStatus.READY)
            .Select(x => new { x.Id, x.UserId, x.LastRunAt })
            .ToListAsync(cancellationToken);

        if (sessions.Count == 0)
        {
            return new PortfolioEventTriggerResultDto
            {
                TriggeredAt = now,
                SessionsScanned = 0
            };
        }

        var sessionIds = sessions.Select(x => x.Id).ToList();
        var openTrades = await _dbContext.PortfolioManagerTrades
            .AsNoTracking()
            .Where(x => sessionIds.Contains(x.SessionId) && x.Status == PortfolioTradeStatus.OPEN)
            .Select(x => new
            {
                x.SessionId,
                x.InstrumentId,
                x.Symbol,
                x.Sector
            })
            .ToListAsync(cancellationToken);

        var instrumentIds = openTrades
            .Select(x => x.InstrumentId)
            .Distinct()
            .ToList();

        var pricesSince = DateTimeOffset.UtcNow.AddHours(-6);
        var recentPrices = await _dbContext.InstrumentPrices
            .AsNoTracking()
            .Where(x => instrumentIds.Contains(x.InstrumentId) && x.Timestamp >= pricesSince)
            .OrderByDescending(x => x.Timestamp)
            .ToListAsync(cancellationToken);

        var priceByInstrument = recentPrices
            .GroupBy(x => x.InstrumentId)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(x => x.Timestamp)
                    .Take(2)
                    .ToList());

        var newsSince = now.AddHours(-2);
        var instrumentNewsMap = await _dbContext.NewsImpacts
            .AsNoTracking()
            .Where(x => x.InstrumentId != null && instrumentIds.Contains(x.InstrumentId.Value))
            .Where(x => x.Article != null && x.Article.PublishedAt >= newsSince)
            .GroupBy(x => x.InstrumentId!.Value)
            .Select(g => new
            {
                InstrumentId = g.Key,
                MaxImpact = g.Max(x => Math.Abs(x.ImpactScore))
            })
            .ToDictionaryAsync(x => x.InstrumentId, x => x.MaxImpact, cancellationToken);

        var tradeSectors = openTrades
            .Select(x => x.Sector)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sectorNewsMap = await _dbContext.NewsImpacts
            .AsNoTracking()
            .Where(x => x.Sector != null && tradeSectors.Contains(x.Sector.Name))
            .Where(x => x.Article != null && x.Article.PublishedAt >= newsSince)
            .GroupBy(x => x.Sector!.Name)
            .Select(g => new
            {
                Sector = g.Key,
                MaxImpact = g.Max(x => Math.Abs(x.ImpactScore))
            })
            .ToDictionaryAsync(x => x.Sector, x => x.MaxImpact, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var result = new PortfolioEventTriggerResultDto
        {
            TriggeredAt = now,
            SessionsScanned = sessions.Count
        };

        foreach (var session in sessions)
        {
            if (session.LastRunAt.HasValue && session.LastRunAt.Value >= now.AddMinutes(-minMinutesBetweenRuns))
            {
                result.SkippedRecentRuns += 1;
                continue;
            }

            var sessionTrades = openTrades
                .Where(x => x.SessionId == session.Id)
                .ToList();

            if (sessionTrades.Count == 0)
            {
                continue;
            }

            var reasons = new List<string>();

            foreach (var trade in sessionTrades)
            {
                if (priceByInstrument.TryGetValue(trade.InstrumentId, out var twoPrices) && twoPrices.Count == 2)
                {
                    var latest = twoPrices[0];
                    var previous = twoPrices[1];

                    if (previous.Close > 0)
                    {
                        var pctMove = Math.Abs((latest.Close - previous.Close) / previous.Close);
                        if (pctMove >= priceMoveThreshold)
                        {
                            reasons.Add($"price move {trade.Symbol}: {pctMove:P2}");
                        }
                    }

                    if (previous.Volume > 0)
                    {
                        var volumeSpike = (decimal)latest.Volume / previous.Volume;
                        if (volumeSpike >= volumeSpikeMultiple)
                        {
                            reasons.Add($"volume spike {trade.Symbol}: {volumeSpike:N2}x");
                        }
                    }
                }

                if (instrumentNewsMap.TryGetValue(trade.InstrumentId, out var instrumentImpact)
                    && instrumentImpact >= newsImpactThreshold)
                {
                    reasons.Add($"instrument news impact {trade.Symbol}: {instrumentImpact:N2}");
                }

                if (!string.IsNullOrWhiteSpace(trade.Sector)
                    && sectorNewsMap.TryGetValue(trade.Sector, out var sectorImpact)
                    && sectorImpact >= sectorShiftThreshold)
                {
                    reasons.Add($"sector shift {trade.Sector}: {sectorImpact:N2}");
                }
            }

            if (reasons.Count == 0)
            {
                continue;
            }

            result.EventsDetected += 1;
            var run = await RunSessionAsync(session.UserId, session.Id, cancellationToken);
            if (run != null)
            {
                result.TriggeredRuns += 1;
                result.TriggeredSessions.Add(new PortfolioEventTriggerSessionDto
                {
                    SessionId = session.Id,
                    UserId = session.UserId,
                    Reason = string.Join("; ", reasons.Distinct())
                });
            }
        }

        return result;
    }

    private static decimal CalculateUnrealizedPnl(PortfolioManagerTrade trade)
    {
        var signedQuantity = trade.Direction.Equals("SELL", StringComparison.OrdinalIgnoreCase)
            ? -trade.Quantity
            : trade.Quantity;
        return (trade.CurrentPrice - trade.EntryPrice) * signedQuantity;
    }

    private static decimal CalculateWinRate(List<PortfolioManagerTrade> closedTrades)
    {
        if (closedTrades.Count == 0)
        {
            return 0;
        }

        var wins = closedTrades.Count(x => (x.Pnl ?? 0m) > 0m);
        return Math.Round((decimal)wins * 100m / closedTrades.Count, 2);
    }

    private PortfolioOptimizationRequest BuildOptimizationRequest(PortfolioManagerSession session)
    {
        var (maxRiskPerTrade, maxPortfolioRisk, maxSectorAllocation, minPositionPercent) = session.RiskProfile.ToLowerInvariant() switch
        {
            "conservative" => (1.5m, 4.0m, 25m, 8m),
            "aggressive" => (3.0m, 10.0m, 40m, 3m),
            _ => (2.0m, 6.0m, 30m, 5m)
        };

        return new PortfolioOptimizationRequest
        {
            TotalCapital = session.InitialCapital,
            MaxRiskPerTradePercent = maxRiskPerTrade,
            MaxPortfolioRiskPercent = maxPortfolioRisk,
            MaxPositions = session.MaxPositions,
            EnableSectorDiversification = true,
            MaxSectorAllocationPercent = maxSectorAllocation,
            MinPositionSizePercent = minPositionPercent,
            TimeframeMinutes = session.TimeframeMinutes,
            MinConfidence = session.MinConfidence
        };
    }

    private static List<OptimizedPosition> FilterByPreferredSectors(
        List<OptimizedPosition> positions,
        List<string> preferredSectors)
    {
        if (!preferredSectors.Any())
        {
            return positions;
        }

        var sectorSet = preferredSectors
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return positions
            .Where(x => sectorSet.Contains(x.Sector))
            .ToList();
    }

    private async Task<PortfolioSessionSummaryDto> BuildSessionSummaryAsync(
        PortfolioManagerSession session,
        CancellationToken cancellationToken)
    {
        var tradeStats = await _dbContext.PortfolioManagerTrades
            .AsNoTracking()
            .Where(x => x.SessionId == session.Id)
            .GroupBy(x => x.SessionId)
            .Select(g => new
            {
                OpenPositions = g.Count(x => x.Status == PortfolioTradeStatus.OPEN),
                ClosedPositions = g.Count(x => x.Status == PortfolioTradeStatus.CLOSED)
            })
            .FirstOrDefaultAsync(cancellationToken);

        return MapSummary(session, tradeStats?.OpenPositions ?? 0, tradeStats?.ClosedPositions ?? 0);
    }

    private static PortfolioSessionSummaryDto MapSummary(
        PortfolioManagerSession session,
        int openPositions,
        int closedPositions)
    {
        return new PortfolioSessionSummaryDto
        {
            SessionId = session.Id,
            SessionName = session.SessionName,
            Budget = session.InitialCapital,
            RiskProfile = session.RiskProfile,
            AutoRebalanceEnabled = session.AutoRebalanceEnabled,
            MaxPositions = session.MaxPositions,
            TimeframeMinutes = session.TimeframeMinutes,
            MinConfidence = session.MinConfidence,
            Status = session.Status.ToString(),
            Mode = session.Mode.ToString(),
            OpenPositions = openPositions,
            ClosedPositions = closedPositions,
            AllocatedCapital = session.AllocatedCapital,
            UnrealizedPnl = session.UnrealizedPnl,
            RealizedPnl = session.RealizedPnl,
            WinRatePercent = session.WinRatePercent,
            LastRunAt = session.LastRunAt,
            UpdatedAt = session.UpdatedAt
        };
    }

    private static PortfolioPositionDto MapTrade(PortfolioManagerTrade trade)
    {
        return new PortfolioPositionDto
        {
            TradeId = trade.Id,
            InstrumentId = trade.InstrumentId,
            Symbol = trade.Symbol,
            InstrumentName = trade.InstrumentName,
            Sector = trade.Sector,
            Strategy = trade.Strategy,
            Direction = trade.Direction,
            EntryPrice = trade.EntryPrice,
            CurrentPrice = trade.CurrentPrice,
            ExitPrice = trade.ExitPrice,
            Quantity = trade.Quantity,
            AllocationPercent = trade.AllocationPercent,
            AllocatedCapital = trade.AllocatedCapital,
            Confidence = trade.Confidence,
            FusionScore = trade.FusionScore,
            FusionNewsSignal = trade.FusionNewsSignal,
            FusionTechnicalSignal = trade.FusionTechnicalSignal,
            FusionSectorSignal = trade.FusionSectorSignal,
            FusionDirectionVeto = trade.FusionDirectionVeto,
            FusionIncluded = trade.FusionIncluded,
            FusionEvidence = trade.FusionEvidence,
            Pnl = trade.Pnl,
            PnlPercent = trade.PnlPercent,
            Status = trade.Status.ToString(),
            EntryReasoning = trade.EntryReasoning,
            ExitReasoning = trade.ExitReasoning,
            Signals = trade.Signals,
            ModelProvider = trade.ModelProvider,
            ModelName = trade.ModelName,
            OpenedAt = trade.OpenedAt,
            ClosedAt = trade.ClosedAt
        };
    }

    private static List<string> NormalizeList(List<string> values)
    {
        return values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeRiskProfile(string riskProfile)
    {
        if (string.IsNullOrWhiteSpace(riskProfile))
        {
            return "balanced";
        }

        var normalized = riskProfile.Trim().ToLowerInvariant();
        return normalized is "conservative" or "balanced" or "aggressive"
            ? normalized
            : "balanced";
    }

    private static void ApplySessionInputs(
        PortfolioManagerSession session,
        string sessionName,
        decimal budget,
        string riskProfile,
        List<string> preferredSectors,
        List<string> preferredThemes,
        bool autoRebalanceEnabled,
        int maxPositions,
        int timeframeMinutes,
        int minConfidence)
    {
        session.SessionName = sessionName;
        session.InitialCapital = budget;
        session.RiskProfile = riskProfile;
        session.PreferredSectors = NormalizeList(preferredSectors);
        session.PreferredThemes = NormalizeList(preferredThemes);
        session.AutoRebalanceEnabled = autoRebalanceEnabled;
        session.MaxPositions = maxPositions <= 0 ? 10 : maxPositions;
        session.TimeframeMinutes = timeframeMinutes <= 0 ? 15 : timeframeMinutes;
        session.MinConfidence = minConfidence <= 0 ? 60 : minConfidence;
        session.Mode = autoRebalanceEnabled ? PortfolioSessionMode.SCHEDULED : PortfolioSessionMode.MANUAL;
        session.Status = autoRebalanceEnabled ? PortfolioSessionStatus.RUNNING : PortfolioSessionStatus.READY;
        session.NextRunAt = autoRebalanceEnabled ? DateTime.UtcNow.AddHours(1) : null;
        session.UpdatedAt = DateTime.UtcNow;
    }

    private async Task<(string Reasoning, string Provider, string Model)> GenerateReasoningAsync(
        OptimizedPosition position,
        PortfolioManagerSession session,
        string fusionEvidence,
        CancellationToken cancellationToken)
    {
        var provider = (_configuration["AiProvider:Active"] ?? _configuration["AiProvider:Provider"] ?? "Ollama").Trim();
        var newsContext = await BuildNewsContextAsync(position, cancellationToken);

        var fallbackReasoning = BuildFallbackReasoning(position, session);
        var isEnabled = bool.TryParse(_configuration["PortfolioManager:EnableLlmReasoning"], out var parsedEnabled)
            ? parsedEnabled
            : true;

        if (!isEnabled)
        {
            return (fallbackReasoning, provider, string.Empty);
        }

        try
        {
            if (provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
            {
                var model = await GetBestOllamaModelAsync(cancellationToken);
                var reasoning = await GenerateWithOllamaAsync(model, position, session, newsContext, fusionEvidence, cancellationToken);
                return (reasoning, provider, model);
            }

            var anthropicModel = _configuration["AiProvider:AnthropicModel"] ?? "claude-opus-4-5";
            var anthropicReasoning = await GenerateWithAnthropicAsync(anthropicModel, position, session, newsContext, fusionEvidence, cancellationToken);
            return (anthropicReasoning, provider, anthropicModel);
        }
        catch
        {
            return (fallbackReasoning, provider, string.Empty);
        }
    }

    private static string BuildFallbackReasoning(OptimizedPosition position, PortfolioManagerSession session)
    {
        var sectorsText = session.PreferredSectors.Any()
            ? string.Join(", ", session.PreferredSectors)
            : "broad market opportunity set";

        return $"Selected {position.Symbol} ({position.Strategy}) with {position.Confidence:N2}% confidence, allocation {position.AllocationPercent:N2}%, entry {position.EntryPrice:N2}, stop {position.StopLoss:N2}, and target {position.Target:N2}. This aligns with {session.RiskProfile} risk profile and preferred sectors: {sectorsText}.";
    }

    private async Task<string> GetBestOllamaModelAsync(CancellationToken cancellationToken)
    {
        var ollamaBaseUrl = (_configuration["AiProvider:OllamaBaseUrl"] ?? "http://localhost:11434").TrimEnd('/');
        var configuredModel = _configuration["AiProvider:OllamaModel"] ?? _configuration["AiProvider:DefaultModel"] ?? "llama3.1:8b-instruct";
        var useBestAvailable = bool.TryParse(_configuration["AiProvider:OllamaUseBestAvailable"], out var parsed) && parsed;

        if (!useBestAvailable)
        {
            return configuredModel;
        }

        var preferredModels = _configuration.GetSection("AiProvider:OllamaBestModelPriority").Get<string[]>()
            ?? new[] { "llama3.1:8b-instruct", "phi3.1-mini" };

        var client = _httpClientFactory.CreateClient();
        using var response = await client.GetAsync($"{ollamaBaseUrl}/api/tags", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return configuredModel;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(content);

        if (!document.RootElement.TryGetProperty("models", out var modelsElement) || modelsElement.ValueKind != JsonValueKind.Array)
        {
            return configuredModel;
        }

        var installedModels = modelsElement
            .EnumerateArray()
            .Where(x => x.TryGetProperty("name", out _))
            .Select(x => x.GetProperty("name").GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToList();

        foreach (var preferred in preferredModels)
        {
            var match = installedModels.FirstOrDefault(m => string.Equals(m, preferred, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        return installedModels.FirstOrDefault() ?? configuredModel;
    }

    private async Task<string> GenerateWithOllamaAsync(
        string model,
        OptimizedPosition position,
        PortfolioManagerSession session,
        string newsContext,
        string fusionEvidence,
        CancellationToken cancellationToken)
    {
        var ollamaBaseUrl = (_configuration["AiProvider:OllamaBaseUrl"] ?? "http://localhost:11434").TrimEnd('/');
        var payload = new
        {
            model,
            prompt = BuildReasoningPrompt(position, session, newsContext, fusionEvidence),
            stream = false,
            options = new
            {
                num_predict = 350
            }
        };

        var client = _httpClientFactory.CreateClient();
        using var response = await client.PostAsync(
            $"{ollamaBaseUrl}/api/generate",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Ollama reasoning request failed.");
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("response", out var responseText))
        {
            throw new InvalidOperationException("Ollama response text missing.");
        }

        if (responseText.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("Ollama response format is invalid.");
        }

        var parsed = responseText.GetString();
        if (string.IsNullOrWhiteSpace(parsed))
        {
            throw new InvalidOperationException("Ollama response was empty.");
        }

        return parsed;
    }

    private async Task<string> GenerateWithAnthropicAsync(
        string model,
        OptimizedPosition position,
        PortfolioManagerSession session,
        string newsContext,
        string fusionEvidence,
        CancellationToken cancellationToken)
    {
        var apiKey = _configuration["Anthropic:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Anthropic API key is not configured.");
        }

        var payload = new
        {
            model,
            max_tokens = 350,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = BuildReasoningPrompt(position, session, newsContext, fusionEvidence)
                }
            }
        };

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        requestMessage.Headers.Add("x-api-key", apiKey);
        requestMessage.Headers.Add("anthropic-version", "2023-06-01");

        var client = _httpClientFactory.CreateClient();
        using var response = await client.SendAsync(requestMessage, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Anthropic reasoning request failed.");
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(content);

        if (!TryExtractAnthropicText(document.RootElement, out var extractedText)
            || string.IsNullOrWhiteSpace(extractedText))
        {
            throw new InvalidOperationException("Anthropic response content missing or malformed.");
        }

        return extractedText;
    }

    private static bool TryExtractAnthropicText(JsonElement root, out string text)
    {
        text = string.Empty;
        if (!root.TryGetProperty("content", out var contentArray)
            || contentArray.ValueKind != JsonValueKind.Array
            || contentArray.GetArrayLength() == 0)
        {
            return false;
        }

        foreach (var item in contentArray.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var raw = item.GetString();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    text = raw;
                    return true;
                }
            }

            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("text", out var textValue)
                && textValue.ValueKind == JsonValueKind.String)
            {
                var raw = textValue.GetString();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    text = raw;
                    return true;
                }
            }
        }

        return false;
    }

    private static string BuildReasoningPrompt(
        OptimizedPosition position,
        PortfolioManagerSession session,
        string newsContext,
        string fusionEvidence)
    {
        var signals = position.Signals.Any()
            ? string.Join("; ", position.Signals)
            : "No additional signals available";

        var sectors = session.PreferredSectors.Any()
            ? string.Join(", ", session.PreferredSectors)
            : "None specified";

        var themes = session.PreferredThemes.Any()
            ? string.Join(", ", session.PreferredThemes)
            : "None specified";

        return $"""
You are generating a concise portfolio decision rationale.

User profile:
- Risk profile: {session.RiskProfile}
- Preferred sectors: {sectors}
- Preferred themes: {themes}

Proposed trade:
- Symbol: {position.Symbol}
- Sector: {position.Sector}
- Strategy: {position.Strategy}
- Direction: {position.Direction}
- Entry: {position.EntryPrice}
- Stop Loss: {position.StopLoss}
- Target: {position.Target}
- Allocation%: {position.AllocationPercent}
- Confidence%: {position.Confidence}
- Signals: {signals}

Recent news context:
{newsContext}

Fusion evidence:
{fusionEvidence}

Return 3 to 5 bullet points explaining why this trade should be included now, including risk context and what invalidates the trade.
""";
    }

    private async Task<FusionDecision> EvaluateFusionDecisionAsync(
        OptimizedPosition position,
        PortfolioManagerSession session,
        CancellationToken cancellationToken)
    {
        var since = DateTime.UtcNow.AddHours(-24);
        var impacts = await _dbContext.NewsImpacts
            .AsNoTracking()
            .Include(x => x.Article)
            .Include(x => x.Sector)
            .Include(x => x.Instrument)
            .Where(x => x.Article != null && x.Article.PublishedAt >= since)
            .Where(x =>
                (x.Instrument != null && x.Instrument.Symbol == position.Symbol)
                || (x.Sector != null && x.Sector.Name == position.Sector)
                || (x.InstrumentId == null && x.SectorId == null))
            .Take(20)
            .ToListAsync(cancellationToken);

        decimal newsSignal = 0m;
        if (impacts.Count > 0)
        {
            newsSignal = impacts
                .Select(x => DirectionToSignedValue(x.Direction) * x.ImpactScore * x.Confidence)
                .Average();
            newsSignal = Math.Clamp(newsSignal, -1m, 1m);
        }

        var technicalSignal = Math.Clamp(position.Confidence / 100m, 0m, 1m);
        var hasSectorPreference = session.PreferredSectors.Any();
        var sectorPreferenceSignal = !hasSectorPreference
            ? 0.5m
            : session.PreferredSectors.Contains(position.Sector, StringComparer.OrdinalIgnoreCase)
                ? 1m
                : 0.15m;
        var sectorMarketSignal = await _sectorIntelligenceService.GetSectorSignalAsync(position.Sector, cancellationToken);
        var normalizedSectorMarket = (sectorMarketSignal + 1m) / 2m;
        var sectorSignal = hasSectorPreference
            ? Math.Clamp((sectorPreferenceSignal * 0.70m) + (normalizedSectorMarket * 0.30m), 0m, 1m)
            : Math.Clamp(normalizedSectorMarket, 0m, 1m);

        var newsWeight = ParseDecimalConfig("PortfolioManager:Fusion:NewsWeight", 0.35m);
        var technicalWeight = ParseDecimalConfig("PortfolioManager:Fusion:TechnicalWeight", 0.50m);
        var sectorWeight = ParseDecimalConfig("PortfolioManager:Fusion:SectorWeight", 0.15m);
        var minFusion = ParseDecimalConfig("PortfolioManager:Fusion:MinScore", 0.55m);

        var normalizedNews = (newsSignal + 1m) / 2m;
        var fusionScore = (normalizedNews * newsWeight) + (technicalSignal * technicalWeight) + (sectorSignal * sectorWeight);

        var isLong = position.Direction.Equals("BUY", StringComparison.OrdinalIgnoreCase);
        var isShort = position.Direction.Equals("SELL", StringComparison.OrdinalIgnoreCase);
        var directionalVeto = (isLong && newsSignal <= -0.35m) || (isShort && newsSignal >= 0.35m);

        var include = fusionScore >= minFusion && !directionalVeto;
        var evidence = $"score={fusionScore:N3} threshold={minFusion:N2} technical={technicalSignal:N2} news={newsSignal:N2} sector={sectorSignal:N2} sectorMarket={sectorMarketSignal:N2} veto={directionalVeto}";

        return new FusionDecision(
            include,
            fusionScore,
            newsSignal,
            technicalSignal,
            sectorSignal,
            directionalVeto,
            evidence);
    }

    private decimal ParseDecimalConfig(string key, decimal fallback)
    {
        if (!decimal.TryParse(_configuration[key], out var parsed))
        {
            return fallback;
        }

        return parsed;
    }

    private static decimal DirectionToSignedValue(NewsImpactDirection direction)
    {
        return direction switch
        {
            NewsImpactDirection.BULLISH => 1m,
            NewsImpactDirection.BEARISH => -1m,
            _ => 0m
        };
    }

    private async Task<string> BuildNewsContextAsync(OptimizedPosition position, CancellationToken cancellationToken)
    {
        var since = DateTime.UtcNow.AddHours(-24);
        var impacts = await _dbContext.NewsImpacts
            .AsNoTracking()
            .Include(x => x.Article)
            .Include(x => x.Sector)
            .Include(x => x.Instrument)
            .Where(x => x.Article != null && x.Article.PublishedAt >= since)
            .Where(x =>
                (x.Instrument != null && x.Instrument.Symbol == position.Symbol)
                || (x.Sector != null && x.Sector.Name == position.Sector)
                || (x.InstrumentId == null && x.SectorId == null))
            .OrderByDescending(x => x.Article!.PublishedAt)
            .Take(3)
            .ToListAsync(cancellationToken);

        if (impacts.Count == 0)
        {
            return "No high relevance recent news was found for this symbol or sector.";
        }

        var lines = impacts.Select(x =>
            $"- {x.Article!.Headline} | direction={x.Direction} | score={x.ImpactScore:N2} | confidence={x.Confidence:N2}");

        return string.Join(Environment.NewLine, lines);
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }

    private sealed record FusionDecision(
        bool ShouldInclude,
        decimal Score,
        decimal NewsSignal,
        decimal TechnicalSignal,
        decimal SectorSignal,
        bool DirectionalVeto,
        string Evidence);
}
