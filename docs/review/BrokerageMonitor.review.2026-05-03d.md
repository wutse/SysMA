# BrokerageMonitor — Architecture Review (2026-05-03d)

> **Reviewer**: Chief Software Architect
> **Date**: 2026-05-03
> **Scope**: Full-solution re-review following 608-test baseline (test-gap closure session)
> **Branch**: `refactor` (HEAD `6a5cf70`)
> **Test Run**: 608 / 608 passed — zero failures, zero compilation errors

---

## 📊 Architecture Health Score

| Project                             | Score    | Δ     | Notes                                                                                               |
| ----------------------------------- | -------- | ----- | --------------------------------------------------------------------------------------------------- |
| **BrokerageMonitor.Domain**         | 9.5 / 10 | —     | N2 still open; no new violations                                                                    |
| **BrokerageMonitor.Application**    | 9.2 / 10 | ↓ 0.3 | N1 still open; **N3 (N+1 read in ToggleMaintenanceModeHandler)** new; **N4 (double GetUtcNow)** new |
| **BrokerageMonitor.Infrastructure** | 9.8 / 10 | —     | No new violations; all I3–I6 remain resolved                                                        |
| **BrokerageMonitor.Web**            | 9.5 / 10 | —     | W1 + A8 advisory remain; no new issues                                                              |
| **BrokerageMonitor.MailAgent**      | 9.0 / 10 | —     | Not re-reviewed this session                                                                        |
| **Overall Solution**                | 9.4 / 10 | ↓ 0.1 | New N3/N4 advisories in Application layer                                                           |

---

## ✅ Architectural Strengths

### 1. All Critical Violations Remain Resolved
All critical violations C1–C8 (Application `TimeProvider`) and I1–I6 (Infrastructure `TimeProvider`) remain resolved since the 2026-05-03b/c sessions. No regressions introduced.

### 2. TimeProvider Pattern — Consistent in New Code
Every new or modified source file correctly uses `_timeProvider.GetUtcNow()`:

| File                               | Pattern                                                             |
| ---------------------------------- | ------------------------------------------------------------------- |
| `AlertEvaluationService`           | `_timeProvider.GetUtcNow()` ✓                                       |
| `MailChannelProcessor`             | `_timeProvider.GetUtcNow()` ✓                                       |
| `StationStartupRecoveryService`    | `_timeProvider.GetUtcNow()` ✓                                       |
| `AcknowledgeAlertHandler`          | `_timeProvider.GetUtcNow()` ✓                                       |
| `ToggleMaintenanceModeHandler`     | `_timeProvider.GetUtcNow()` ✓                                       |
| `OverrideComponentStateHandler`    | `_timeProvider.GetUtcNow()` ✓                                       |
| `GetHealthDefinitionsQueryHandler` | `_timeProvider.GetLocalNow()` ✓ (correct for local date scheduling) |
| `DailyExecutionCreatorJob`         | `_timeProvider.GetLocalNow()` ✓                                     |
| `DataRetentionJob`                 | `_timeProvider.GetUtcNow()` ✓                                       |
| `MonitoredComponentRepository`     | `_timeProvider.GetUtcNow()` ✓                                       |
| `MonitoredSystemRepository`        | `_timeProvider.GetUtcNow()` ✓                                       |
| `HeartbeatTimeoutMonitor`          | `_timeProvider` injected ✓                                          |

### 3. `IAlertRecordRepository` Interface — Caller-Supplied Timestamp
`AcknowledgeBySystemAsync` signature correctly documents that `acknowledgedAt` must be caller-supplied, ensuring the Application layer's `TimeProvider` drives all time values. The interface comment explicitly prohibits implementations from reading the system clock:

```csharp
/// The <paramref name="acknowledgedAt"/> timestamp is supplied by the caller so that the
/// Application layer's <see cref="TimeProvider"/> drives all time values.
Task AcknowledgeBySystemAsync(string systemId, string operatorName,
    DateTimeOffset acknowledgedAt, CancellationToken ct = default);
```

