# BrokerageMonitor.Infrastructure — Architecture Review

> **Reviewer**: Chief Software Architect
> **Date**: 2026-05-01 _(previous: 2026-04-25)_
> **Layer**: Infrastructure (depends on Domain + Application)

### Δ Changes Since Previous Review

| #   | Issue                                                          | Status                                                     |
| --- | -------------------------------------------------------------- | ---------------------------------------------------------- |
| 1   | Reflection-based domain mutation in `DailyExecutionRepository` | ✅ **FIXED** (previous review)                              |
| 2   | `SmtpClient` deprecated                                        | ✅ **FIXED** (previous review) — replaced with MailKit      |
| 3   | Fire-and-forget `OnTimerFired` — `_stoppingToken` missing      | ✅ **FIXED** (previous review)                              |
| 4   | Fire-and-forget silently swallows exceptions                   | ✅ **FIXED** — `.ContinueWith` logs `OnlyOnFaulted` errors  |
| 5   | `SmtpEmailNotificationService` registered as `Transient`       | ✅ **FIXED** — now registered as `Singleton`                |
| —   | Inconsistent `IDbConnection` open state across repositories    | 🟡 **STILL OPEN** (low priority)                            |
| —   | `SendOnFailure` schema column not renamed after domain rename  | 🟡 **NEW** — semantic drift between schema and domain model |
| —   | `AddRealtimeNotifications()` XML doc comment is stale          | 🟡 **NEW** — doc claims it registers `IMonitorBroadcaster`  |

---

## 📊 Architecture Health Score: 9.0 / 10

Two new observations are identified this cycle. The domain rename of `SendOnFailure` → `NotificationsEnabled` (fix F3) was not propagated to the SQLite schema or the `DefinitionRow` Dapper record — the column still reads `SendOnFailure`, creating a semantic drift that will mislead developers querying the database directly. Additionally, the `AddRealtimeNotifications()` XML doc comment incorrectly states it registers `MonitorBroadcaster` as `IMonitorBroadcaster`, when that registration actually lives in `ApplicationServiceCollectionExtensions`. Both are low-risk documentation/schema inconsistencies, not runtime defects, but they will erode discoverability over time.

---

## ✅ Architectural Strengths

1. **Parameterized Queries via `CommandDefinition`** — All repositories use Dapper's `CommandDefinition` with anonymous objects rather than string interpolation. No SQL injection vectors identified.

2. **Transaction Scoping in `HealthMonitorDefinitionRepository`** — Both `UpsertAsync` (main row + junction table) and `DeleteAsync` (cascade delete) are wrapped in explicit `BeginTransaction` / `Commit` / `Rollback` blocks, ensuring atomicity.

3. **`[DisallowConcurrentExecution]` on Quartz Jobs** — `DataRetentionJob` and `AggregateHealthEvaluationJob` both carry this attribute, preventing overlapping executions that could cause double-deletes or double-evaluations.

4. **Exponential Backoff in `ZeroMQSubscriberService`** — Reconnection delay doubles from 1 s up to a 60 s ceiling, preventing thundering-herd reconnect storms after broker restarts.

5. **HTML Encoding in Email Templates** — `SmtpEmailNotificationService.Encode()` calls `WebUtility.HtmlEncode()` on all user-controlled values before embedding them in HTML bodies, preventing XSS in email clients.

6. **`HeartbeatTimeoutMonitor` Dual-Role Pattern** — Implementing both `BackgroundService` and `IHeartbeatTimerRegistry` on the same class, with registration forwarding via `AddSingleton<IHeartbeatTimerRegistry>(sp => sp.GetRequiredService<HeartbeatTimeoutMonitor>())`, ensures a single instance is shared across DI resolution paths without lifetime mismatch.

7. **Batch Component Loading in `HealthMonitorDefinitionRepository`** — `BuildDefinitionsAsync` loads all junction rows in a single `IN @Ids` query rather than N individual queries.

---

## ⚠️ Critical Violations

### 1. ✅ ~~Reflection-Based Domain Object Mutation in `DailyExecutionRepository`~~ — FIXED

A `DailyExecution.Rehydrate()` static factory method has been added to the domain aggregate, allowing `DailyExecutionRepository.MapToDomain()` to reconstitute the object without bypassing encapsulation:

