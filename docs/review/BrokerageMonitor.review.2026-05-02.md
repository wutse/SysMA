# BrokerageMonitor — Complete Architecture Review (Full Pass)

> **Review Date**: 2026-05-02  
> **Reviewer**: Chief Software Architect  
> **Scope**: Full project review against requirements (v2.0) + architecture design (v1.4)  
> **Methodology**: Requirements → Architecture → Domain → Application → Infrastructure → MailAgent → Web → Tests  

---

## 📊 1. Architecture Health Scores

| Project                   | Score    | Trend | Key Risk                                                                |
| ------------------------- | -------- | ----- | ----------------------------------------------------------------------- |
| **Domain**                | 9.5 / 10 | ▶     | UTC/local time advisory in `MarketSessionWindow`                        |
| **Application**           | 9.0 / 10 | ▼ 0.5 | `NotificationsEnabled` semantic drift vs FR-013/FR-014 (MEDIUM)         |
| **Infrastructure**        | 9.5 / 10 | ▶     | Custom Cron parser in `HealthRuleSchedule` (MEDIUM)                     |
| **MailAgent**             | 9.0 / 10 | ▶     | Blocking `GetAwaiter().GetResult()` in STA bridge (ADVISORY)            |
| **Web**                   | 8.5 / 10 | ▼ 0.5 | Scaffolding artifacts `Counter.razor` / `Weather.razor` (LOW)           |
| **Tests**                 | 7.5 / 10 | ▶     | Web coverage 0.6%; custom Cron parser untested at edge cases            |
| **Requirements Coverage** | 9.2 / 10 | ▶     | FR-013 success-notification semantic violated by `NotificationsEnabled` |

---

## ✅ 2. Architectural Strengths

### 2.1 Domain Layer — Exemplary Aggregate Design
- All 6 Aggregate Roots (`MonitoredSystem`, `MonitoredComponent`, `ComponentState`, `AlertRecord`, `HealthMonitorDefinition`, `DailyExecution`) enforce invariants in constructors and mutation methods — no Anemic Domain Model.
- `BI-001` (mandatory override reason), `BI-007` (no duplicate pop-up), `BI-009` (operator name required), `BI-013` (terminal state immutable), `BI-014` (WatchedComponents non-empty) are all enforced as domain-layer exceptions, not application-layer validation.
- `DailyExecution.AddCompletedComponent()` is idempotent; `Complete()` throws on terminal overwrite — correct treatment of BI-013.
- `MailParsingRule` enforces BI-016 (no keyword overlap) in the constructor.
- `Rehydrate()` factory methods on `ComponentState` and `DailyExecution` ensure repositories never auto-assign timestamps — makes persistence deterministic and testable.

### 2.2 Application Layer — CQRS + Proper Event Isolation
- Use Cases are cleanly separated by concern (Alerts, Dashboard, Health, History, Maintenance, Management, StateOverride).
- `DomainEventDispatcher.DispatchAsync` wraps each handler in `SafeInvokeAsync` — one handler failure never cascades to others (US-028).
- `AggregateHealthEvaluationService.UpdateComponentProgressAsync` uses `GetByWatchedComponentAsync(componentId)` (targeted JOIN) instead of full table scan — F5 from prior review is verified in place.
- `StationStartupRecoveryService` correctly implements all FR-033 startup scenarios: state hydration, Running→Warning for ScheduledJob, heartbeat timer arming for Services only.
- `AlertEvaluationService` correctly applies all suppression rules in sequence: maintenance mode (BI-006), market session (FR-030), duplicate pop-up (BI-007).

### 2.3 Infrastructure Layer — Correct Patterns
- SQLite WAL mode is enabled on startup (`DatabaseInitializer`).
- All repositories use `CommandDefinition` with `CancellationToken` — cancellation is propagated throughout.
- `HeartbeatTimeoutMonitor` uses `ConcurrentDictionary<string, TimerEntry>` with proper `Timer.Dispose()` on re-registration — no timer leak.
- `ZeroMQSubscriberService` implements exponential backoff (1s → 2s → … → 60s) with `ERR_ZMQ_DISCONNECTED` log code.
- `AggregateHealthEvaluationJob` is decorated with `[DisallowConcurrentExecution]` — prevents overlapping deadline evaluations.

### 2.4 MailAgent — Correct STA Architecture
- `OutlookMailReader` dedicates a permanent STA background thread for all COM Interop calls — architecturally correct for Outlook COM requirements.
- COM objects (`Application`, `NameSpace`) are created once and reused across polls; `Marshal.ReleaseComObject` is called on folder/items after each scan.
- `MailRelayWorker` is a clean `BackgroundService` with configurable polling interval.

