# BrokerageMonitor — Architecture Review Summary

> **Reviewed**: 2026-05-01 _(previous: 2026-04-25)_
> **Reviewer**: Chief Software Architect
> **Methodology**: Bottom-up dependency order — Domain → Application → Infrastructure → MailAgent → Web

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

## ✅ Fixed Since Previous Review (2026-04-25)

| #   | Where                                        | What                                                                                                                                        |
| --- | -------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------- |
| F1  | **Infrastructure / HeartbeatTimeoutMonitor** | `OnTimerFired` now attaches `.ContinueWith(OnlyOnFaulted)` — exceptions from `RaiseComponentLostAsync` are logged instead of silently lost. |
| F2  | **Infrastructure / AddNotificationServices** | `SmtpEmailNotificationService` registered as `Singleton` — per-email TCP connection allocation eliminated.                                  |
| F3  | **Application / HealthMonitorDefinition**    | `SendOnFailure` renamed to `NotificationsEnabled` in the domain model — semantic ambiguity removed.                                         |
| F4  | **Domain / ComponentState**                  | Public constructor now accepts `DateTimeOffset? changedAt = null` — `LastStatusChangedAt = changedAt ?? UtcNow` gives tests full control.   |
| F5  | **Domain / DailyExecution**                  | Public constructor now accepts `DateTimeOffset? createdAt = null` — `CreatedAt = createdAt ?? UtcNow` gives tests full control.             |

---

## 🆕 New Findings This Cycle (2026-05-01)

| #   | Where              | What                                                                                                                                                                                                                                                                                                                                                     |
| --- | ------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| N1  | **Infrastructure** | `SendOnFailure` column in `HealthMonitorDefinitions` schema not renamed after domain property changed to `NotificationsEnabled` — semantic drift between persistence layer and domain model.                                                                                                                                                             |
| N2  | **Infrastructure** | `AddRealtimeNotifications()` XML doc comment falsely claims it registers `IMonitorBroadcaster` — stale documentation.                                                                                                                                                                                                                                    |
| N3  | **Application**    | `NullAggregateHealthEvaluationService` is defined but never registered in DI and never referenced in tests — orphaned dead-code stub.                                                                                                                                                                                                                    |
| N4  | **MailAgent**      | 2026-04-25 `EnsureOutlookSession()` fix **confirmed in source** — `_outlookApp` / `_outlookNs` are long-lived; `Logon` is called once per process lifetime. `TruncateBody()` cap also confirmed. MailAgent review date updated to 2026-05-01.                                                                                                            |
| N5  | **Domain**         | `IMonitoredSystemRepository.SetMaintenanceModeAsync(systemId, active)` exposes a second mutation path that bypasses `ActivateMaintenance` / `DeactivateMaintenance` — BI-009 operator invariant not enforced, `MaintenanceOperator` left inconsistent, audit trail broken. **Remove the method; all callers must use load → mutate → upsert.**           |
| N6  | **Domain**         | `DailyExecution.Complete()` calls `_failedComponents.AddRange(failedComponents)` with no deduplication guard, while `AddCompletedComponent()` already applies one. Duplicate IDs produce incorrect failure counts and misleading notification content. Fix: `failedComponents.Distinct(StringComparer.OrdinalIgnoreCase)`.                               |
| N7  | **Domain**         | `MailParsingRule.GetHashCode()` includes only `FromPattern` + `SubjectPattern`, while `Equals()` also compares `SuccessKeywords` + `FailureKeywords`. Maximises hash collision probability for the primary differentiating fields; O(n) bucket degradation in `Dictionary` / `HashSet`. Fix: fold keyword lists into `HashCode` via `OrdinalIgnoreCase`. |
| N8  | **Domain**         | `IAlertRecordRepository.AcknowledgeBySystemAsync` deliberately bypasses `AlertRecord.Acknowledge()` for bulk performance. Trade-off accepted; repository interface must encode the `WHERE AcknowledgedAt IS NULL` constraint in its XML doc comment so future implementations cannot silently omit it.                                                   |

---

