# BrokerageMonitor.Infrastructure — Review Summary

> **Last Review**: 2026-05-02 | **Reviewer**: Chief Software Architect

---

## 📊 Current Status

**Health Score: 9.5 / 10**

The Infrastructure layer is well-structured and secure. All repositories use parameterized `CommandDefinition` queries (no SQL injection vectors), transaction scoping is correct in `HealthMonitorDefinitionRepository`, and the `HeartbeatTimeoutMonitor` dual-role singleton pattern is cleanly implemented. All previously open violations have been resolved, including the `SendOnFailure → NotificationsEnabled` DDL rename with idempotent startup migration, and the `[DisallowConcurrentExecution]` Quartz job decoration. Two medium items remain: the Domain's custom Cron parser lacks full Quartz expression support, and a missing composite index on `AlertRecords.SystemId` causes a full table scan on every heartbeat-driven alert evaluation.

---

## 🔧 Pending Action Items

1. **(MEDIUM — FR-042)** `HealthRuleSchedule.MatchesCron()` is a custom 5-field parser that does not support `L`, `W`, `#`, or `?` tokens used by Quartz. Replace with `Cronos` NuGet package or delegate to Quartz's `CronExpression.IsValidExpression()` for validation; consider adding construction-time validation so malformed expressions throw rather than silently producing no executions.

2. **(LOW — FR-010)** Add a composite index on `AlertRecords(SystemId, IsGlobalFlagActive)` in `DatabaseInitializer.SchemaDdl`. `HasUnacknowledgedAlertAsync` runs a `COUNT(1) WHERE SystemId = @s AND IsGlobalFlagActive = 1` on every alertable heartbeat event — a full table scan without this index.
   ```sql
   CREATE INDEX IF NOT EXISTS IX_AlertRecords_SystemId
   ON AlertRecords(SystemId, IsGlobalFlagActive);
   ```
