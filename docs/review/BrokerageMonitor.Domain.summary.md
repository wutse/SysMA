# BrokerageMonitor.Domain — Review Summary

> **Last Review**: 2026-05-02 | **Reviewer**: Chief Software Architect

---

## 📊 Current Status

**Health Score: 9.5 / 10**

The Domain layer demonstrates exemplary Clean Architecture discipline. All six Aggregate Roots enforce invariants in constructors and mutation methods, eliminating Anemic Domain Model risk. All previously identified violations (aggregate boundary bypass via `SetMaintenanceModeAsync`, `DailyExecution.Complete()` deduplication gap, `MailParsingRule.GetHashCode()` contract inconsistency) have been resolved. The only remaining concern is advisory-level: two locations carry implicit UTC assumptions that create a testability gap but pose low operational risk at current server configurations.

---

## 🔧 Pending Action Items

1. **(ADVISORY)** `MarketSessionWindow.IsWithinSession(DateTimeOffset)` uses `.TimeOfDay` without normalizing the UTC offset — two instants representing the same UTC moment but carrying different offsets can return different results. Document the required offset convention (UTC-normalized input strongly recommended) or enforce normalization inside the method.

2. **(ADVISORY)** `DailyExecutionCreatorService` derives execution date via `DateTime.Today` (server local calendar). Inject `System.TimeProvider` to decouple from the server's clock, enabling correct UTC-based date derivation and eliminating the testability gap.