```csharp
// DailyExecution.cs (new)
public static DailyExecution Rehydrate(
    Guid executionId, Guid definitionId, string systemId,
    DateOnly executionDate, DailyExecutionStatus status,
    DateTimeOffset createdAt, DateTimeOffset? evaluatedAt,
    IEnumerable<string>? completedComponents,
    IEnumerable<string>? failedComponents,
    string? missedReason, DateTimeOffset? notificationSentAt) { ... }

// DailyExecutionRepository.cs (updated)
private static DailyExecution MapToDomain(DailyExecutionRow row)
    => DailyExecution.Rehydrate(
        executionId: Guid.Parse(row.ExecutionId),
        ...); // ✅ No reflection
```

---

## ✅ Architectural Strengths

1. **Parameterized Queries via `CommandDefinition`** — All repositories use Dapper's `CommandDefinition` with anonymous objects. No SQL injection vectors identified.

2. **Transaction Scoping in `HealthMonitorDefinitionRepository`** — `UpsertAsync` and `DeleteAsync` are wrapped in explicit `BeginTransaction` / `Commit` / `Rollback` blocks.

3. **`[DisallowConcurrentExecution]` on Quartz Jobs** — `DataRetentionJob` and `AggregateHealthEvaluationJob` prevent overlapping executions.

4. **Exponential Backoff in `ZeroMQSubscriberService`** — Reconnection delay doubles from 1 s to a 60 s ceiling.

5. **HTML Encoding in Email Templates** — `Encode()` calls `WebUtility.HtmlEncode()` on all user-controlled values — no XSS in email clients.

6. **`MailKit` Replaces Deprecated `SmtpClient`** — `SmtpEmailNotificationService` now uses `MailKit.Net.Smtp.SmtpClient` + `MimeMessage`, eliminating the `[Obsolete]` warning and enabling modern TLS/OAuth flows.

7. **`HeartbeatTimeoutMonitor` Dual-Role Pattern** — A single instance shared across DI via `AddSingleton` forwarding, with `_stoppingToken` captured from `ExecuteAsync` so timer callbacks respect graceful shutdown.

8. **`Rehydrate()` Factory Eliminates All Reflection** — `DailyExecution.Rehydrate()` and `ComponentState.Rehydrate()` are used by all repositories — no `SetPrivateProperty` or `AppendToPrivateList` reflection helpers remain.

---

## ⚠️ Remaining Violations

### 1. Inconsistent `IDbConnection` Open State Across Repositories

`AlertRecordRepository` and most repositories use Dapper's implicit lazy-open — connections returned by `CreateConnection()` are used immediately without calling `.Open()`. However, `HealthMonitorDefinitionRepository` explicitly casts to `SqliteConnection` and calls `conn.Open()` before executing queries:

```csharp
// HealthMonitorDefinitionRepository.cs
using var conn = (SqliteConnection)_factory.CreateConnection();
conn.Open(); // ✅ explicit open
```

```csharp
// AlertRecordRepository.cs
using var conn = _factory.CreateConnection();
// No .Open() — relies on Dapper's implicit open behaviour
```

**Impact**: Low risk today (Dapper handles both patterns), but inconsistency will mislead maintainers and creates a subtle trap if a new repository skips the open call in a path where Dapper's implicit open does not fire (e.g., raw `IDbCommand` usage).

**Fix**: Standardize on explicit `conn.Open()` across all repositories, or document the lazy-open convention explicitly in a `DbConnectionFactory` XML comment.

---

### 2. 🟡 Schema Column `SendOnFailure` Not Renamed After Domain Rename

The domain model property `HealthMonitorDefinition.SendOnFailure` was renamed to `NotificationsEnabled` (fix F3). However, the SQLite schema in `DatabaseInitializer.cs` and the Dapper row record in `HealthMonitorDefinitionRepository` still use the old name:

```csharp
// DatabaseInitializer.cs — DDL unchanged
CREATE TABLE IF NOT EXISTS HealthMonitorDefinitions (
    ...
    SendOnFailure   INTEGER NOT NULL DEFAULT 0,   // ❌ stale name
    ...
);

// HealthMonitorDefinitionRepository.cs — DefinitionRow record
private sealed record DefinitionRow(
    ...
    long    SendOnFailure,   // ❌ stale name
    ...);

// Mapping in MapToDomain
notificationsEnabled: row.SendOnFailure == 1   // Works, but hides the mismatch
```

**Impact**: No runtime defect — the mapping `notificationsEnabled: row.SendOnFailure == 1` is functionally correct. However, anyone querying the SQLite file directly (debugging, BI tool, DBA) will encounter `SendOnFailure` which now means the opposite of what it says: `1` means "all notifications enabled," not "send on failure only." This will become a maintenance trap as the codebase evolves.