### 2.5 Web Layer — Clean Architecture Compliant (Post-Fix)
- All 5 pages depend exclusively on Application-layer query handlers and command handlers — no direct repository injection.
- `OperatorSessionService` is circuit-scoped (`AddScoped`) — one instance per Blazor Server circuit tab, correctly implementing FR-009.
- SignalR hub (`MonitorHub`) is a thin adapter with no business logic.

---

## ⚠️ 3. Critical & Medium Violations

### 🔴 MEDIUM — `NotificationsEnabled` Semantic Drift vs FR-013 / FR-014

**Location**: `HealthMonitorDefinition.NotificationsEnabled`, `AggregateHealthEvaluationService.FinalizeExecutionAsync`

**Requirement**:
- **FR-013**: When all watched components complete before deadline, a **「系統正常」彙整通知 is ALWAYS sent** (no condition).
- **FR-014**: `SendOnFailure` flag **僅控制（only controls）** the failure notification. `SendOnFailure=false` → don't send failure notification; `SendOnFailure=true` → send failure notification.

**Current Behaviour**: `NotificationsEnabled=false` suppresses **BOTH** success AND failure notifications:

```csharp
// AggregateHealthEvaluationService.cs line 246
if (terminalStatus != DailyExecutionStatus.Exempted && definition.NotificationsEnabled)
{
    notificationSentAt = await SendNotificationsAsync(...);
}
```

A definition with `NotificationsEnabled=false` will never send the success notification, violating FR-013.

**Impact**: Operators who want only to suppress failure-spam notifications (by setting the flag to false) also lose the success confirmation — a notification they explicitly expect (FR-013).

**Fix — Before vs After**:

```csharp
// BEFORE (current — incorrect)
public bool NotificationsEnabled { get; private set; }  // suppresses both success and failure

// AFTER — restore SendOnFailure semantics
public bool SendOnFailure { get; private set; }  // controls ONLY failure notification

// In FinalizeExecutionAsync:
// BEFORE
if (terminalStatus != DailyExecutionStatus.Exempted && definition.NotificationsEnabled)
    notificationSentAt = await SendNotificationsAsync(...);

// AFTER
bool shouldSendExternal = terminalStatus switch
{
    DailyExecutionStatus.Success  => true,                 // FR-013: always send success
    DailyExecutionStatus.Failed   => definition.SendOnFailure, // FR-014: conditional
    _                              => false
};
if (shouldSendExternal)
    notificationSentAt = await SendNotificationsAsync(...);
```

---

### 🟡 MEDIUM — `HealthRuleSchedule` Custom Cron Parser (Infrastructure Risk)

**Location**: `BrokerageMonitor.Domain/ValueObjects/HealthRuleSchedule.cs` `MatchesCron()`

**Issue**: The domain rolls its own 5-field Cron date-matcher. It handles `*`, ranges (`-`), and step expressions (`/`) but does **not** handle:
- `L` (last day of month/week)
- `W` (nearest weekday)  
- `#` (nth weekday of month)
- `?` (no specific value — common in Quartz Cron)

**Impact**: A Quartz job scheduled with `0 30 17 ? * MON-FRI` would fail `IsMatch()` silently because `?` and `MON-FRI` are not supported, causing daily executions not to be created on weekdays.

**Fix**: Delegate Cron-to-date matching to Quartz's `CronExpression` class (already a dependency in Infrastructure), or use `Cronos` NuGet package in Domain. Alternatively, document the supported subset and validate on construction.

---

### 🟡 MEDIUM — `DomainEventDispatcher` Violates OCP (Hardcoded Switch)

**Location**: `BrokerageMonitor.Application/Services/DomainEventDispatcher.cs`

**Issue**: Adding a new domain event requires modifying the dispatcher's `switch` statement — an Open/Closed Principle violation.

```csharp
switch (@event)
{
    case ComponentStatusChanged e: ...
    case ComponentLost e: ...
    case ComponentStateOverridden e: ...
    default: break;  // silently drops all other events
}
```

**Impact**: Low operational risk (all current events are handled), but growth of the domain event catalogue will require repeated modification of this file.

**Fix**: Use a dictionary of `Type → IReadOnlyList<Func<IDomainEvent, CancellationToken, Task>>` handlers registered at startup, or introduce a simple `IDomainEventHandler<TEvent>` interface with DI registration.

---

### 🟡 LOW — Scaffolding Artifacts in Web Project

**Location**: `BrokerageMonitor.Web/Components/Pages/Counter.razor`, `Weather.razor`

These are default Blazor template files with no business value. They register routes (`/counter`, `/weather`) and reference unrelated services.

**Fix**: Delete both files. Verify no `NavLink` in `Layout/NavMenu.razor` references them.

---

### 🟡 LOW — Missing Index on `AlertRecords.SystemId`

**Location**: `DatabaseInitializer.cs` schema DDL

