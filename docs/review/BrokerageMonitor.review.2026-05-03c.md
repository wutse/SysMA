# BrokerageMonitor — Architecture Review (2026-05-03c)

> **Reviewer**: Chief Software Architect
> **Date**: 2026-05-03
> **Scope**: Full-solution re-review following `TimeProvider` remediation (C3–C8, I3–I6)
> **Branch**: `refactor` (commit `404cafc`)
> **Test Run**: 608 / 608 passed

---

## 📊 Architecture Health Score

| Project                             | Score    | Δ         | Notes                                          |
| ----------------------------------- | -------- | --------- | ---------------------------------------------- |
| **BrokerageMonitor.Domain**         | 9.5 / 10 | ↓ 0.2     | N2 advisory added (domain fallback UtcNow)     |
| **BrokerageMonitor.Application**    | 9.5 / 10 | ↑ 0.5     | C3–C8 all resolved; only N1 style gap remains  |
| **BrokerageMonitor.Infrastructure** | 9.8 / 10 | ↑ 0.8     | I3–I6 all resolved; no new violations found    |
| **BrokerageMonitor.Web**            | 9.5 / 10 | —         | No change; W1 style + A8 bunit advisory remain |
| **BrokerageMonitor.MailAgent**      | 9.0 / 10 | —         | Not re-reviewed this session                   |
| **Overall Solution**                | 9.5 / 10 | ↑ 0.3     |                                                |

---

## ✅ Architectural Strengths

### 1. Clean Architecture Boundaries — Fully Enforced
All five projects respect the dependency rule. Application references only Domain; Infrastructure references only Domain + Application interfaces; Web/MailAgent reference Application only. Zero illegal references found.

### 2. TimeProvider Pattern — Comprehensively Applied (C3–C8 / I3–I6 Resolved)
The `TimeProvider` pattern is now consistent across all Application handlers and Infrastructure repositories:

| Class | Before | After |
|---|---|---|
| `OverrideComponentStateHandler` | `DateTimeOffset.UtcNow` | `_timeProvider.GetUtcNow()` |
| `AcknowledgeAlertHandler` | `DateTimeOffset.UtcNow` | `_timeProvider.GetUtcNow()` |
| `GetAlertsQueryHandler` | `DateTimeOffset.UtcNow` (×2) | `_timeProvider.GetUtcNow()` |
| `ToggleMaintenanceModeHandler` | `DateTimeOffset.UtcNow` | `_timeProvider.GetUtcNow()` |
| `MailChannelProcessor` | `DateTimeOffset.UtcNow` | `_timeProvider.GetUtcNow()` |
| `AlertEvaluationService` | `DateTimeOffset.UtcNow` | `_timeProvider.GetUtcNow()` |
| `HeartbeatTimeoutMonitor` | `DateTimeOffset.UtcNow` | `_timeProvider.GetUtcNow()` |
| `AlertRecordRepository` | `DateTimeOffset.UtcNow` | Caller-supplied `acknowledgedAt` |
| `MonitoredComponentRepository` | `DateTimeOffset.UtcNow` | `_timeProvider.GetUtcNow()` |
| `MonitoredSystemRepository` | `DateTimeOffset.UtcNow` | `_timeProvider.GetUtcNow()` |

`TimeProvider.System` is registered as a singleton in `ApplicationServiceCollectionExtensions`. All 608 unit tests pass with this pattern in place.

### 3. Repository Pattern — Correct Ownership Model
`AlertRecordRepository.AcknowledgeBySystemAsync` now accepts `DateTimeOffset acknowledgedAt` from the caller — this is the architecturally correct approach (Option B). The Application handler owns the "now" timestamp via `TimeProvider`, and passes it down; the repository does not need its own clock. The `IAlertRecordRepository` interface, the implementation, and all test stubs are in sync.

### 4. DI Registration — No Constructor Gaps
All new `TimeProvider` parameters are resolved automatically by the DI container. `TryAddSingleton(TimeProvider.System)` is called before any scoped service registration, so all handlers and repositories resolve correctly. No manual factory registrations were needed.

### 5. Domain Integrity — Rich Aggregates
All six Aggregate Roots (`AlertRecord`, `ComponentState`, `DailyExecution`, `HealthMonitorDefinition`, `MonitoredComponent`, `MonitoredSystem`) enforce invariants at construction. Business rules (`BI-007`, `BI-009`, `BI-013`, `BI-014`) are co-located with state, not leaked into Application Services. No "anemic domain model" anti-patterns found.

### 6. Async/Await — Uniformly Correct
No `.Result` or `.Wait()` blocking calls found across all 32 Infrastructure source files and 67 Application source files. All hosted services, ZeroMQ loops, and Quartz jobs use proper `async`/`await` with `CancellationToken` threading.

