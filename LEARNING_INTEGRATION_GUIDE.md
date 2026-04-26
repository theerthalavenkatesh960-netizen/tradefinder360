# Phase 4c: Portfolio Fusion Learning - Complete Integration Guide

## Overview

This document describes the complete stack for **Portfolio Fusion Learning**, the adaptive orchestration layer that continuously improves trading configuration based on live performance.

### Architecture Layers

```
┌─────────────────────────────────────────────────────────────┐
│  Frontend React Layer                                        │
│  ├─ useLearning Hook (state, API calls)                      │
│  ├─ LearningPanel (results, approval UI)                     │
│  └─ ConfigHistory (audit trail, rollback)                    │
└─────────────────────────────────────────────────────────────┘
                              ↓
        REST API (LearningController - /api/portfolio/learning)
                              ↓
┌─────────────────────────────────────────────────────────────┐
│  Backend Orchestration Layer                                 │
│  ├─ PortfolioLearningOrchestrator (thin coordinator)         │
│  └─ LearningResultDto + supporting DTOs                      │
└─────────────────────────────────────────────────────────────┘
                              ↓
        Heavy Lifting (TradingSystem.AI Services)
                              ↓
┌─────────────────────────────────────────────────────────────┐
│  AI Services Layer (TradingSystem.AI)                        │
│  ├─ PortfolioPerformanceAnalyzer                             │
│  │  └─ Computes performance metrics from recent sessions     │
│  ├─ PortfolioLearningService                                 │
│  │  ├─ EvaluateLearningNeed()                                │
│  │  └─ ComputeAdaptiveConfig()                               │
│  ├─ SignalCorrelationAnalyzer                                │
│  │  └─ Correlates fusion signals with outcomes               │
│  └─ AI Integration (Claude via Anthropic SDK)                │
│     └─ Reasoning generation, risk assessment, insights       │
└─────────────────────────────────────────────────────────────┘
                              ↓
    Data Persistence (EF Core Repositories)
                              ↓
┌─────────────────────────────────────────────────────────────┐
│  Database Layer                                              │
│  ├─ FusionLearningConfig Table                               │
│  │  └─ All iterations, proposed configs, status history      │
│  ├─ PortfolioPerformanceHistory Table                        │
│  │  └─ Time-series of metrics for learning feedback          │
│  ├─ PortfolioManagerSession Table (existing)                 │
│  │  └─ For correlation analysis                              │
│  └─ Trades Table (existing)                                  │
│     └─ For performance metrics computation                    │
└─────────────────────────────────────────────────────────────┘
```

---

## 1. Backend Setup

### 1.1 Dependency Injection (Program.cs)

```csharp
public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplicationBuilder.CreateBuilder(args);

        // Add EF Core
        builder.Services.AddDbContext<TradingDbContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

        // Add repositories
        builder.Services.AddScoped<IFusionLearningConfigRepository, FusionLearningConfigRepository>();
        builder.Services.AddScoped<IPortfolioPerformanceHistoryRepository, PortfolioPerformanceHistoryRepository>();

        // Add AI services (all heavy lifting)
        builder.Services.AddScoped<PortfolioPerformanceAnalyzer>();
        builder.Services.AddScoped<PortfolioLearningService>();
        builder.Services.AddScoped<SignalCorrelationAnalyzer>();

        // Add Anthropic client (for Claude reasoning)
        builder.Services.AddScoped<AnthropicClient>(_ =>
            new AnthropicClient(apiKey: builder.Configuration["Anthropic:ApiKey"]));

        // Add orchestrator
        builder.Services.AddScoped<PortfolioLearningOrchestrator>();

        // Add controllers
        builder.Services.AddControllers();

        var app = builder.Build();
        app.MapControllers();
        app.Run();
    }
}
```

### 1.2 Database Schema

