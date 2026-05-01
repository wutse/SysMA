# BrokerageMonitor.Infrastructure — Architecture Review

> **Reviewer**: Chief Software Architect
> **Date**: 2026-05-01 _(fourth pass — refactor compliance check)_
> **Layer**: Infrastructure (depends on Domain + Application)

### Δ Changes Since Previous Review

| #   | Issue                                                          | Status                                                                            |
| --- | -------------------------------------------------------------- | --------------------------------------------------------------------------------- |
| 1   | Reflection-based domain mutation in `DailyExecutionRepository` | ✅ **FIXED** (previous review)                                                     |
| 2   | `SmtpClient` deprecated                                        | ✅ **FIXED** (previous review) — replaced with MailKit                             |
| 3   | Fire-and-forget `OnTimerFired` — `_stoppingToken` missing      | ✅ **FIXED** (previous review)                                                     |
| 4   | Fire-and-forget silently swallows exceptions                   | ✅ **FIXED** — `.ContinueWith` logs `OnlyOnFaulted` errors                         |
| 5   | `SmtpEmailNotificationService` registered as `Transient`       | ✅ **FIXED** — now registered as `Singleton`                                       |
| —   | Inconsistent `IDbConnection` open state across repositories    | 🟡 **STILL OPEN** (low priority)                                                   |
| —   | `SendOnFailure` schema column not renamed after domain rename  | ✅ **FIXED** — column renamed; idempotent migration added in `DatabaseInitializer` |
| —   | `AddRealtimeNotifications()` XML doc comment is stale          | ✅ **FIXED** — false `IMonitorBroadcaster` claim removed from doc comment          |
| —   | `ApplyMigrationsAsync` synchronous despite `Async` suffix      | 🟡 **NEW — LOW** — method returns `Task.CompletedTask` synchronously               |

---

## 📊 Architecture Health Score: 9.0 / 10

## 📊 Architecture Health Score: 9.5 / 10

Both open violations from the 2026-05-01 review are resolved. `SendOnFailure` has been renamed `NotificationsEnabled` in the DDL, the `DefinitionRow` Dapper record, and all SQL strings; an idempotent `ApplyMigrationsAsync` migration runs at startup to rename the column in existing databases. The `AddRealtimeNotifications()` XML doc comment no longer contains the false `IMonitorBroadcaster` claim. A new `GetByWatchedComponentAsync` implementation correctly uses a junction-table JOIN. The only remaining open item is the low-priority `IDbConnection` open-state convention, plus a new minor observation on `ApplyMigrationsAsync` naming.

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

### 2. `ApplyMigrationsAsync` — Synchronous Implementation with `Async` Suffix (NEW — LOW)

The new `ApplyMigrationsAsync` method in `DatabaseInitializer` returns `Task.CompletedTask` after executing all migration logic synchronously via `IDbCommand.ExecuteReader()` and `ExecuteNonQuery()`. The `Async` suffix implies an awaitable operation backed by I/O, which is misleading:

```csharp
// DatabaseInitializer.cs
private static Task ApplyMigrationsAsync(IDbConnection connection, CancellationToken ct)
{
    ct.ThrowIfCancellationRequested();
    using var pragma = connection.CreateCommand();       // sync
    using var reader = pragma.ExecuteReader();           // sync — no ExecuteReaderAsync
    ...
    renameCmd.ExecuteNonQuery();                         // sync
    return Task.CompletedTask;                           // ❌ sync method, Async suffix
}
```

**Fix**: Either convert to a true `async Task` using `ExecuteReaderAsync` / `ExecuteNonQueryAsync`, or rename to `ApplyMigrations()` returning `void` / remove the `Task` wrapper:

```csharp
// Option A — true async
private static async Task ApplyMigrationsAsync(IDbConnection connection, CancellationToken ct)
{
    ct.ThrowIfCancellationRequested();
    using var pragma = (SqliteCommand)connection.CreateCommand();
    pragma.CommandText = "PRAGMA table_info(HealthMonitorDefinitions);";
    using var reader = await pragma.ExecuteReaderAsync(ct).ConfigureAwait(false);
    bool hasSendOnFailure = false;
    while (await reader.ReadAsync(ct).ConfigureAwait(false))
    {
        if (reader["name"] is string name &&
            string.Equals(name, "SendOnFailure", StringComparison.OrdinalIgnoreCase))
        { hasSendOnFailure = true; break; }
    }
    await reader.CloseAsync().ConfigureAwait(false);
    if (hasSendOnFailure)
    {
        using var cmd = (SqliteCommand)connection.CreateCommand();
        cmd.CommandText = "ALTER TABLE HealthMonitorDefinitions RENAME COLUMN SendOnFailure TO NotificationsEnabled;";
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
```

---

## ✅ Fixed Violations (This Cycle)

### 2. Schema Column `SendOnFailure` → `NotificationsEnabled` (FIXED)

```csharp
// Before ❌ — DDL
SendOnFailure INTEGER NOT NULL DEFAULT 0

// After ✅ — DDL
NotificationsEnabled INTEGER NOT NULL DEFAULT 0

// Idempotent startup migration added in DatabaseInitializer.ApplyMigrationsAsync()
ALTER TABLE HealthMonitorDefinitions RENAME COLUMN SendOnFailure TO NotificationsEnabled;
```

### 3. Stale XML Doc Comment in `AddRealtimeNotifications()` (FIXED)

```csharp
// Before ❌ — false claim about IMonitorBroadcaster registration
/// Registers SignalRNotificationService as IRealtimeNotificationService, and
/// MonitorBroadcaster as IMonitorBroadcaster for Blazor Server broadcasting.

// After ✅ — accurate
/// Registers SignalRNotificationService as the singleton IRealtimeNotificationService.
```

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