### 7. Error Handling — Appropriate by Layer
- Infrastructure notification errors (`SignalRNotificationService`, `SmtpEmailNotificationService`, `TeamsNotificationService`) are swallowed at the boundary and logged — they must not propagate into Domain logic. ✓
- ZeroMQ subscriber uses exponential backoff (1 s → 60 s) without crashing the host. ✓
- Quartz jobs catch and log exceptions rather than rethrowing (where applicable), preventing scheduler disruption. ✓

### 8. N+1 Elimination in Dashboard
`GetDashboardQueryHandler` loads all systems, all active components, and all alert-flagged system IDs in three parallel-eligible queries, then groups in memory. No N+1 pattern.

---

## ⚠️ Critical Violations

**None.** All critical violations from previous reviews (C1–C8, I1–I6) have been resolved.

---

## 💡 Refactoring Suggestions

### N1 — Style: `GetAlertsQueryHandler.cs` 2-Space Indentation (Application)
**Severity**: Cosmetic
**Location**: `src/BrokerageMonitor.Application/UseCases/Alerts/GetAlertsQueryHandler.cs`

The formatter applied 2-space indentation to class-level members, while the rest of the codebase uses 4-space. This is an inconsistency introduced by the formatter tool.

```csharp
// Current (formatter artifact — 2-space)
public sealed class GetAlertsQueryHandler
{
  private readonly IAlertRecordRepository _alertRepo;
  private readonly TimeProvider _timeProvider;

// Expected (4-space — consistent with rest of codebase)
public sealed class GetAlertsQueryHandler
{
    private readonly IAlertRecordRepository _alertRepo;
    private readonly TimeProvider _timeProvider;
```

### N2 — Advisory: Domain Aggregate `DateTimeOffset.UtcNow` Fallback Defaults (Domain + Application)
**Severity**: Low / Advisory
**Locations**:
- `src/BrokerageMonitor.Domain/Aggregates/ComponentState.cs:54` — `LastStatusChangedAt = changedAt ?? DateTimeOffset.UtcNow`
- `src/BrokerageMonitor.Domain/Aggregates/DailyExecution.cs:66` — `CreatedAt = createdAt ?? DateTimeOffset.UtcNow`

Both aggregate constructors accept an optional timestamp parameter with `DateTimeOffset.UtcNow` as the fallback. The `HeartbeatProcessor` creates a new `ComponentState` without passing `message.Timestamp`:

```csharp
// Current (HeartbeatProcessor.cs)
var currentState = _stateCache.GetState(message.ComponentId)
    ?? new ComponentState(message.ComponentId, ComponentStatus.Unknown);
    //                                         ↑ no changedAt → falls back to DateTimeOffset.UtcNow
```

**Practical impact is low**: in the typical first-heartbeat flow, `statusChanged` will be `true` (Unknown → new status), so `UpdateStatus(rolledUpStatus, message.Timestamp)` immediately overwrites `LastStatusChangedAt`. However, it breaks the testability contract. The proper fix is to always pass the timestamp from the caller:

```csharp
// Preferred (Application caller owns the clock via message.Timestamp or TimeProvider)
var currentState = _stateCache.GetState(message.ComponentId)
    ?? new ComponentState(message.ComponentId, ComponentStatus.Unknown, message.Timestamp);
```

Similarly, `DailyExecutionCreatorService.CreateExecutionAsync` should pass a `TimeProvider`-sourced timestamp to the `DailyExecution` constructor rather than relying on the domain default.

### Carried-Over Items (Not Reviewed This Session)
- **A6** (Advisory): `MarketSessionWindow.IsWithinSession` — UTC offset convention should be documented in XML remarks.
- **W1** (Style): `SchedulerOptionsValidator.cs` 2-space indentation.
- **A8** (Advisory): Blazor Web layer bunit test coverage (~0.6%).
- **MailAgent STA bridge** (Low): COM Outlook interop blocks on the STA thread; verified safe but non-obvious.

---

## 📝 Implementation Example

### N2 Before vs. After — `HeartbeatProcessor.cs`

```csharp
// ---- BEFORE (Domain fallback UtcNow) ----
var currentState = _stateCache.GetState(message.ComponentId)
    ?? new ComponentState(message.ComponentId, ComponentStatus.Unknown);

// ---- AFTER (explicit timestamp from message — already TimeProvider-sourced) ----
var currentState = _stateCache.GetState(message.ComponentId)
    ?? new ComponentState(message.ComponentId, ComponentStatus.Unknown, message.Timestamp);
```

### N2 Before vs. After — `DailyExecutionCreatorService.cs`

```csharp
// ---- BEFORE ----
var execution = new DailyExecution(
    executionId,
    definition.DefinitionId,
    definition.SystemId,
    date,
    initialStatus,
    missedReason);
// createdAt not passed → DateTimeOffset.UtcNow fallback fires inside Domain constructor

// ---- AFTER ----
var now = _timeProvider.GetUtcNow();
var execution = new DailyExecution(
    executionId,
    definition.DefinitionId,
    definition.SystemId,
    date,
    initialStatus,
    missedReason,
    createdAt: now);  // explicit — no static clock call in Domain
```
