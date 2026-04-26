# BrokerageMonitor — Architecture Review Summary

> **Reviewed**: 2026-04-25 _(previous: 2026-04-24)_
> **Reviewer**: Chief Software Architect
> **Methodology**: Bottom-up dependency order — Domain → Application → Infrastructure → MailAgent → Web

---

## Health Scores

| Project            | Previous | Current  | Delta | Key Remaining Risk                                                         |
| ------------------ | -------- | -------- | ----- | -------------------------------------------------------------------------- |
| **Domain**         | 7.5 / 10 | 8.5 / 10 | ▲ 1.0 | Non-deterministic public constructors (`ComponentState`, `DailyExecution`) |
| **Application**    | 7.0 / 10 | 8.5 / 10 | ▲ 1.5 | `SendOnFailure` flag naming ambiguous; minor                               |
| **Infrastructure** | 7.0 / 10 | 8.0 / 10 | ▲ 1.0 | Timer fire-and-forget still swallows exceptions; `Transient` SmtpService   |
| **MailAgent**      | 7.5 / 10 | 9.0 / 10 | ▲ 1.5 | No open violations                                                         |
| **Web**            | 7.5 / 10 | 9.0 / 10 | ▲ 1.5 | No open violations                                                         |

---

## ✅ Fixed Since Previous Review (2026-04-24)

| #   | Where                                         | What                                                                                                                                                                                 |
| --- | --------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| F1  | **Application / DomainEventDispatcher**       | `ComponentStateOverridden` case added — `AlertEvaluationService` is now called on manual state overrides. Active alerts correctly cleared/raised.                                    |
| F2  | **Application / Maintenance + StateOverride** | Both `ToggleMaintenanceModeHandler` and `OverrideComponentStateHandler` now call `_stateCache.SetState()` — dashboard no longer shows stale status.                                  |
| F3  | **Application / Dashboard**                   | `GetDashboardQueryHandler` replaced per-system loop with 3 batch queries (`GetAllActiveAsync`, `GetAllActiveAsync`, `GetSystemsWithUnacknowledgedAlertAsync`).                       |
| F4  | **Application / DomainEventDispatcher**       | `DispatchComponentLostAsync` now reads actual previous status from `_stateCache.GetState()` instead of hardcoding `Unknown`.                                                         |
| F5  | **Application / FinalizeExecution**           | `FinalizeExecutionAsync` now gates notifications behind `definition.SendOnFailure` — success notifications no longer sent when the flag is `false`.                                  |
| F6  | **Application / AppSettingsImporter**         | `TimeOnly.Parse()` now uses `CultureInfo.InvariantCulture` — locale-sensitive failure eliminated.                                                                                    |
| F7  | **Domain / Value Objects**                    | All value objects (`EmailAddress`, `MarketSessionWindow`, `HealthRuleSchedule`, `MailParsingRule`, `MetricValue`, `SubIndicator`, `WatchedComponent`) now implement `IEquatable<T>`. |
| F8  | **Domain / AlertRecord**                      | `AlertRecord.Acknowledge()` now throws `InvalidOperationException` on second call — audit integrity preserved.                                                                       |
| F9  | **Domain / HealthRuleSchedule**               | `MatchesCron()` uses `int.TryParse` (no `FormatException`), step-divisor-zero guard added, and 3-of-5 field intent documented with comment.                                          |
| F10 | **Domain / ReadModels**                       | `AuditLogEntry` and `ExecutionHistoryEntry` moved to `ReadModels/` folder — `Repositories/` contains only interface contracts.                                                       |
| F11 | **Infrastructure / SmtpClient**               | `System.Net.Mail.SmtpClient` replaced with `MailKit` (`SmtpClient` + `MimeMessage`) — obsolete API eliminated.                                                                       |
| F12 | **Infrastructure / HeartbeatTimeoutMonitor**  | `OnTimerFired` now passes `_stoppingToken` (stored from `ExecuteAsync`) to `Task.Run` — graceful shutdown is now respected.                                                          |
| F13 | **Web / Quartz scheduling**                   | `UpsertHealthMonitorDefinitionHandler` calls `_jobScheduler.ScheduleOrRescheduleAsync()` on create/update — new definitions are evaluated without restart.                           |
| F14 | **Web / Program.cs**                          | Startup orchestration extracted to `WebApplicationStartup` — Program.cs is now a clean top-level entry point.                                                                        |
| F15 | **MailAgent / Email body**                    | `MailRelayWorker.TruncateBody()` caps body length at `MaxBodyCharacters` from options — unbounded memory pressure eliminated.                                                        |
| F16 | **MailAgent / COM lifetime**                  | `OutlookMailReader.EnsureOutlookSession()` reuses a long-lived `_outlookApp`/`_outlookNs` pair — one COM instance per process lifetime.                                              |
| F17 | **Domain / ComponentState**                   | `ComponentState.Rehydrate()` static factory added — repositories no longer rely on `UtcNow` for persistence hydration.                                                               |

---

## 🟡 Remaining Violations (Open)

| #   | Where              | What                                                                                                                                                                                          |
| --- | ------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1   | **Domain**         | `DailyExecution` and `ComponentState` public constructors still call `DateTimeOffset.UtcNow` internally — unit tests cannot assert exact `CreatedAt` / `LastStatusChangedAt` values.          |
| 2   | **Infrastructure** | `HeartbeatTimeoutMonitor.OnTimerFired` still discards the `Task.Run` result (`_ = ...`) — exceptions thrown by `RaiseComponentLostAsync` are silently swallowed (not logged).                 |
| 3   | **Infrastructure** | `SmtpEmailNotificationService` is registered as `Transient` — a new connection is allocated per email send; sub-optimal under alert bursts.                                                   |
| 4   | **Application**    | `SendOnFailure` flag name implies "only on failure" but the implementation sends for both `Success` and `Failed` when `true`. Rename to `NotificationsEnabled` to match the actual semantics. |

---

## Review Documents

| File                                                                                   | Score    |
| -------------------------------------------------------------------------------------- | -------- |
| [BrokerageMonitor.Domain.review.md](BrokerageMonitor.Domain.review.md)                 | 8.5 / 10 |
| [BrokerageMonitor.Application.review.md](BrokerageMonitor.Application.review.md)       | 8.5 / 10 |
| [BrokerageMonitor.Infrastructure.review.md](BrokerageMonitor.Infrastructure.review.md) | 8.0 / 10 |
| [BrokerageMonitor.MailAgent.review.md](BrokerageMonitor.MailAgent.review.md)           | 9.0 / 10 |
| [BrokerageMonitor.Web.review.md](BrokerageMonitor.Web.review.md)                       | 9.0 / 10 |