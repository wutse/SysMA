# BrokerageMonitor — Architecture Review Summary

> **Reviewed**: 2026-05-01 _(previous: 2026-04-25)_
> **Reviewer**: Chief Software Architect
> **Methodology**: Bottom-up dependency order — Domain → Application → Infrastructure → MailAgent → Web

---

## Health Scores

| Project            | Previous | Current  | Delta | Key Remaining Risk                                                           |
| ------------------ | -------- | -------- | ----- | ---------------------------------------------------------------------------- |
| **Domain**         | 8.5 / 10 | 9.0 / 10 | ▲ 0.5 | Local-time `DateTime.Today` in scheduling path (time zone edge case)         |
| **Application**    | 8.5 / 10 | 8.5 / 10 | —     | `GetAllActiveAsync` called on every `ComponentStatusChanged` event (hot N+1) |
| **Infrastructure** | 8.0 / 10 | 9.5 / 10 | ▲ 1.5 | Inconsistent `IDbConnection` open convention across repositories             |
| **MailAgent**      | 9.0 / 10 | 9.0 / 10 | —     | No open violations                                                           |
| **Web**            | 9.0 / 10 | 7.5 / 10 | ▼ 1.5 | **8 direct repository injections across 5 pages** — Dependency Rule violated |

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

## 🔴 New Critical Violation

| #   | Where   | What                                                                                                                                                                                                                                                                                                                                                                                              |
| --- | ------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| C1  | **Web** | **Direct Domain repository injection in Blazor pages** — 8 `@inject` directives across 5 pages (`DashboardPage`, `AlertCenterPage`, `HealthDefinitionEditorPage`, `HealthManagementPage`, `HistoryPage`, `SystemManagementPage`) bypass the Application layer entirely, violating the Dependency Rule. Each repository usage must be replaced with a use-case handler or a new Application query. |

---

## 🟡 Remaining Violations (Open)

| #   | Where              | What                                                                                                                                                                                                                             |
| --- | ------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1   | **Application**    | `UpdateComponentProgressAsync` calls `GetAllActiveAsync()` for every `ComponentStatusChanged` event — full table scan on every heartbeat. Add `GetByWatchedComponentAsync(componentId)` to `IHealthMonitorDefinitionRepository`. |
| 2   | **Infrastructure** | `IDbConnection` open convention is inconsistent — some repositories call `.Open()` explicitly, others rely on Dapper's lazy-open. Document or standardize.                                                                       |
| 3   | **Web**            | Orphaned `@inject IMonitoredSystemRepository SystemRepository` in `DashboardPage.razor` — never referenced; remove the directive.                                                                                                |

---

## Review Documents

| File                                                                                   | Score    |
| -------------------------------------------------------------------------------------- | -------- |
| [BrokerageMonitor.Domain.review.md](BrokerageMonitor.Domain.review.md)                 | 9.0 / 10 |
| [BrokerageMonitor.Application.review.md](BrokerageMonitor.Application.review.md)       | 8.5 / 10 |
| [BrokerageMonitor.Infrastructure.review.md](BrokerageMonitor.Infrastructure.review.md) | 9.5 / 10 |
| [BrokerageMonitor.MailAgent.review.md](BrokerageMonitor.MailAgent.review.md)           | 9.0 / 10 |
| [BrokerageMonitor.Web.review.md](BrokerageMonitor.Web.review.md)                       | 7.5 / 10 |