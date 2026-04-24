# BrokerageMonitor — Architecture Review Summary

> **Reviewed**: 2026-04-24 _(previous: 2026-04-22)_
> **Reviewer**: Chief Software Architect
> **Methodology**: Bottom-up dependency order — Domain → Application → Infrastructure → MailAgent → Web

---

## Health Scores

| Project            | Previous | Current  | Delta | Key Remaining Risk                                                          |
| ------------------ | -------- | -------- | ----- | --------------------------------------------------------------------------- |
| **Domain**         | 7.5 / 10 | 7.5 / 10 | —     | Non-deterministic constructors; cron parser fragile                         |
| **Application**    | 7.0 / 10 | 7.0 / 10 | —     | Cache invalidation gap; N+1 dashboard; `ComponentStateOverridden` not dispatched |
| **Infrastructure** | 6.5 / 10 | 7.0 / 10 | ▲ 0.5 | `SmtpClient` deprecated; fire-and-forget timer                              |
| **MailAgent**      | 7.5 / 10 | 7.5 / 10 | —     | Unbounded email body size; COM instance per call                            |
| **Web**            | 6.0 / 10 | 7.5 / 10 | ▲ 1.5 | Program.cs SRP; dynamic Quartz not re-triggered                             |

---

## ✅ Fixed Since Previous Review

| #  | Where                       | What                                                                                                                      |
| -- | --------------------------- | ------------------------------------------------------------------------------------------------------------------------- |
| F1 | **Web / Program.cs**        | `AddZeroMq(builder.Configuration)` is now called — heartbeat pipeline fully operational.                                 |
| F2 | **Web + Infrastructure**    | Concrete `AuditLogger` registered via `AddPersistence()`. `TryAddScoped` pattern ensures stub loses to real impl.        |
| F3 | **Infrastructure / Domain** | `DailyExecution.Rehydrate()` static factory method added; `DailyExecutionRepository` no longer uses reflection.          |
| F4 | **MailAgent**               | `OutlookMailReader` refactored to dedicated long-lived STA thread with `BlockingCollection` — COM instability resolved.   |

---

## 🔴 Critical Violations — Fix Immediately

| #  | Where                                         | What                                                                                                                                                                                                         |
| -- | --------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| 1  | **Application / DomainEventDispatcher**       | `ComponentStateOverridden` event falls through to `default: break` — `AlertEvaluationService` is never called on manual state overrides. Active alerts not cleared; no new alerts raised.                   |
| 2  | **Application / Maintenance + StateOverride** | `ToggleMaintenanceModeHandler` and `OverrideComponentStateHandler` write to `IComponentStateRepository` but do not call `_stateCache.SetState()` — dashboard shows stale status after every operator action. |

---

## 🟡 Architectural Violations (Open)

| #  | Where                     | What                                                                                                                                                                              |
| -- | ------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 3  | **Application**           | `GetDashboardQueryHandler` issues one `GetBySystemIdAsync` + one `HasUnacknowledgedAlertAsync` per system inside a `foreach` — 2N+1 queries on every live dashboard refresh.      |
| 4  | **Application**           | `DomainEventDispatcher` hardcodes `PreviousStatus = Unknown` in synthetic `ComponentLost` events — audit entries record the wrong previous state.                                 |
| 5  | **Application**           | `FinalizeExecutionAsync` sends success notifications unconditionally even when `SendOnFailure = false` — confusing flag semantics.                                                |
| 6  | **Application**           | `AppSettingsImporter` calls `TimeOnly.Parse()` without `CultureInfo.InvariantCulture` — locale-sensitive failure risk.                                                           |
| 7  | **Domain**                | `ComponentState` constructor uses `DateTimeOffset.UtcNow` internally — non-deterministic. `DailyExecution` constructor also uses `UtcNow` (mitigated by `Rehydrate()` for DB).   |
| 8  | **Domain**                | All value objects (`EmailAddress`, `MarketSessionWindow`, etc.) override `Equals(object?)` but do not implement `IEquatable<T>` — boxing in generic collections.                 |
| 9  | **Domain**                | `AlertRecord.Acknowledge()` allows silent overwrite — original acknowledging operator can be replaced without error.                                                              |
| 10 | **Domain**                | `HealthRuleSchedule.MatchesCron()` has `int.Parse` without try-parse for step range-start, no step-divisor-zero guard, and only evaluates 3 of 5 cron fields.                    |
| 11 | **Domain / Repositories** | `AuditLogEntry` and `ExecutionHistoryEntry` are read-models co-located in the `Repositories/` folder — should be in `ReadModels/`.                                               |
| 12 | **Infrastructure**        | `SmtpClient` is deprecated in .NET 6+. Should migrate to `MailKit`.                                                                                                              |
| 13 | **Infrastructure**        | `HeartbeatTimeoutMonitor.OnTimerFired` discards the async task (`_ = ...`) with `CancellationToken.None` — shutdown not respected; faults may be silently swallowed.              |
| 14 | **Web**                   | `AggregateHealthEvaluationJob` is only scheduled at startup — new `HealthMonitorDefinition`s created at runtime are never evaluated until restart.                                |
| 15 | **Web**                   | `Program.cs` violates SRP — 80+ lines of orchestration logic should be delegated to dedicated startup services.                                                                  |
| 16 | **MailAgent**             | Email body published to ZeroMQ without any size cap — multi-MB newsletters cause memory pressure on both processes.                                                               |
| 17 | **MailAgent**             | `ReadUnreadMailsOnSta()` still creates a new `MSOutlook.Application` + `Logon/Logoff` per poll call (on STA thread — less severe, but still wasteful).                            |

---

## Review Documents

| File                                                                                   | Score    |
| -------------------------------------------------------------------------------------- | -------- |
| [BrokerageMonitor.Domain.review.md](BrokerageMonitor.Domain.review.md)                 | 7.5 / 10 |
| [BrokerageMonitor.Application.review.md](BrokerageMonitor.Application.review.md)       | 7.0 / 10 |
| [BrokerageMonitor.Infrastructure.review.md](BrokerageMonitor.Infrastructure.review.md) | 7.0 / 10 |
| [BrokerageMonitor.MailAgent.review.md](BrokerageMonitor.MailAgent.review.md)           | 7.5 / 10 |
| [BrokerageMonitor.Web.review.md](BrokerageMonitor.Web.review.md)                       | 7.5 / 10 |