`AlertEvaluationService` calls `HasUnacknowledgedAlertAsync(systemId)` on every alertable status change. This runs a `COUNT(1)` with `WHERE SystemId = @SystemId AND IsGlobalFlagActive = 1`. With no index on `SystemId`, this is a full table scan on every heartbeat-driven alert evaluation.

**Fix** (one DDL line in `SchemaDdl`):
```sql
CREATE INDEX IF NOT EXISTS IX_AlertRecords_SystemId 
ON AlertRecords(SystemId, IsGlobalFlagActive);
```

---

## 💡 4. Refactoring Suggestions

### 4.1 Introduce `TimeProvider` for Testability (ADVISORY)

`DateTime.Today` and `DateTimeOffset.UtcNow` are sprinkled across service classes:

| Location                                                        | Issue                                                |
| --------------------------------------------------------------- | ---------------------------------------------------- |
| `AggregateHealthEvaluationService.UpdateComponentProgressAsync` | `DateOnly.FromDateTime(DateTime.Today)` — local-time |
| `DailyExecutionCreatorService.RecoverTodayAsync`                | `DateTime.Today`, `DateTime.Now` — local-time        |
| `MarketSessionWindow.IsWithinSession(DateTimeOffset)`           | converts via `.TimeOfDay` without timezone info      |

.NET 8 provides `System.TimeProvider` — injecting it as a service would make all time-dependent logic testable without `Thread.Sleep`.

### 4.2 Strengthen `HealthRuleSchedule` Construction Validation

Currently `HealthRuleSchedule` accepts any string as `CronExpression` without parsing it. A malformed expression causes `MatchesCron()` to return `false` silently — no DailyExecution is created and no error is raised.

Add `CronExpression.IsValidExpression(expression)` (Quartz API) in the constructor when `scheduleType == ScheduleType.Cron`.

### 4.3 Expand Test Coverage for `HealthRuleSchedule` Edge Cases

Current tests cover only `Daily` and simple `Weekly` schedules. Add parametrised tests:
- Step expressions: `0/2` (every other day)
- Range expressions: `1-5` (weekdays)  
- Month-scoped Crons: `0 17 31 1,3,5,7,8,10,12 *`
- Invalid expression → constructor exception

### 4.4 Remove Test1.cs Placeholder Files

`BrokerageMonitor.Domain.Tests/Test1.cs`, `Application.Tests/Test1.cs`, and `Infrastructure.Tests/Test1.cs` contain `[Ignore]` placeholder methods that add noise to CI output. They should be deleted entirely.

---

## 📝 5. Requirements Coverage Audit

| FR / BI    | Requirement                                   | Status | Notes                                                                  |
| ---------- | --------------------------------------------- | ------ | ---------------------------------------------------------------------- |
| FR-001     | Worst-case roll-up                            | ✅      | `StateRollupService.GetSeverity()` — severity order matches spec       |
| FR-002     | ZeroMQ XSUB subscription                      | ✅      | `ZeroMQSubscriberService`                                              |
| FR-003     | Heartbeat timeout → Lost                      | ✅      | `HeartbeatTimeoutMonitor` + ScheduledJob Running-only rule             |
| FR-005     | Manual state override                         | ✅      | `OverrideComponentStateHandler`                                        |
| FR-007     | Stopped → no Lost                             | ✅      | `HeartbeatProcessor` skips `ResetTimer` when Stopped                   |
| FR-008     | Override requires reason                      | ✅      | BI-001 enforced in handler                                             |
| FR-009     | Session-based operator name                   | ✅      | Circuit-scoped `OperatorSessionService`                                |
| FR-010     | Alert → Pop-up + Email                        | ✅      | `AlertEvaluationService`                                               |
| FR-011     | No duplicate pop-up (BI-007)                  | ✅      | `HasUnacknowledgedAlertAsync` guard                                    |
| **FR-013** | **Success notification always sent**          | ❌      | **`NotificationsEnabled=false` silently suppresses success**           |
| FR-014     | `SendOnFailure` controls failure notification | ⚠️      | Semantically renamed to `NotificationsEnabled` — incorrect scope       |
| FR-016     | Maintenance → Exempted                        | ✅      | `EvaluateDefinitionAsync` maintenance check                            |
| FR-020     | Inbox always written                          | ✅      | `WriteInboxItemAsync` called regardless of `NotificationsEnabled`      |
| FR-024     | 30-day retention                              | ✅      | `DataRetentionJob` scheduled daily at 01:00                            |
| FR-028     | Per-component heartbeat threshold             | ✅      | `HeartbeatTimeoutSeconds` per component                                |
| FR-030     | Alert only in market session                  | ✅      | `MarketSession.IsWithinSession` guard                                  |
| FR-033     | Startup state recovery                        | ✅      | `StationStartupRecoveryService` all 4 steps                            |
| FR-034     | Event-driven progress tracking                | ✅      | `UpdateComponentProgressAsync`                                         |
| FR-038     | ScheduledJob state machine                    | ✅      | `HeartbeatTimeoutMonitor` Running-only rule                            |
| FR-039     | Sub-indicator rollup                          | ✅      | `ComputeRolledUpStatus()` in `HeartbeatProcessor`                      |
| FR-042     | Daily DailyExecution creation                 | ✅      | `DailyExecutionCreatorJob` at `DailyExecutionCreateTime`               |
| FR-044     | Startup DailyExecution recovery               | ✅      | `RecoverTodayAsync()`                                                  |
| FR-048     | Mail Relay Local Agent                        | ✅      | `MailRelayWorker` + STA `OutlookMailReader`                            |
| FR-050     | Mail channel processing                       | ✅      | `MailChannelProcessor` — failure-first keyword evaluation              |
| FR-051     | Mail channel = same pipeline                  | ✅      | `MailChannelProcessor.ApplyStatusAsync` reuses same state machine      |
| BI-006     | Maintenance suppresses all alerts             | ✅      | `AlertEvaluationService` + `AggregateHealthEvaluationService`          |
| BI-008     | Completion conditions by component type       | ✅      | `HasMetCompletionConditionAtDeadline()`                                |
| BI-010     | Independent recipient lists                   | ✅      | `AlertRecipients` vs `EmailRecipients` are separate                    |
| BI-012     | No duplicate DailyExecution per date          | ✅      | `CreateExecutionAsync` checks existing before insert                   |
| BI-013     | Terminal state immutable                      | ✅      | `DailyExecution.Complete()` throws; DB UPDATE has `NOT IN (...)` guard |
| BI-016     | SuccessKeywords ∩ FailureKeywords = ∅         | ✅      | `MailParsingRule` constructor                                          |

