# BrokerageMonitor.Application — Review Summary

> **Last Review**: 2026-05-02 | **Reviewer**: Chief Software Architect

---

## 📊 Current Status

**Health Score: 9.0 / 10**

The Application layer is architecturally sound with clean CQRS-style use cases, proper `DomainEventDispatcher` isolation, and an efficient `SafeInvokeAsync` fan-out pattern. The hot-path N+1 scan on every `ComponentStatusChanged` event has been resolved via `GetByWatchedComponentAsync`. However, one **HIGH-priority functional regression** was identified: renaming `SendOnFailure` → `NotificationsEnabled` changed the flag's semantic scope from "controls failure notification only" to "suppresses both success and failure notifications," directly violating FR-013 which requires the success notification to always be sent.

---

## 🔧 Pending Action Items

1. **(HIGH — FR-013/FR-014)** Revert `NotificationsEnabled` back to `SendOnFailure`. Fix `FinalizeExecutionAsync` so the success notification is always sent regardless of the flag; only the failure notification should be gated by `SendOnFailure`. See `BrokerageMonitor.review.2026-05-02.md` §3 for the Before/After fix pattern.

2. **(ADVISORY — FR-042/FR-044)** Inject `System.TimeProvider` into `DailyExecutionCreatorService` and `AggregateHealthEvaluationService` to replace `DateTime.Today` / `DateTimeOffset.UtcNow` direct calls, enabling correct UTC-based date logic and unit-testable time behavior.

3. **(ADVISORY)** Delete `Test1.cs` placeholder files from `BrokerageMonitor.Application.Tests` (and other test projects) — the `[Ignore]` placeholder methods add noise to CI output without verifying any behavior.
