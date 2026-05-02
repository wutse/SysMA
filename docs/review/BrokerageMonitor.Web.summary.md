# BrokerageMonitor.Web — Review Summary

> **Last Review**: 2026-05-02 | **Reviewer**: Chief Software Architect

---

## 📊 Current Status

**Health Score: 9.0 / 10**

The Web layer now fully respects the Dependency Rule. Scaffolding artifacts (`Counter.razor`, `Weather.razor`) have been deleted. `HealthDefinitionEditorPage.razor` correctly reflects the `SendOnFailure` semantics with an updated label (「失敗時發送通知」). One remaining housekeeping item: hardcoded cron expressions in startup. Test coverage remains at 0.6% (advisory).

---

## 🔧 Pending Action Items

1. **(LOW)** `WebApplicationStartup.ScheduleStaticJobsAsync` hardcodes cron expressions as string literals. Move to `appsettings.json` under an `IOptions<>` section for environment-specific scheduling.

2. **(ADVISORY — A8)** Web layer test coverage is 0.6%. Adopt `bunit` for Blazor component testing. Priority targets: `DashboardPage`, `AlertCenterPage`, `OperatorSessionService`. Target ≥ 30% Web coverage.