### 4. Quartz Job Safety — `[DisallowConcurrentExecution]`
Both `DailyExecutionCreatorJob` and `DataRetentionJob` are decorated with `[DisallowConcurrentExecution]`. This prevents double-execution if the scheduler fires a second trigger before the first completes — critical for BI-012 (unique daily execution per definition per day).

### 5. IDisposable Management in `HeartbeatTimeoutMonitor`
The monitor correctly uses `await using var scope = _scopeFactory.CreateAsyncScope()` (async disposal), not the sync `using var scope` pattern. All per-component `System.Threading.Timer` instances are disposed on shutdown via `foreach` over `ConcurrentDictionary<string, TimerEntry>.Values`.

### 6. `MailChannelProcessor` — Failure-First Keyword Evaluation (BI-016)
The processor correctly implements the "failure-first" keyword precedence rule: `isFailure` is evaluated before `isSuccess`, so an email that contains both failure and success keywords is always treated as a failure. No ambiguity branch exists.

### 7. `AlertEvaluationService` — Email Failure Fallback to Inbox (US-031)
Email delivery failures are correctly caught at `EmailDeliveryException` boundary; the service writes a `NotificationDeliveryFailed` inbox item and does not rethrow. The sentinel `AlertEmailFailureDefinitionId` (`00000000-0000-0000-0000-000000000001`) satisfies the domain invariant while remaining identifiable for filtering.

### 8. `StationStartupRecoveryService` — FR-033 Compliance
The service correctly transitions `ScheduledJob` components from `Running` → `Warning` on startup, raises `ComponentStatusChanged` for downstream alert evaluation, and persists the new state. Timer registration (`RegisterComponent`) is called for all active components, not just those being transitioned.

### 9. Test Coverage Improvement
608 tests vs. 489 in the 2026-05-01 baseline — a **+24.3% increase**. The following previously-zero-coverage paths are now exercised:

| Area                                  | Previous | Current                       |
| ------------------------------------- | -------- | ----------------------------- |
| Domain events (8 event types)         | 0%       | 100% (constructor + equality) |
| `HealthMonitorDefinitionRepository`   | 26.3%    | Full CRUD covered             |
| `GetHealthDefinitionsQueryHandler`    | 0%       | Fully covered                 |
| `MonitorBroadcaster` exception branch | ~27%     | Exception path covered        |
| `NotificationInboxItem.MarkAsRead`    | Partial  | Idempotency covered           |

---

## ⚠️ Critical Violations

**None.** All critical violations from previous reviews (C1–C8, I1–I6) remain resolved.

---

## 💡 Refactoring Suggestions

### N3 — N+1 Read Pattern in `ToggleMaintenanceModeHandler` (Application) — **NEW**
**Severity**: Medium / Performance
**Location**: `src/BrokerageMonitor.Application/UseCases/Maintenance/ToggleMaintenanceModeHandler.cs`
**FR Reference**: FR-032 / US-033

The handler loads components for the system (1 query), then issues a `GetByComponentIdAsync` call per component (N queries), plus one `UpsertAsync` per component (N queries). For a system with 10 components, a single maintenance toggle fires **21 database round-trips** (1 read + 10 reads + 10 writes).

`IComponentStateRepository.GetByComponentIdsAsync` already exists for exactly this use case. The read loop should be replaced with a single batch call:

```csharp
// Before (N+1 reads)
foreach (var component in components)
{
    var state = await _stateRepository.GetByComponentIdAsync(component.ComponentId, ct)
                ?? new ComponentState(component.ComponentId);
    state.UpdateStatus(targetStatus, now);
    await _stateRepository.UpsertAsync(state, ct);
    _stateCache.SetState(state);
}

// After (1 batch read + N individual upserts — writes remain individual)
var componentIds = components.Select(c => c.ComponentId).ToList();
var existingStates = await _stateRepository.GetByComponentIdsAsync(componentIds, ct);
var stateById = existingStates.ToDictionary(s => s.ComponentId, StringComparer.Ordinal);

foreach (var component in components)
{
    var state = stateById.GetValueOrDefault(component.ComponentId)
                ?? new ComponentState(component.ComponentId);
    state.UpdateStatus(targetStatus, now);
    await _stateRepository.UpsertAsync(state, ct);
    _stateCache.SetState(state);
}
```

