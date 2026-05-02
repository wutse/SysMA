# BrokerageMonitor.Infrastructure — Review Summary

> **Last Review**: 2026-05-03 | **Reviewer**: Chief Software Architect

---

## 📊 Current Status

**Health Score: 9.5 / 10**

The Infrastructure layer remains well-structured. Two `TimeProvider` consistency gaps were identified: `DailyExecutionCreatorJob` uses `DateTime.Today` and `DataRetentionJob` uses `DateTimeOffset.UtcNow` directly. `TimeProvider.System` is already registered in the DI container — injecting it is a straightforward fix.

---

## 🔧 Pending Action Items

1. **(LOW — I1)** `DailyExecutionCreatorJob.cs:31` — Replace `DateTime.Today` with `_timeProvider.GetLocalNow()`. Inject `TimeProvider` into constructor. Risk: wrong date on non-local-timezone servers.

2. **(LOW — I2)** `DataRetentionJob.cs` — Replace `DateTimeOffset.UtcNow.AddDays(...)` with `_timeProvider.GetUtcNow().AddDays(...)`. Inject `TimeProvider` into constructor.
