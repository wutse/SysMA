# BrokerageMonitor.Infrastructure — Review Summary

> **Last Review**: 2026-05-03b | **Reviewer**: Chief Software Architect

---

## 📊 Current Status

**Health Score: 9.0 / 10**

I1 and I2 from the previous session are resolved. A second-pass scan found four additional `TimeProvider` gaps: `HeartbeatTimeoutMonitor` uses `DateTimeOffset.UtcNow` directly in its timer callback (no `TimeProvider` injected); `AlertRecordRepository`, `MonitoredComponentRepository`, and `MonitoredSystemRepository` all stamp `UpdatedAt`/`AcknowledgedAt` via `DateTimeOffset.UtcNow.ToString("O")` rather than a testable provider.

---

## 🔧 Pending Action Items

1. **(LOW — I3)** `HeartbeatTimeoutMonitor.cs:215` — Inject `TimeProvider`; replace `DateTimeOffset.UtcNow` with `_timeProvider.GetUtcNow()` when constructing `ComponentLost`.

2. **(LOW — I4)** `AlertRecordRepository.cs:85` — Either inject `TimeProvider` or accept `DateTimeOffset acknowledgedAt` from the caller (preferred: caller `AcknowledgeAlertHandler` already owns `now`).

3. **(LOW — I5)** `MonitoredComponentRepository.cs:78` — Replace `DateTimeOffset.UtcNow.ToString("O")` in `UpsertAsync`; inject `TimeProvider`.

4. **(LOW — I6)** `MonitoredSystemRepository.cs:64` — Replace `DateTimeOffset.UtcNow.ToString("O")` in `UpsertAsync`; inject `TimeProvider`.