#### FusionLearningConfig Table
```sql
CREATE TABLE FusionLearningConfigs (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    Iteration INT NOT NULL,
    Status NVARCHAR(50) NOT NULL, -- "ACTIVE", "INACTIVE", "ROLLED_BACK", "REJECTED"
    
    -- Weights & Parameters
    TechnicalWeight DECIMAL(5,3) NOT NULL,    -- 0.000 to 1.000
    NewsWeight DECIMAL(5,3) NOT NULL,
    SectorWeight DECIMAL(5,3) NOT NULL,
    MinimumFusionScore DECIMAL(5,3) NOT NULL,
    NewsNegativeBoundary DECIMAL(5,3) NOT NULL,
    NewsPositiveBoundary DECIMAL(5,3) NOT NULL,
    
    -- Metadata
    ReasoningText NVARCHAR(MAX),
    RiskAssessment NVARCHAR(MAX),
    SessionsAnalyzed INT,
    
    -- Timestamps
    CreatedAt DATETIME2 NOT NULL,
    AppliedAt DATETIME2 NULL,
    RolledBackAt DATETIME2 NULL,
    
    CONSTRAINT UC_FusionLearningConfigs_Iteration UNIQUE (Iteration)
);

CREATE INDEX IDX_Status ON FusionLearningConfigs(Status);
CREATE INDEX IDX_CreatedAt ON FusionLearningConfigs(CreatedAt DESC);
```

#### PortfolioPerformanceHistory Table
```sql
CREATE TABLE PortfolioPerformanceHistories (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    SessionId BIGINT,
    
    -- Metrics (all FLOAT for time-series)
    WinRate FLOAT NOT NULL,
    SharpeRatio FLOAT NOT NULL,
    MaxDrawdown FLOAT NOT NULL,
    ProfitFactor FLOAT NOT NULL,
    AverageHoldDays FLOAT NOT NULL,
    AverageHoldEfficiency FLOAT NOT NULL,
    AverageFusionScore FLOAT NOT NULL,
    VetoRejectionRate FLOAT NOT NULL,
    TotalTrades INT NOT NULL,
    WinningTrades INT NOT NULL,
    LosingTrades INT NOT NULL,
    TotalPnL DECIMAL(18,2) NOT NULL,
    
    RecordedAt DATETIME2 NOT NULL,
    
    FOREIGN KEY (SessionId) REFERENCES PortfolioManagerSessions(Id)
);

CREATE INDEX IDX_RecordedAt ON PortfolioPerformanceHistories(RecordedAt DESC);
```

---

## 2. API Endpoints

### POST /api/portfolio/learning/trigger
**Trigger learning analysis on recent performance**

Request:
```json
{
  "userId": "user123",
  "triggerSource": "USER_MANUAL",
  "sessionsToAnalyze": 5
}
```

Response:
```json
{
  "iterationNumber": 5,
  "triggeredAt": "2024-01-15T10:30:00Z",
  "triggerSource": "USER_MANUAL",
  "status": "PENDING_ACTIVATION",
  
  "currentMetrics": {
    "winRate": 0.62,
    "sharpeRatio": 1.45,
    "maxDrawdown": -0.125,
    "profitFactor": 1.8,
    "averageHoldDays": 3.2,
    "averageHoldEfficiency": 0.85,
    "averageFusionScore": 0.72,
    "vetoRejectionRate": 0.15,
    "totalTrades": 450,
    "winningTrades": 279,
    "losingTrades": 171,
    "totalPnL": 125000
  },
  
  "priorConfig": {
    "iteration": 4,
    "technicalWeight": 0.40,
    "newsWeight": 0.35,
    "sectorWeight": 0.25,
    "minimumFusionScore": 0.65,
    "newsNegativeBoundary": -0.40,
    "newsPositiveBoundary": 0.50,
    "status": "ACTIVE"
  },
  
  "proposedConfig": {
    "iteration": 5,
    "technicalWeight": 0.42,
    "newsWeight": 0.38,
    "sectorWeight": 0.20,
    "minimumFusionScore": 0.68,
    "newsNegativeBoundary": -0.35,
    "newsPositiveBoundary": 0.55,
    "status": "PENDING_ACTIVATION"
  },
  
  "changes": [
    {
      "parameter": "TechnicalWeight",
      "oldValue": 0.40,
      "newValue": 0.42,
      "justification": "Technical signals showed high correlation with winning trades"
    },
    {
      "parameter": "NewsWeight",
      "oldValue": 0.35,
      "newValue": 0.38,
      "justification": "News signals detected improved predictive power"
    }
  ],
  
  "reasoningText": "Based on analysis of 450 trades over 5 sessions, technical weight should increase by 0.02 due to improved correlation with outcomes. News weight shows promise in recent sessions (+0.03). Veto thresholds tightened for better selectivity.",
  
  "riskAssessment": "Conservative tuning (max parameter change: ±0.05). Historical drawdown stable. No concentration risk detected.",
  
  "aiModelInsights": "Signal correlations show: technical_strength:+0.86, news_sentiment:+0.72, sector_momentum:+0.58. Fusion score threshold at 0.68 optimal for precision-recall trade-off.",
  
  "sessionsAnalyzed": 5
}
```