**Fix**: Rename the DDL column to `NotificationsEnabled` and update the `DefinitionRow` record accordingly. Because SQLite does not support `RENAME COLUMN` before version 3.25.0 (WAL mode is compatible), use `ALTER TABLE HealthMonitorDefinitions RENAME COLUMN SendOnFailure TO NotificationsEnabled;` — supported since SQLite 3.25 (bundled in .NET 8's `Microsoft.Data.Sqlite`).

---

### 3. 🟡 Stale XML Doc Comment in `AddRealtimeNotifications()`

The `AddRealtimeNotifications()` summary doc claims it registers `MonitorBroadcaster` as `IMonitorBroadcaster`, but the method body only registers `IRealtimeNotificationService → SignalRNotificationService`. The `MonitorBroadcaster` / `IMonitorBroadcaster` registration is performed in `ApplicationServiceCollectionExtensions.AddApplicationServices()`.

```csharp
// InfrastructureServiceCollectionExtensions.cs — current (misleading)
/// <summary>
/// Registers <see cref="SignalRNotificationService"/> as the singleton
/// <see cref="IRealtimeNotificationService"/> implementation, and
/// <see cref="MonitorBroadcaster"/> as the singleton <see cref="IMonitorBroadcaster"/>   ← ❌ false claim
/// for Blazor Server in-process event broadcasting.
/// </summary>
public static IServiceCollection AddRealtimeNotifications(this IServiceCollection services)
{
    services.AddSingleton<IRealtimeNotificationService, SignalRNotificationService>();
    // IMonitorBroadcaster is NOT registered here
    return services;
}
```

**Fix**: Remove the second sentence from the XML comment.

---

## 💡 Refactoring Suggestion

```csharp
/// <summary>
/// Creates and returns a <b>closed</b> <see cref="IDbConnection"/>.
/// Dapper opens the connection automatically on first query execution.
/// Callers that use raw IDbCommand must call <c>Open()</c> explicitly.
/// </summary>
public IDbConnection CreateConnection() => new SqliteConnection(_connectionString);
```

---

## 📝 Fixed Violation Reference — Before vs After

### Before — Fire-and-Forget Swallowed Exceptions (FIXED)

```csharp
// HeartbeatTimeoutMonitor.cs (previous)
private void OnTimerFired(object? state)
{
    _ = Task.Run(
        () => RaiseComponentLostAsync(entry, _stoppingToken),
        _stoppingToken);
    // ❌ Any exception from RaiseComponentLostAsync was silently discarded
}
```

### After — Faulted Task Logged via `ContinueWith` (current)

```csharp
// HeartbeatTimeoutMonitor.cs (current)
Task.Run(() => RaiseComponentLostAsync(entry, _stoppingToken), _stoppingToken)
    .ContinueWith(
        t => _logger.LogError(
            t.Exception,
            "HeartbeatTimeoutMonitor: unhandled error in timer callback for component {ComponentId}.",
            entry.ComponentId),
        CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted,  // ✅ only fires on fault
        TaskScheduler.Default);
```

---

```mermaid
%% Infrastructure — Persistence and Service Registration
graph LR
    subgraph "DI Registration"
        AP["AddPersistence()"]
        AZ["AddZeroMq()"]
        AN["AddNotificationServices()"]
        AR["AddRealtimeNotifications()"]
    end

    subgraph "Persistence"
        DBF["DbConnectionFactory<br/>(Singleton)"]
        DBInit["DatabaseInitializer<br/>(Singleton)"]
        Repos["9× Repository<br/>(Scoped)"]
    end

    subgraph "ZeroMQ"
        ZMQ["ZeroMQSubscriberService<br/>(BackgroundService)"]
        HTM["HeartbeatTimeoutMonitor<br/>(BackgroundService + IRegistry)"]
        Parsers["Message Parsers<br/>(Singleton)"]
    end

    subgraph "Notifications"
        SMTP["SmtpEmailNotificationService<br/>(Singleton)"]
        Teams["TeamsNotificationService<br/>(HttpClient)"]
        SignalR["SignalRNotificationService<br/>(Singleton)"]
    end

    AP -->|"registers"| DBF
    AP -->|"registers"| DBInit
    AP -->|"registers"| Repos
    AZ -->|"registers"| ZMQ
    AZ -->|"registers"| HTM
    AZ -->|"registers"| Parsers
    AN -->|"registers"| SMTP
    AN -->|"registers"| Teams
    AR -->|"registers"| SignalR
```

> **Design Intent**: Infrastructure is decomposed into four independently callable extension methods (`AddPersistence`, `AddZeroMq`, `AddNotificationServices`, `AddRealtimeNotifications`), each registering a cohesive group of services. This allows test hosts to mix real and stub implementations at the group level.