This reduces the DB round-trips from **2N+1 to N+2** (1 batch read + N upserts + 1 component list fetch).

---

### N4 — Double `GetUtcNow()` in `GetAlertsQueryHandler` (Application) — **NEW**
**Severity**: Low / Advisory
**Location**: `src/BrokerageMonitor.Application/UseCases/Alerts/GetAlertsQueryHandler.cs`

When both `query.From` and `query.To` are `null`, two separate `GetUtcNow()` calls are issued. Although the practical difference is sub-millisecond, it violates the single-clock-read-per-operation convention, and breaks the testability contract when using `FakeTimeProvider` in scenarios where time is advanced between calls.

```csharp
// Before (2 clock reads)
var from = query.From ?? _timeProvider.GetUtcNow().AddDays(-7);
var to   = query.To   ?? _timeProvider.GetUtcNow();

// After (1 clock read)
var now  = _timeProvider.GetUtcNow();
var from = query.From ?? now.AddDays(-7);
var to   = query.To   ?? now;
```

---

### Carried-Over Items (Not Addressed This Session)

| ID     | Severity | File                                                              | Description                                                                                                    |
| ------ | -------- | ----------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------- |
| **N1** | Cosmetic | `GetAlertsQueryHandler.cs`                                        | 2-space indentation; formatter artifact                                                                        |
| **N2** | Low      | `HeartbeatProcessor.cs:67`, `DailyExecutionCreatorService.cs:109` | Domain aggregate `DateTimeOffset.UtcNow` fallback — callers should pass explicit timestamp from `TimeProvider` |
| **A6** | Advisory | `MarketSessionWindow.IsWithinSession`                             | UTC offset normalization undocumented                                                                          |
| **W1** | Cosmetic | `SchedulerOptionsValidator.cs`                                    | 2-space indentation (intentional formatter style for Web project)                                              |
| **A8** | Advisory | `BrokerageMonitor.Web`                                            | Blazor component bunit coverage ~0.6%                                                                          |

---

## 📝 Implementation Example

### N3 Before vs. After — `ToggleMaintenanceModeHandler.cs`

```csharp
// ---- BEFORE (N+1 reads — GetByComponentIdAsync in loop) ----
foreach (var component in components)
{
    var state = await _stateRepository.GetByComponentIdAsync(component.ComponentId, ct)
                ?? new ComponentState(component.ComponentId);
    state.UpdateStatus(targetStatus, now);
    await _stateRepository.UpsertAsync(state, ct);
    _stateCache.SetState(state);
}

// ---- AFTER (1 batch read via GetByComponentIdsAsync) ----
var componentIds = components.Select(c => c.ComponentId).ToList();
var existing = await _stateRepository.GetByComponentIdsAsync(componentIds, ct);
var stateById = existing.ToDictionary(s => s.ComponentId, StringComparer.Ordinal);

foreach (var component in components)
{
    var state = stateById.GetValueOrDefault(component.ComponentId)
                ?? new ComponentState(component.ComponentId);
    state.UpdateStatus(targetStatus, now);
    await _stateRepository.UpsertAsync(state, ct);
    _stateCache.SetState(state);
}
// Result: 2N+1 → N+2 DB round-trips for a system with N components
```

### N4 Before vs. After — `GetAlertsQueryHandler.cs`

```csharp
// ---- BEFORE (2 GetUtcNow() calls) ----
var from = query.From ?? _timeProvider.GetUtcNow().AddDays(-7);
var to   = query.To   ?? _timeProvider.GetUtcNow();

// ---- AFTER (single clock read) ----
var now  = _timeProvider.GetUtcNow();
var from = query.From ?? now.AddDays(-7);
var to   = query.To   ?? now;
```