### POST /api/portfolio/learning/approve/{configId}
**Approve proposed config; make it active**

Response: Same as trigger, with `status: "APPLIED"`

### POST /api/portfolio/learning/reject/{configId}
**Reject proposed config; keep current active**

### POST /api/portfolio/learning/rollback
**Rollback to previous config if current underperforms**

### GET /api/portfolio/learning/history?limit=10
**Audit trail of all learning iterations**

### GET /api/portfolio/learning/current-config
**Get currently active config**

---

## 3. Frontend Integration

### 3.1 Using the useLearning Hook

```tsx
import { useLearning } from '../hooks/useLearning';

export const MyComponent = () => {
  const {
    state,
    triggerLearning,
    approveConfig,
    rejectConfig,
    rollbackConfig,
    fetchHistory,
    fetchCurrentConfig,
  } = useLearning('/api');

  // Trigger learning
  const handleTrigger = async () => {
    try {
      const result = await triggerLearning({
        userId: 'user123',
        triggerSource: 'USER_MANUAL',
        sessionsToAnalyze: 5,
      });
      console.log('Learning result:', result);
    } catch (err) {
      console.error('Learning failed:', err);
    }
  };

  // Approve config
  const handleApprove = async () => {
    if (state.lastResult?.proposedConfig?.iteration) {
      await approveConfig(state.lastResult.proposedConfig.iteration);
    }
  };

  // Get history
  useEffect(() => {
    fetchHistory(10);
  }, []);

  return (
    <div>
      <button onClick={handleTrigger} disabled={state.isLoading}>
        Trigger Learning
      </button>
      {state.lastResult && (
        <>
          <p>Sharpe: {state.lastResult.currentMetrics?.sharpeRatio}</p>
          <button onClick={handleApprove}>Approve</button>
        </>
      )}
    </div>
  );
};
```

### 3.2 Using LearningPanel Component

```tsx
import { LearningPanel } from '../components/LearningPanel';

export const Dashboard = () => {
  return (
    <div>
      <LearningPanel onRefresh={() => window.location.reload()} />
    </div>
  );
};
```

### 3.3 Using ConfigHistory Component

```tsx
import { ConfigHistory } from '../components/ConfigHistory';

export const HistoryPage = () => {
  return (
    <div>
      <ConfigHistory limit={20} />
    </div>
  );
};
```

---

## 4. Complete Workflow Example

### Scenario: User Presses "Trigger Learning"

**Step 1: Frontend submits trigger request**
```
LearningPanel → useLearning.triggerLearning() → POST /api/portfolio/learning/trigger
```

**Step 2: Backend orchestrator receives request**
```
LearningController.TriggerLearning()
  ↓
PortfolioLearningOrchestrator.TriggerLearningAsync()
```

