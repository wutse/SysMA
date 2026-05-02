# BrokerageMonitor — Architecture Review
**Date**: 2026-05-03 | **Reviewer**: Chief Software Architect | **Scope**: All Projects

---

## 📊 Architecture Health Score — Session Summary

| Project            | Previous | This Session | Δ   |
| ------------------ | -------- | ------------ | --- |
| **Domain**         | 9.7      | **9.7**      | —   |
| **Application**    | 10.0     | **9.5**      | ↓   |
| **Infrastructure** | 9.7      | **9.5**      | ↓   |
| **Web**            | 9.3      | **9.5**      | ↑   |
| **MailAgent**      | 9.0      | **9.0**      | —   |

---

## BrokerageMonitor.Domain (9.7 / 10)

### ✅ Architectural Strengths
- All six Aggregate Roots enforce invariants at construction time — no setters exposed on core state.
- `HealthRuleSchedule` rejects unsupported Cron tokens at construction time (no silent false-returns).
- `MarketSessionWindow` correctly uses `TimeOnly` for comparisons, avoiding full `DateTime` arithmetic.
- Value Objects are immutable and use structural equality (`IEquatable<T>`).

### ⚠️ Critical Violations
None.

### 💡 Refactoring Suggestions

1. **(ADVISORY — A6)** `MarketSessionWindow.IsWithinSession(DateTimeOffset)` extracts `.TimeOfDay` without normalizing the UTC offset. A `DateTimeOffset` representing the same instant but in a different time zone will yield a different `TimeOfDay` and produce an incorrect result. Document the required offset convention in a `<remarks>` XML comment, or enforce normalization by converting to a known offset before the call.

2. **(ADVISORY — A6)** Domain advisory from 2026-05-02b — unchanged. Carries forward.

---

## BrokerageMonitor.Application (9.5 / 10) ↓ from 10.0

### ✅ Architectural Strengths
- `DomainEventDispatcher` async chain is uniformly `.ConfigureAwait(false)` — no context capture risk.
- `AggregateHealthEvaluationService` fully uses injected `TimeProvider` for all clock reads.
- `DailyExecutionCreatorService` correctly injects `TimeProvider` and uses `GetLocalNow()`.
- DTOs properly decouple the domain from the presentation layer.

### ⚠️ Critical Violations

#### LOW — C1: `DateTime.Today` in `GetHealthDefinitionsQueryHandler`
**File**: `src/BrokerageMonitor.Application/UseCases/Health/GetHealthDefinitionsQueryHandler.cs:46`

```csharp
// BEFORE — direct clock read; untestable
var today = DateOnly.FromDateTime(DateTime.Today);
```

`GetHealthDefinitionsQueryHandler` does not inject `TimeProvider` at all. This breaks the testability pattern established by `AggregateHealthEvaluationService` and `DailyExecutionCreatorService`, and cannot be controlled in unit tests.

#### LOW — C2: `DateTimeOffset.UtcNow` in `StationStartupRecoveryService`
**File**: `src/BrokerageMonitor.Application/Startup/StationStartupRecoveryService.cs:70`

```csharp
// BEFORE — static clock; cannot be controlled in tests
var now = DateTimeOffset.UtcNow;
```

All other services use injected `TimeProvider`; `StationStartupRecoveryService` is the last outlier.

### 💡 Refactoring Suggestions

**Fix C1 — Inject `TimeProvider` into `GetHealthDefinitionsQueryHandler`:**

```csharp
// AFTER
public sealed class GetHealthDefinitionsQueryHandler
{
    private readonly IHealthMonitorDefinitionRepository _definitionRepo;
    private readonly IDailyExecutionRepository _executionRepo;
    private readonly IMonitoredComponentRepository _componentRepo;
    private readonly TimeProvider _timeProvider;                          // ← add
    private readonly ILogger<GetHealthDefinitionsQueryHandler> _logger;

    public GetHealthDefinitionsQueryHandler(
        IHealthMonitorDefinitionRepository definitionRepo,
        IDailyExecutionRepository executionRepo,
        IMonitoredComponentRepository componentRepo,
        TimeProvider timeProvider,                                         // ← add
        ILogger<GetHealthDefinitionsQueryHandler> logger)
    { ... }

    public async Task<IReadOnlyList<HealthMonitorDefinitionDto>> HandleAsync(...)
    {
        ...
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime); // ← fix
        ...
    }
}
```

**Fix C2 — Inject `TimeProvider` into `StationStartupRecoveryService`:**

```csharp
// AFTER — constructor gains TimeProvider
public StationStartupRecoveryService(
    IComponentStateRepository stateRepository,
    IMonitoredComponentRepository componentRepository,
    IComponentStateCache stateCache,
    IHeartbeatTimerRegistry timerRegistry,
    IDomainEventDispatcher eventDispatcher,
    IDailyExecutionCreatorService dailyExecutionCreator,
    TimeProvider timeProvider,                            // ← add
    ILogger<StationStartupRecoveryService> logger) { ... }

// Usage
var now = _timeProvider.GetUtcNow();  // ← replaces DateTimeOffset.UtcNow
```

---

## BrokerageMonitor.Infrastructure (9.5 / 10) ↓ from 9.7

### ✅ Architectural Strengths
- `AggregateHealthEvaluationJob` and all other Quartz jobs use `IServiceScopeFactory` for scoped service resolution — no captive dependency issue.
- `[DisallowConcurrentExecution]` applied on all Quartz jobs — safe for periodic triggers.
- `DailyExecutionCreatorJob` correctly delegates to `IDailyExecutionCreatorService`, maintaining layer separation.
- `DataRetentionJob` reads `RetentionDays` from `JobDataMap`, making it configurable without code changes.

