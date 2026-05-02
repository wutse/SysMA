# BrokerageMonitor — Architecture Review Summary

> **Current snapshot**: 2026-05-03b (`TimeProvider` second-pass sweep — 10 new gaps found across Application + Infrastructure)
> **Reviewer**: Chief Software Architect
> For historical review details see the dated per-project files in this folder.

---

## Health Scores

| Project            | Last Review | Score    | Open Items | Key Remaining Risk                                                                      |
| ------------------ | ----------- | -------- | ---------- | --------------------------------------------------------------------------------------- |
| **Domain**         | 2026-05-03  | 9.7 / 10 | 1          | `MarketSessionWindow` UTC advisory only                                                 |
| **Application**    | 2026-05-03b | 9.0 / 10 | 6          | 6× `DateTimeOffset.UtcNow` in new handlers/services (C3–C8); no `TimeProvider` injected |
| **Infrastructure** | 2026-05-03b | 9.0 / 10 | 4          | `HeartbeatTimeoutMonitor` timer callback + 3 repository `UpdatedAt` stamps (I3–I6)      |
| **MailAgent**      | 2026-05-03  | 9.0 / 10 | 2          | STA bridge blocking call; `PollAndPublishAsync` suffix (advisory)                       |
| **Web**            | 2026-05-03  | 9.5 / 10 | 2          | Indent style in `SchedulerOptionsValidator`; bunit advisory                             |

---