**Step 3: Orchestrator calls AI services**
```
PortfolioPerformanceAnalyzer.AnalyzeLastSessionsAsync(userId, 5)
  ├─ Query DB: Last 5 sessions for userId
  ├─ Query DB: All trades in those sessions
  ├─ Compute: WinRate, SharpeRatio, MaxDrawdown, etc.
  └─ Return: PortfolioPerformanceMetrics

IFusionLearningConfigRepository.GetActiveConfigAsync()
  └─ Return: Current FusionLearningConfig (iteration 4)

SignalCorrelationAnalyzer.AnalyzeSignalCorrelationsAsync(sessionId)
  ├─ Query DB: All fusion signals from session
  ├─ Query DB: Trade outcomes linked to signals
  ├─ Compute: Correlation(TechnicalWeight, Profit), etc.
  └─ Return: Dictionary<string, float> correlations

PortfolioLearningService.ComputeAdaptiveConfigAsync()
  ├─ Input: currentMetrics, priorConfig, signalCorrelations
  ├─ Call Claude API: Generate reasoning + proposed weights
  ├─ Construct: Proposed FusionLearningConfig (iteration 5)
  └─ Return: FusionLearningConfig with ReasoningText, RiskAssessment
```

**Step 4: Orchestrator persists and responds**
```
IFusionLearningConfigRepository.AddAsync(proposedConfig)
  └─ Save to DB with Status = "PENDING_ACTIVATION"

IPortfolioPerformanceHistoryRepository.AddAsync(historyRecord)
  └─ Append metrics to time-series table

Return: LearningResultDto with all details

Response sent to frontend:
LearningPanel displays results, changes, reasoning
User sees two buttons: [Approve] [Reject]
```

**Step 5: User clicks "Approve"**
```
LearningPanel.handleApprove()
  ↓
useLearning.approveConfig(configId)
  ↓
POST /api/portfolio/learning/approve/{configId}
  ↓
LearningController.ApproveConfig(configId)
  ↓
PortfolioLearningOrchestrator.ApproveConfigAsync(configId)
  ├─ Update DB: Set proposedConfig.Status = "ACTIVE"
  ├─ Update DB: Set priorConfig.Status = "INACTIVE"
  └─ Return: LearningResultDto with Status = "APPLIED"

Frontend updates UI: ✓ Config applied
Trading engine loads new config from DB
Portfolio Manager uses new weights for next decisions
```

---

## 5. Data Flow Diagram

```
┌─────────────────────────────────────────────────────────────┐
│         Portfolio Manager (Trading Engine)                  │
│         Loads active config, makes decisions                │
│         Trades complete, outcomes recorded                  │
└────────────────────────┬────────────────────────────────────┘
                         ↓
        PortfolioManagerSession + Trades saved to DB
                         ↓
┌─────────────────────────────────────────────────────────────┐
│   User Triggers Learn (via UI) or Auto-trigger via scheduler │
└────────────────────────┬────────────────────────────────────┘
                         ↓
         PortfolioPerformanceAnalyzer reads DB
         Computes: WinRate, Sharpe, Drawdown, etc.
                         ↓
         PortfolioLearningService evaluates need
         If needed: calls Claude API for reasoning
                         ↓
         Claude returns: weights, reasoning, risk assessment
                         ↓
         New FusionLearningConfig saved
         PortfolioPerformanceHistory appended
                         ↓
         Response sent to frontend
         User approves/rejects in UI
                         ↓
         If approved: Update Status = "ACTIVE"
         Trading engine loads new config on next cycle
                         ↓
         Performance metrics recorded again
         Loop repeats...
```

---

## 6. Error Handling & Resilience

### Learning Service Failures (Non-Blocking)

If Claude API fails:
```csharp
try
{
    var result = await _learningService.ComputeAdaptiveConfigAsync(...);
}
catch (HttpRequestException ex)
{
    // Claude API unavailable; use heuristic fallback
    _logger.LogWarning("Claude API failed; using heuristic tuning");
    var fallbackConfig = GenerateHeuristicConfig(currentMetrics, priorConfig);
    // Continue with fallback; notify user of degradation
}
```

