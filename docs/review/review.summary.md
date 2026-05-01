# BrokerageMonitor — Architecture Review Summary

> **Reviewed**: 2026-05-01 _(fourth pass — refactor compliance check)_
> **Previous**: 2026-05-01 (third pass — deep re-review)
> **Reviewer**: Chief Software Architect
> **Methodology**: Bottom-up dependency order — Domain → Application → Infrastructure → MailAgent → Web

---

## Health Scores

| Project            | Previous | Current  | Delta | Key Remaining Risk                                                                              |
| ------------------ | -------- | -------- | ----- | ----------------------------------------------------------------------------------------------- |
| **Domain**         | 8.0 / 10 | 9.5 / 10 | ▲ 1.5 | Implicit UTC assumptions in `MarketSessionWindow` and `DailyExecutionCreatorService` (advisory) |
| **Application**    | 8.5 / 10 | 9.0 / 10 | ▲ 0.5 | `GetSystemsWithComponentsQueryHandler` still N+1 (LOW — non-hot-path)                           |
| **Infrastructure** | 9.0 / 10 | 9.5 / 10 | ▲ 0.5 | `IDbConnection` open convention inconsistent (LOW); `ApplyMigrationsAsync` sync naming (LOW)    |
| **MailAgent**      | 9.0 / 10 | 9.0 / 10 | —     | No open violations; all fixes confirmed                                                         |
| **Web**            | 7.5 / 10 | 9.5 / 10 | ▲ 2.0 | All 8 repository injections replaced; Dependency Rule now fully respected                       |

---

## ✅ Fixed Since Previous Review (2026-05-01 third pass)

| #   | Where                                                      | What                                                                                                                                                                          |
| --- | ---------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| F1  | **Domain / `IMonitoredSystemRepository`**                  | `SetMaintenanceModeAsync` removed from interface and infrastructure implementation. All maintenance-mode transitions now go through load → `ActivateMaintenance` → upsert.    |
| F2  | **Domain / `DailyExecution.Complete()`**                   | `failedComponents.Distinct(StringComparer.OrdinalIgnoreCase)` applied before `AddRange`, matching the guard in `AddCompletedComponent()`.                                     |
| F3  | **Domain / `MailParsingRule.GetHashCode()`**               | `SuccessKeywords` and `FailureKeywords` folded into `HashCode` with `OrdinalIgnoreCase`, eliminating O(n) bucket degradation in `Dictionary`/`HashSet`.                       |
| F4  | **Domain / `IAlertRecordRepository`**                      | `AcknowledgeBySystemAsync` XML doc now documents the `WHERE AcknowledgedAt IS NULL` constraint so implementations cannot silently omit it.                                    |
| F5  | **Application / `AggregateHealthEvaluationService`**       | `GetAllActiveAsync()` hot-path replaced with `GetByWatchedComponentAsync(evt.ComponentId)` — targeted JOIN, no full table scan per heartbeat.                                 |
| F6  | **Application / `ApplicationServiceCollectionExtensions`** | `NullAggregateHealthEvaluationService` orphaned stub deleted.                                                                                                                 |
| F7  | **Application (new)**                                      | 4 new Application query handlers added: `GetAlertsQueryHandler`, `GetActiveSystemIdsQueryHandler`, `GetActiveComponentsQueryHandler`, `GetSystemsWithComponentsQueryHandler`. |
| F8  | **Infrastructure / `HealthMonitorDefinitionRepository`**   | `SendOnFailure` column renamed `NotificationsEnabled` in DDL, `DefinitionRow`, and all SQL strings. Idempotent `ApplyMigrationsAsync` migration applied on startup.           |
| F9  | **Infrastructure / `AddRealtimeNotifications()`**          | XML doc comment no longer falsely claims it registers `IMonitorBroadcaster`.                                                                                                  |
| F10 | **Infrastructure / `GetByWatchedComponentAsync`**          | Implementation added in `HealthMonitorDefinitionRepository` using a junction-table INNER JOIN.                                                                                |
| F11 | **Web / All 5 pages**                                      | All 8 `@inject Repository` directives removed; pages now depend on Application-layer query handlers only. All `@using BrokerageMonitor.Domain.Repositories` removed.          |
| F12 | **Web / `DashboardPage.razor`**                            | Orphaned `@inject IMonitoredSystemRepository SystemRepository` directive removed.                                                                                             |

---

## 🟡 Remaining Violations (Open)

