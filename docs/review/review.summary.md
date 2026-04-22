# BrokerageMonitor — Architecture Review Summary

> **Reviewed**: 2026-04-22
> **Reviewer**: Chief Software Architect
> **Methodology**: Bottom-up dependency order — Domain → Application → Infrastructure → MailAgent → Web

---

## Health Scores

| Project            | Score    | Key Risk                                           |
| ------------------ | -------- | -------------------------------------------------- |
| **Domain**         | 7.5 / 10 | Non-deterministic constructors; custom cron parser |
| **Application**    | 7.0 / 10 | Cache invalidation gap; N+1 dashboard query        |
| **Infrastructure** | 6.5 / 10 | Reflection-based aggregate mutation                |
| **MailAgent**      | 7.5 / 10 | New COM instance per poll cycle                    |
| **Web**            | 6.0 / 10 | ZeroMQ never registered; audit logging dead        |

---

## 🔴 Critical Bugs — Fix Immediately

| #   | Where                                         | What                                                                                                                                                                                                         |
| --- | --------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| 1   | **Web / Program.cs**                          | `AddZeroMq()` is never called — the entire heartbeat pipeline is disconnected. All components remain `Unknown` forever at runtime.                                                                           |
| 2   | **Web + Application**                         | No concrete `IAuditLogger` implementation exists. `NullAuditLogger` silently discards every operator action in production. `IAuditLogRepository` and its SQLite table are fully implemented but never wired. |
| 3   | **Infrastructure / DailyExecutionRepository** | `MapToDomain` uses `BindingFlags.NonPublic` reflection to forcibly set private aggregate properties and append to private backing lists — breaks silently on any rename.                                     |

---

## 🟡 Architectural Violations

| #   | Where                     | What                                                                                                                                                                                         |
| --- | ------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 4   | **Application**           | `ToggleMaintenanceModeHandler` and `OverrideComponentStateHandler` write to SQLite but do not update `IComponentStateCache` — dashboard shows stale component status after operator actions. |
| 5   | **Application**           | `DomainEventDispatcher` hardcodes `PreviousStatus = Unknown` in synthetic `ComponentLost` events — all `ComponentLost` audit entries record the wrong previous state.                        |
| 6   | **Application**           | `GetDashboardQueryHandler` issues one `GetBySystemIdAsync` call per system inside a `foreach` — N+1 queries on every live dashboard refresh.                                                 |
| 7   | **Application**           | `FinalizeExecutionAsync` sends success notifications unconditionally even when `SendOnFailure = false` — confusing flag semantics.                                                           |
| 8   | **Application**           | `AppSettingsImporter` calls `TimeOnly.Parse()` without `CultureInfo.InvariantCulture` — locale-sensitive failure risk.                                                                       |
| 9   | **Domain**                | `DailyExecution` and `ComponentState` constructors use `DateTimeOffset.UtcNow` internally — non-deterministic, untestable, and the root cause of the Infrastructure reflection hack.         |
| 10  | **Domain**                | All value objects (`EmailAddress`, `MarketSessionWindow`, etc.) override `Equals(object?)` but do not implement `IEquatable<T>` — boxing in generic collections.                             |
| 11  | **Domain**                | `AlertRecord.Acknowledge()` allows silent overwrite — original acknowledging operator can be replaced without error.                                                                         |
| 12  | **Domain**                | `HealthRuleSchedule.MatchesCron()` home-built parser has `int.Parse` without try-parse, no step-divisor zero guard, and only evaluates 3 of 5 cron fields.                                   |
| 13  | **Domain / Repositories** | `AuditLogEntry` and `ExecutionHistoryEntry` are read-models co-located in the `Repositories/` folder — should be in `ReadModels/`.                                                           |
| 14  | **Infrastructure**        | `SmtpClient` is deprecated in .NET 6+. Should migrate to `MailKit`.                                                                                                                          |
| 15  | **Infrastructure**        | `HeartbeatTimeoutMonitor.OnTimerFired` discards the async task (`_ = ...`) with `CancellationToken.None` — shutdown is not respected; faults are silently swallowed.                         |
| 16  | **Web**                   | `AggregateHealthEvaluationJob` is only scheduled at startup — new `HealthMonitorDefinition`s created at runtime are never evaluated until restart.                                           |
| 17  | **MailAgent**             | `OutlookMailReader` creates a new `MSOutlook.Application` + `Logon/Logoff` on every poll cycle — expensive and causes COM instability under load.                                            |
| 18  | **MailAgent**             | `ReadUnreadMails()` uses `.GetAwaiter().GetResult()` — sync-over-async pattern; cancellation cannot propagate.                                                                               |
| 19  | **MailAgent**             | Email body is published to ZeroMQ without any size cap — multi-MB newsletters will cause memory pressure on both processes.                                                                  |

---

## Review Documents

| File                                                                                   | Score    |
| -------------------------------------------------------------------------------------- | -------- |
| [BrokerageMonitor.Domain.review.md](BrokerageMonitor.Domain.review.md)                 | 7.5 / 10 |
| [BrokerageMonitor.Application.review.md](BrokerageMonitor.Application.review.md)       | 7.0 / 10 |
| [BrokerageMonitor.Infrastructure.review.md](BrokerageMonitor.Infrastructure.review.md) | 6.5 / 10 |
| [BrokerageMonitor.MailAgent.review.md](BrokerageMonitor.MailAgent.review.md)           | 7.5 / 10 |
| [BrokerageMonitor.Web.review.md](BrokerageMonitor.Web.review.md)                       | 6.0 / 10 |
