# BrokerageMonitor — Architecture Review (2026-05-03b)

> **Reviewer**: Chief Software Architect
> **Scope**: Full solution — `TimeProvider` gap sweep (second pass) + prior-item resolution audit

---

## 📊 Architecture Health Score

| Project            | Score    | Delta |
| ------------------ | -------- | ----- |
| **Domain**         | 9.7 / 10 | —     |
| **Application**    | 9.0 / 10 | ↓ 0.5 |
| **Infrastructure** | 9.0 / 10 | ↓ 0.5 |
| **MailAgent**      | 9.0 / 10 | —     |
| **Web**            | 9.5 / 10 | —     |

---

## ✅ Architectural Strengths

1. **Prior items resolved** — All four `TimeProvider` gaps flagged in `2026-05-03`:
   - `GetHealthDefinitionsQueryHandler` (C1) ✅ — `_timeProvider.GetLocalNow().DateTime`
   - `StationStartupRecoveryService` (C2) ✅ — `_timeProvider.GetUtcNow()`
   - `DailyExecutionCreatorJob` (I1) ✅ — `_timeProvider.GetLocalNow().DateTime`
   - `DataRetentionJob` (I2) ✅ — `_timeProvider.GetUtcNow()`

2. **Operator-action handlers** (`OverrideComponentStateHandler`, `AcknowledgeAlertHandler`, `ToggleMaintenanceModeHandler`) are well-structured: guard clauses validate all inputs at the boundary, `IDomainEventDispatcher` is used to raise domain events, and audit logging + real-time notification are co-located.

3. **`MailChannelProcessor`** implements a clean failure-first keyword evaluation algorithm with no raw SQL or infrastructure coupling.

4. **`HeartbeatTimeoutMonitor`** correctly uses `Task.Run` + `ContinueWith(OnlyOnFaulted)` to bridge synchronous `System.Threading.Timer` callbacks into async work without blocking.

5. **`GetAlertsQueryHandler`** correctly falls back to a 7-day window when no date range is specified — a sensible default rather than an unbounded table scan.

---

## ⚠️ Critical Violations

### Application — `TimeProvider` consistency gaps (C3–C8)

The pattern established by `AggregateHealthEvaluationService` and `DailyExecutionCreatorService` (inject `TimeProvider`, call `_timeProvider.GetUtcNow()`) has **not been applied** to six newly added Application types:

| #   | File                               | Line  | Issue                                                         |
| --- | ---------------------------------- | ----- | ------------------------------------------------------------- |
| C3  | `OverrideComponentStateHandler.cs` | 84    | `var now = DateTimeOffset.UtcNow`                             |
| C4  | `AcknowledgeAlertHandler.cs`       | 70    | `var now = DateTimeOffset.UtcNow`                             |
| C5  | `GetAlertsQueryHandler.cs`         | 25–26 | `DateTimeOffset.UtcNow.AddDays(-7)` / `DateTimeOffset.UtcNow` |
| C6  | `ToggleMaintenanceModeHandler.cs`  | 79    | `var now = DateTimeOffset.UtcNow`                             |
| C7  | `MailChannelProcessor.cs`          | 50    | `var now = DateTimeOffset.UtcNow`                             |
| C8  | `AlertEvaluationService.cs`        | 207   | `sentAt: DateTimeOffset.UtcNow`                               |

Severity: **LOW** (no functional defect in production; breaks test determinism).

### Infrastructure — `TimeProvider` consistency gaps (I3–I6)

| #   | File                              | Line | Issue                                                                  |
| --- | --------------------------------- | ---- | ---------------------------------------------------------------------- |
| I3  | `HeartbeatTimeoutMonitor.cs`      | 215  | `DateTimeOffset.UtcNow` in timer callback; `TimeProvider` not injected |
| I4  | `AlertRecordRepository.cs`        | 85   | `DateTimeOffset.UtcNow.ToString("O")` in `AcknowledgeBySystemAsync`    |
| I5  | `MonitoredComponentRepository.cs` | 78   | `DateTimeOffset.UtcNow.ToString("O")` in `UpsertAsync`                 |
| I6  | `MonitoredSystemRepository.cs`    | 64   | `DateTimeOffset.UtcNow.ToString("O")` in `UpsertAsync`                 |

Severity for I3: **LOW** (breaks event timestamp determinism in tests).
Severity for I4–I6: **LOW** (repository timestamp not testable; inconsistent with overall strategy).

### Indentation style — `GetAlertsQueryHandler.cs`

