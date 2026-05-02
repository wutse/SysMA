# BrokerageMonitor.Web — Review Summary

> **Last Review**: 2026-05-02 | **Reviewer**: Chief Software Architect

---

## 📊 Current Status

**Health Score: 8.5 / 10**

The Web layer now fully respects the Dependency Rule — all eight former `@inject Repository` directives across five Blazor pages have been replaced with Application-layer query handlers, and no `.razor` file references `BrokerageMonitor.Domain.Repositories` directly. Startup sequencing (`WebApplicationStartup`), circuit-scoped `OperatorSessionService`, and SignalR hub design are correct. The score is held below 9.0 by two low-priority housekeeping items (scaffolding artifacts, hardcoded cron expressions) and critically low test coverage on the Web layer (0.6%).

---

## 🔧 Pending Action Items

1. **(LOW)** Delete `Counter.razor` and `Weather.razor` — default Blazor template scaffolding with no business value. Verify `NavMenu.razor` has no `NavLink` referencing `/counter` or `/weather` before deleting.

2. **(LOW)** `WebApplicationStartup.ScheduleStaticJobsAsync` hardcodes cron expressions for `SmokeTestJob` and `DailyExecutionCreatorJob` as string literals. Move them to `appsettings.json` under an `IOptions<>` configuration section for environment-specific scheduling without recompile.

3. **(ADVISORY)** Web layer test coverage is 0.6%. Adopt `bunit` for Blazor component testing. Priority targets: `DashboardPage`, `AlertCenterPage`, and `OperatorSessionService` integration. Target ≥ 30% Web coverage.