### ⚠️ Critical Violations

#### LOW — I1: `DateTime.Today` in `DailyExecutionCreatorJob` (existing, downgraded from ADVISORY)
**File**: `src/BrokerageMonitor.Infrastructure/Scheduling/DailyExecutionCreatorJob.cs:31`

```csharp
var today = DateOnly.FromDateTime(DateTime.Today); // server local clock — untestable
```

This job fires at 05:30 daily. On a server with a non-local time zone the date could be off by one day. `TimeProvider` is already registered in the DI container (`TryAddSingleton(TimeProvider.System)`) — inject and use it.

#### LOW — I2: `DateTimeOffset.UtcNow` in `DataRetentionJob`
**File**: `src/BrokerageMonitor.Infrastructure/Scheduling/DataRetentionJob.cs` (cutoff calculation)

```csharp
var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays); // static clock; untestable
```

The retention cutoff should be derived from an injected `TimeProvider` for testability.

### 💡 Refactoring Suggestions

**Fix I1 — Inject `TimeProvider` into `DailyExecutionCreatorJob`:**

```csharp
// AFTER
public sealed class DailyExecutionCreatorJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;           // ← add
    private readonly ILogger<DailyExecutionCreatorJob> _logger;

    public DailyExecutionCreatorJob(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,                         // ← add
        ILogger<DailyExecutionCreatorJob> logger) { ... }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime); // ← fix
        ...
    }
}
```

**Fix I2 — Inject `TimeProvider` into `DataRetentionJob`:**

```csharp
// AFTER
private readonly TimeProvider _timeProvider;

// In Execute():
var cutoff = _timeProvider.GetUtcNow().AddDays(-retentionDays); // ← fix
```

---

## BrokerageMonitor.Web (9.5 / 10) ↑ from 9.3

### ✅ Architectural Strengths
- **D1 RESOLVED**: `SchedulerOptionsValidator` implementing `IValidateOptions<SchedulerOptions>` is now present and correctly registered as a singleton. `WebApplicationStartup` constructor resolves `IOptions<SchedulerOptions>.Value` before `app.Run()`, triggering validation at app startup before traffic is served.
- No scaffolding artifacts remain (`Counter.razor`, `Weather.razor` deleted in prior session).
- `OperatorSessionService` correctly scoped to Blazor Server circuit (`AddScoped`).
- SignalR hub and NLog wired up correctly.

### ⚠️ Critical Violations

#### STYLE — W1: Inconsistent Indentation in `SchedulerOptionsValidator`
**File**: `src/BrokerageMonitor.Web/Services/SchedulerOptionsValidator.cs`

The class body uses 2-space indentation while the rest of the codebase uses 4-space indentation. No functional impact, but violates code style consistency.

### 💡 Refactoring Suggestions

1. **(MINOR)** Use `AddOptions<SchedulerOptions>().Bind(...).ValidateOnStart()` in `Program.cs` alongside the existing `IValidateOptions` registration for explicit startup-time validation semantics — even though the current approach is functionally equivalent via `WebApplicationStartup`.

2. **(ADVISORY — A8)** Web layer test coverage remains at ~0.6%. Priority `bunit` targets: `DashboardPage`, `AlertCenterPage`, `HealthManagementPage`.

---

## BrokerageMonitor.MailAgent (9.0 / 10)

### ✅ Architectural Strengths
- STA thread architecture for COM interop is correctly implemented with `BlockingCollection<Action>` and `Marshal.ReleaseComObject` in all paths.
- Long-lived `_outlookApp` / `_outlookNs` session is managed correctly with lazy initialization and teardown on error.
- `MailRelayWorker` uses `PeriodicTimer` for non-overlapping polls with proper cancellation (`stoppingToken`).
- `OperationCanceledException` is correctly re-thrown, not swallowed.

### ⚠️ Critical Violations
None.

### 💡 Refactoring Suggestions

1. **(ADVISORY — MA1)** `ReadUnreadMails()` blocks its caller via `tcs.Task.GetAwaiter().GetResult()`. By design (STA bridge requirement). Improvement path: pass `CancellationToken` into the `TaskCompletionSource` and have the STA work item call `tcs.TrySetCanceled(ct)` when the token fires — prevents the caller from being stuck indefinitely on shutdown.

2. **(ADVISORY — MA2)** `PollAndPublishAsync` returns `Task.CompletedTask` with no `await` — a synchronous method with an `Async` suffix. Rename to `PollAndPublish()` returning `void`, or make genuinely async if the publish path moves to an async queue.

---

## 📝 Cross-Cutting Observations

### `TimeProvider` Consistency Gap
The codebase has adopted `TimeProvider` well in core services, but three outliers remain:

| File                                  | Issue                   |
| ------------------------------------- | ----------------------- |
| `GetHealthDefinitionsQueryHandler.cs` | `DateTime.Today`        |
| `StationStartupRecoveryService.cs`    | `DateTimeOffset.UtcNow` |
| `DailyExecutionCreatorJob.cs`         | `DateTime.Today`        |
| `DataRetentionJob.cs`                 | `DateTimeOffset.UtcNow` |

All four should inject and use `TimeProvider` from the already-registered singleton. This is a **systematic LOW** across the Application and Infrastructure layers.