The handler uses 2-space indentation (same issue as `SchedulerOptionsValidator.cs`). Consistent 4-space indentation is expected across the project.

---

## 💡 Refactoring Suggestions

### R1 — Inject `TimeProvider` into Application handlers (C3–C8)

All six handlers follow a trivial pattern: add `TimeProvider` constructor parameter, replace `DateTimeOffset.UtcNow` with `_timeProvider.GetUtcNow()`.

### R2 — Inject `TimeProvider` into `HeartbeatTimeoutMonitor` (I3)

`HeartbeatTimeoutMonitor` already uses `IServiceScopeFactory`; adding `TimeProvider` is a one-line constructor change. The timer callback passes the timestamp as an argument to `ComponentLost`, so replacing `DateTimeOffset.UtcNow` with `_timeProvider.GetUtcNow()` captures the `TimeProvider` at construction time.

### R3 — Repository `UpdatedAt`/`AcknowledgedAt` timestamps (I4–I6)

Two approaches:
- **Option A (preferred)**: Inject `TimeProvider` and capture `_timeProvider.GetUtcNow()` once per method call.
- **Option B (acceptable)**: Pass the timestamp in from the Application layer (caller already has `now`). For `AcknowledgeBySystemAsync`, the handler at line 70 already holds `now`; exposing it as a method parameter is cleaner and removes the repository's time dependency entirely.

### R4 — Fix indentation in `GetAlertsQueryHandler.cs`

2-space → 4-space to match project style.

---

## 📝 Implementation Example

### Before (C3 — `OverrideComponentStateHandler.cs`)

```csharp
// No TimeProvider in constructor
public OverrideComponentStateHandler(
    IMonitoredComponentRepository componentRepository,
    IComponentStateRepository stateRepository,
    IComponentStateCache stateCache,
    IDomainEventDispatcher eventDispatcher,
    IAuditLogger auditLogger,
    IRealtimeNotificationService realtimeNotification,
    ILogger<OverrideComponentStateHandler> logger)
{ ... }

// Direct static call
var now = DateTimeOffset.UtcNow;
```

### After (C3 — `OverrideComponentStateHandler.cs`)

```csharp
private readonly TimeProvider _timeProvider;

public OverrideComponentStateHandler(
    IMonitoredComponentRepository componentRepository,
    IComponentStateRepository stateRepository,
    IComponentStateCache stateCache,
    IDomainEventDispatcher eventDispatcher,
    IAuditLogger auditLogger,
    IRealtimeNotificationService realtimeNotification,
    TimeProvider timeProvider,
    ILogger<OverrideComponentStateHandler> logger)
{
    ...
    _timeProvider = timeProvider;
}

// Consistent, testable
var now = _timeProvider.GetUtcNow();
```

---

### Before (I3 — `HeartbeatTimeoutMonitor.cs`)

```csharp
// No TimeProvider in constructor
public HeartbeatTimeoutMonitor(
    IServiceScopeFactory scopeFactory,
    ILogger<HeartbeatTimeoutMonitor> logger) { ... }

// In timer callback
var @event = new ComponentLost(entry.ComponentId, entry.SystemId, DateTimeOffset.UtcNow);
```

### After (I3 — `HeartbeatTimeoutMonitor.cs`)

```csharp
private readonly TimeProvider _timeProvider;

public HeartbeatTimeoutMonitor(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<HeartbeatTimeoutMonitor> logger)
{
    _scopeFactory = scopeFactory;
    _timeProvider = timeProvider;
    _logger = logger;
}

// In timer callback — captures injected provider
var @event = new ComponentLost(entry.ComponentId, entry.SystemId, _timeProvider.GetUtcNow());
```

---

### Before (I4/Option B — `AcknowledgeAlertHandler` + `AlertRecordRepository`)

```csharp
// Handler
var now = DateTimeOffset.UtcNow;
await _alertRepository.AcknowledgeBySystemAsync(command.SystemId, command.OperatorName, ct);

// Repository — silent static call
Now = DateTimeOffset.UtcNow.ToString("O"),
```

### After (I4/Option B — pass timestamp as parameter)

```csharp
// Handler — already has 'now' from TimeProvider
await _alertRepository.AcknowledgeBySystemAsync(
    command.SystemId, command.OperatorName, now, ct);

// Repository — receives the timestamp from caller
public async Task AcknowledgeBySystemAsync(
    string systemId, string operatorName, DateTimeOffset acknowledgedAt, CancellationToken ct)
{
    ...
    Now = acknowledgedAt.ToString("O"),
}
```