| #   | Where              | Severity | What                                                                                                                                                                                                                            |
| --- | ------------------ | -------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1   | **Application**    | LOW      | `GetSystemsWithComponentsQueryHandler.HandleAsync()` executes a `foreach` + `GetBySystemIdAsync` loop (N+1). Not a hot path, but architecturally inconsistent. Fix: `GetAllActiveAsync()` + in-memory `GroupBy`.                |
| 2   | **Infrastructure** | LOW      | `IDbConnection` open convention inconsistent — `HealthMonitorDefinitionRepository` calls `conn.Open()` explicitly; others rely on Dapper's lazy-open. Document or standardize.                                                  |
| 3   | **Infrastructure** | LOW      | `ApplyMigrationsAsync` executes synchronously but carries the `Async` suffix and returns `Task.CompletedTask`. Convert to true `async Task` using `ExecuteReaderAsync`/`ExecuteNonQueryAsync`, or rename to drop the suffix.    |
| 4   | **Domain**         | ADVISORY | Implicit UTC assumptions in `MarketSessionWindow.IsWithinSession()` and `DailyExecutionCreatorService` (`DateTime.Today`). Low operational risk; testability gap; recommend `TimeProvider` abstraction for the creator service. |

---

## 🔵 New Application Handlers — Style Note

The four new query handlers added this cycle use **2-space indentation** instead of the codebase-standard **4-space**. Minor style inconsistency; should be corrected for consistency.

---

## Review Documents

| File                                                                                   | Score    |
| -------------------------------------------------------------------------------------- | -------- |
| [BrokerageMonitor.Domain.review.md](BrokerageMonitor.Domain.review.md)                 | 9.5 / 10 |
| [BrokerageMonitor.Application.review.md](BrokerageMonitor.Application.review.md)       | 9.0 / 10 |
| [BrokerageMonitor.Infrastructure.review.md](BrokerageMonitor.Infrastructure.review.md) | 9.5 / 10 |
| [BrokerageMonitor.MailAgent.review.md](BrokerageMonitor.MailAgent.review.md)           | 9.0 / 10 |
| [BrokerageMonitor.Web.review.md](BrokerageMonitor.Web.review.md)                       | 9.5 / 10 |

---

## Health Scores

| Project            | Previous | Current  | Delta | Key Remaining Risk                                                                                                                                                       |
| ------------------ | -------- | -------- | ----- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Domain**         | 9.0 / 10 | 8.0 / 10 | ▼ 1.0 | `IMonitoredSystemRepository.SetMaintenanceModeAsync` dual-write path bypasses aggregate invariant (HIGH); `DailyExecution.Complete()` missing idempotency guard (MEDIUM) |
| **Application**    | 8.5 / 10 | 8.5 / 10 | —     | `GetAllActiveAsync` on every `ComponentStatusChanged` (hot N+1); orphaned stub                                                                                           |
| **Infrastructure** | 9.5 / 10 | 9.0 / 10 | ▼ 0.5 | `SendOnFailure` schema column not renamed; stale `AddRealtimeNotifications` doc                                                                                          |
| **MailAgent**      | 9.0 / 10 | 9.0 / 10 | —     | No open violations; all fixes confirmed in source                                                                                                                        |
| **Web**            | 7.5 / 10 | 7.5 / 10 | —     | **8 direct repository injections across 5 pages** — Dependency Rule violated                                                                                             |

---

## ✅ Fixed Since Previous Review (2026-04-25 second pass — for historical reference)

| #   | Where                                        | What                                                                                                                                        |
| --- | -------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------- |
| F1  | **Infrastructure / HeartbeatTimeoutMonitor** | `OnTimerFired` now attaches `.ContinueWith(OnlyOnFaulted)` — exceptions from `RaiseComponentLostAsync` are logged instead of silently lost. |
| F2  | **Infrastructure / AddNotificationServices** | `SmtpEmailNotificationService` registered as `Singleton` — per-email TCP connection allocation eliminated.                                  |
| F3  | **Application / HealthMonitorDefinition**    | `SendOnFailure` renamed to `NotificationsEnabled` in the domain model — semantic ambiguity removed.                                         |
| F4  | **Domain / ComponentState**                  | Public constructor now accepts `DateTimeOffset? changedAt = null` — `LastStatusChangedAt = changedAt ?? UtcNow` gives tests full control.   |
| F5  | **Domain / DailyExecution**                  | Public constructor now accepts `DateTimeOffset? createdAt = null` — `CreatedAt = createdAt ?? UtcNow` gives tests full control.             |