### Database Failures

```csharp
if (await _configRepository.GetActiveConfigAsync() == null)
{
    // No active config; use hardcoded defaults
    _logger.LogError("No active config found; using defaults");
    return UseDefaultConfig();
}
```

---

## 7. Monitoring & Observability

### Key Metrics to Track

1. **Learning Cycle Duration**
   - Time from trigger to result
   - Should be < 10 seconds for interactive feel

2. **Config Approval Rate**
   - % of proposed configs approved vs rejected
   - If < 20% approved, learning may be too aggressive

3. **Performance After Learning**
   - Sharpe ratio before/after approved config
   - If degraded, auto-rollback

4. **AI Service Latency**
   - Claude API response time
   - Fallback activation rate

### Application Insights Integration

```csharp
_telemetryClient.TrackEvent("LearningTriggered", new Dictionary<string, string>
{
    { "TriggerSource", triggerSource },
    { "SessionsAnalyzed", sessionsToAnalyze.ToString() },
}, new Dictionary<string, double>
{
    { "CurrentWinRate", currentMetrics.WinRate },
    { "CurrentSharpe", currentMetrics.SharpeRatio },
});

_telemetryClient.TrackEvent("ConfigApproved", new Dictionary<string, string>
{
    { "Iteration", proposedConfig.Iteration.ToString() },
});
```

---

## 8. Testing Strategy

### Unit Tests

```csharp
[Test]
public async Task TriggerLearning_WhenPerformanceHealthy_ReturnsNoLearningNeeded()
{
    // Arrange
    var metrics = CreateHealthyMetrics();
    _performanceAnalyzer.Setup(x => x.AnalyzeLastSessionsAsync(...))
        .ReturnsAsync(metrics);

    // Act
    var result = await _orchestrator.TriggerLearningAsync("user1");

    // Assert
    Assert.AreEqual("REJECTED", result.Status);
    Assert.IsNull(result.ProposedConfig);
}

[Test]
public async Task ApproveConfig_UpdatesStatusToActive()
{
    // Test config approval flow
}
```

### Integration Tests

```csharp
[Test]
public async Task FullLearningWorkflow_EndToEnd()
{
    // Setup: Create test session + trades
    // Trigger learning
    // Assert: Config proposed
    // Approve config
    // Assert: Config active, status updated in DB
}
```

---

## 9. Configuration

### appsettings.json

```json
{
  "LearningConfig": {
    "AutoTriggerEnabled": true,
    "AutoTriggerIntervalMinutes": 60,
    "SessionsToAnalyzePerTrigger": 5,
    "EnableClaudeInsights": true,
    "MaxParameterChangePercent": 10,
    "MinimumSessionsForLearning": 3
  },
  "Anthropic": {
    "ApiKey": "sk-ant-...",
    "Model": "claude-3-sonnet-20240229",
    "MaxTokens": 1000
  }
}
```

---

## 10. Deployment Checklist

- [ ] Database migrations applied (FusionLearningConfigs, PortfolioPerformanceHistories)
- [ ] EF Core repositories registered in DI container
- [ ] AI services registered in DI container
- [ ] PortfolioLearningOrchestrator registered
- [ ] LearningController deployed
- [ ] Anthropic API key configured in secrets
- [ ] Frontend hooks created (useLearning)
- [ ] Frontend components created (LearningPanel, ConfigHistory)
- [ ] API base URL configured in frontend
- [ ] Integration tests passing
- [ ] Monitoring/observability enabled
- [ ] Rollback procedure documented

---

## Summary

This is a **complete, production-ready learning system** with:

✅ Lightweight orchestrator  
✅ Heavy-lifting AI services  
✅ Persistent config audit trail  
✅ Transparent reasoning via Claude  
✅ User approval workflow  
✅ Full rollback capability  
✅ Time-series performance history  
✅ React frontend with UI  
✅ Error handling & resilience  

All components are modular, testable, and easily extensible.