---

## 📊 6. Test Quality Summary

| Project              | Tests   | Passed  | Coverage        |
| -------------------- | ------- | ------- | --------------- |
| Domain.Tests         | 99      | 98      | 81.0%           |
| Application.Tests    | 242     | 241     | 84.6%           |
| Infrastructure.Tests | 148     | 147     | 74.7%           |
| **Total**            | **489** | **486** | **62% overall** |

**Gaps to address**:
1. `HealthRuleSchedule.MatchesCron()` — edge cases (step, range, complex month) not tested
2. `SmtpEmailNotificationService` — no unit tests for alert email subject/body format
3. Web components — 0.6% coverage; Blazor component testing (`bunit`) not yet adopted
4. `DatabaseInitializer` — migration `ApplyMigrations` has no integration test
5. `StationStartupRecoveryService` — partial coverage; no test for the Running→Warning branch with downstream alert evaluation

---

## 🔁 7. Action Items (Priority Order)

| #   | Priority   | Item                                                                                                                 | FR/BI          |
| --- | ---------- | -------------------------------------------------------------------------------------------------------------------- | -------------- |
| A1  | 🔴 HIGH     | Revert `NotificationsEnabled` → `SendOnFailure`; fix `FinalizeExecutionAsync` so success notification is always sent | FR-013, FR-014 |
| A2  | 🟡 MEDIUM   | Replace custom Cron parser in `HealthRuleSchedule` with `Cronos` or Quartz `CronExpression` validation               | FR-042         |
| A3  | 🟡 MEDIUM   | Add `IX_AlertRecords_SystemId` composite index to `DatabaseInitializer` DDL                                          | FR-010         |
| A4  | 🟡 LOW      | Delete `Counter.razor` and `Weather.razor` scaffolding artifacts                                                     | —              |
| A5  | 🟡 LOW      | Refactor `DomainEventDispatcher` to handler-registry pattern (OCP)                                                   | —              |
| A6  | ⚪ ADVISORY | Inject `TimeProvider` into `DailyExecutionCreatorService` and `AggregateHealthEvaluationService`                     | FR-042, FR-044 |
| A7  | ⚪ ADVISORY | Delete `Test1.cs` placeholder files from all 3 test projects                                                         | —              |
| A8  | ⚪ ADVISORY | Add `bunit` for Blazor component testing to reach Web > 30% coverage                                                 | —              |

---

## 🏁 8. Overall Assessment

The BrokerageMonitor codebase demonstrates **strong architectural discipline**. Clean Architecture layer boundaries are correctly respected post-fifth-review; the Domain model is rich and enforces all critical business invariants; the Application layer properly orchestrates use cases without domain logic leakage; and the Infrastructure layer uses Dapper idiomatically without exposing persistence concerns upward.

The single **MEDIUM** violation that breaks a functional requirement (FR-013 success notification suppressed by `NotificationsEnabled=false`) should be addressed before production deployment. The remaining items are quality-of-life improvements rather than correctness blockers.