## 🔴 Outstanding Critical Violation (Unchanged)

| #   | Where   | What                                                                                                                                                                                                                                                                                                                                                                                              |
| --- | ------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| C1  | **Web** | **Direct Domain repository injection in Blazor pages** — 8 `@inject` directives across 5 pages (`DashboardPage`, `AlertCenterPage`, `HealthDefinitionEditorPage`, `HealthManagementPage`, `HistoryPage`, `SystemManagementPage`) bypass the Application layer entirely, violating the Dependency Rule. Each repository usage must be replaced with a use-case handler or a new Application query. |

---

## 🟡 Remaining Violations (Open)

| #   | Where              | What                                                                                                                                                                                                                                                                                                                                              |
| --- | ------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| D1  | **Domain**         | `IMonitoredSystemRepository.SetMaintenanceModeAsync` — remove the method. All maintenance-mode transitions must follow load → `ActivateMaintenance` / `DeactivateMaintenance` → `UpsertAsync` so the aggregate enforces BI-009.                                                                                                                   |
| D2  | **Domain**         | `DailyExecution.Complete()` — apply `Distinct(StringComparer.OrdinalIgnoreCase)` to `failedComponents` before `AddRange`, matching the guard already present in `AddCompletedComponent()`.                                                                                                                                                        |
| 1   | **Application**    | `UpdateComponentProgressAsync` calls `GetAllActiveAsync()` for every `ComponentStatusChanged` event — full table scan on every heartbeat. Add `GetByWatchedComponentAsync(componentId)` to `IHealthMonitorDefinitionRepository`.                                                                                                                  |
| 2   | **Application**    | `NullAggregateHealthEvaluationService` is defined in `ApplicationServiceCollectionExtensions.cs` but never registered in DI and never referenced in any test project — orphaned dead-code stub; delete it.                                                                                                                                        |
| 3   | **Infrastructure** | `IDbConnection` open convention is inconsistent — `HealthMonitorDefinitionRepository` calls `conn.Open()` explicitly; other repositories rely on Dapper's lazy-open. Document or standardize.                                                                                                                                                     |
| 4   | **Infrastructure** | `SendOnFailure` schema column in `HealthMonitorDefinitions` table was not renamed when the domain property changed to `NotificationsEnabled`. Rename via `ALTER TABLE … RENAME COLUMN SendOnFailure TO NotificationsEnabled` and update `DefinitionRow`.                                                                                          |
| 5   | **Infrastructure** | `AddRealtimeNotifications()` XML doc comment incorrectly claims it registers `MonitorBroadcaster` as `IMonitorBroadcaster` — the actual registration is in `ApplicationServiceCollectionExtensions`. Remove the false claim from the doc comment.                                                                                                 |
| 6   | **Web**            | Orphaned `@inject IMonitoredSystemRepository SystemRepository` in `DashboardPage.razor` — declared but never referenced anywhere in markup or `@code`; remove the directive.                                                                                                                                                                      |
| C1  | **Web**            | **Direct Domain repository injection in Blazor pages** — 8 `@inject` directives across 5 pages (`DashboardPage`, `AlertCenterPage`, `HealthDefinitionEditorPage`, `HealthManagementPage`, `HistoryPage`, `SystemManagementPage`) bypass the Application layer. Each usage must be routed through an existing or new Application use-case handler. |

---

## Review Documents

| File                                                                                   | Score    |
| -------------------------------------------------------------------------------------- | -------- |
| [BrokerageMonitor.Domain.review.md](BrokerageMonitor.Domain.review.md)                 | 8.0 / 10 |
| [BrokerageMonitor.Application.review.md](BrokerageMonitor.Application.review.md)       | 8.5 / 10 |
| [BrokerageMonitor.Infrastructure.review.md](BrokerageMonitor.Infrastructure.review.md) | 9.0 / 10 |
| [BrokerageMonitor.MailAgent.review.md](BrokerageMonitor.MailAgent.review.md)           | 9.0 / 10 |
| [BrokerageMonitor.Web.review.md](BrokerageMonitor.Web.review.md)                       | 7.5 / 10 |