# BrokerageMonitor.Domain — Review Summary

> **Last Review**: 2026-05-03c | **Reviewer**: Chief Software Architect

---

## 📊 Current Status

**Health Score: 9.5 / 10**

The Domain layer is exemplary — rich aggregates, invariants enforced at construction, no anemic model patterns. One new advisory added this session: two public constructors use `DateTimeOffset.UtcNow` as a default-parameter fallback (`ComponentState`, `DailyExecution`). In practice the impact is negligible (callers immediately overwrite the timestamp), but the pattern breaks the `TimeProvider` testability contract. Callers in Application should pass explicit timestamps.

---

## 🔧 Pending Action Items

1. **(ADVISORY — A6)** `MarketSessionWindow.IsWithinSession(DateTimeOffset)` uses `.TimeOfDay` without normalizing the UTC offset. Document the required offset convention in `<remarks>` XML, or enforce normalization inside the method.

2. **(LOW — N2)** `ComponentState.cs:54` and `DailyExecution.cs:66` — `changedAt ?? DateTimeOffset.UtcNow` / `createdAt ?? DateTimeOffset.UtcNow` fallback. Application callers (`HeartbeatProcessor`, `DailyExecutionCreatorService`) should always supply an explicit timestamp sourced from `TimeProvider`, rather than relying on the Domain default.
