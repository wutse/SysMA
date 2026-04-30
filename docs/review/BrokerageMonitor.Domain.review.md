# BrokerageMonitor.Domain — Architecture Review

> **Reviewer**: Chief Software Architect
> **Date**: 2026-05-01 _(previous: 2026-04-25)_
> **Layer**: Domain (innermost — no project references)

### Δ Changes Since Previous Review

| #   | Issue                                             | Status                                               |
| --- | ------------------------------------------------- | ---------------------------------------------------- |
| 1   | Value objects missing `IEquatable<T>`             | ✅ **FIXED** (previous review)                        |
| 2   | `AlertRecord.Acknowledge()` allows overwriting    | ✅ **FIXED** (previous review)                        |
| 3   | `HealthRuleSchedule.MatchesCron()` fragile parser | ✅ **FIXED** (previous review)                        |
| 4   | Read models in `Repositories/` folder             | ✅ **FIXED** (previous review)                        |
| 5   | `ComponentState.Rehydrate()` missing              | ✅ **FIXED** (previous review)                        |
| 6   | Non-deterministic public constructors             | ✅ **FIXED** — optional timestamp parameters accepted |

---

## 📊 Architecture Health Score: 9.0 / 10

The Domain layer is in excellent shape. All previously identified violations have been resolved. The final open concern — non-deterministic `UtcNow` capture in public constructors — is now addressed: both `ComponentState` and `DailyExecution` accept optional `DateTimeOffset?` parameters that default to `UtcNow`, giving tests full timestamp control. No new violations identified.

---

## ✅ Architectural Strengths

1. **Strong Aggregate Encapsulation** — All aggregates use `private init` / `private set` for properties, with dedicated Dapper-materialization constructors. Business-mutating methods are the only public surface for state changes.

2. **Domain Events as Records** — `sealed record` types ensure immutability and structural equality. The `IDomainEvent` marker with `OccurredAt` is clean and minimal.

3. **Value Object Invariants Enforced at Construction** — `EmailAddress`, `MarketSessionWindow`, `MailParsingRule`, and `HealthRuleSchedule` all throw `ArgumentException` on invalid input.

4. **ReDoS Protection on `EmailAddress`** — The compiled regex includes a `TimeSpan.FromMilliseconds(100)` timeout.

5. **Terminal State Guards** — `DailyExecution.Complete()` rejects non-terminal statuses and prevents terminal-state overwrites (BI-013). The `TerminalStatuses` set is a static `IReadOnlySet<DailyExecutionStatus>`.

6. **`Rehydrate()` Factories** — Both `ComponentState` and `DailyExecution` expose `public static Rehydrate(...)` factory methods. Repositories use these for DB hydration — no reflection, deterministic timestamps.

7. **`IEquatable<T>` on All Value Objects** — All seven value objects (`EmailAddress`, `MarketSessionWindow`, `HealthRuleSchedule`, `MailParsingRule`, `MetricValue`, `SubIndicator`, `WatchedComponent`) implement `IEquatable<T>` — no boxing in generic collections.

8. **`AlertRecord.Acknowledge()` is Idempotent-Safe** — Throws `InvalidOperationException` on a second call, protecting audit integrity.

9. **Safe Cron Parser** — `MatchesCron()` uses `int.TryParse` throughout, guards against step divisor zero, and clearly documents the 3-of-5 field intent (minute/hour are scheduler responsibilities).

10. **Read Models Correctly Placed** — `AuditLogEntry` and `ExecutionHistoryEntry` live in `ReadModels/`. The `Repositories/` folder contains only interface contracts.

---

## ✅ No Open Violations

All previously identified violations have been resolved. The Domain layer has no remaining architectural concerns.

---

## 💡 Observation — Cron Matching Tied to Local Time Zone

`DailyExecutionCreatorService.CreateForDateAsync` and `RecoverTodayAsync` use `DateTime.Today` (local time) to derive the current date, while `HealthRuleSchedule.IsMatch(date)` operates on a `DateOnly`. In a UTC-hosted environment these will agree, but in production environments with a non-UTC server time zone the "today" fed to `IsMatch` could be the wrong calendar day relative to the trading session. Consider capturing date context via a clock abstraction or at minimum documenting the assumed time zone in the service.

---

## 📝 Fixed Violation Reference — Before vs After

### Before — Non-Deterministic Constructors (FIXED)

```csharp
// ComponentState.cs (previous)
public ComponentState(string componentId, ComponentStatus initialStatus = ComponentStatus.Unknown)
{
    LastStatusChangedAt = DateTimeOffset.UtcNow; // ❌ non-deterministic
}

// DailyExecution.cs (previous)
public DailyExecution(Guid executionId, ...)
{
    CreatedAt = DateTimeOffset.UtcNow; // ❌ non-deterministic
}
```

### After — Optional Timestamp Parameter (current)

```csharp
// ComponentState.cs (current)
public ComponentState(
    string componentId,
    ComponentStatus initialStatus = ComponentStatus.Unknown,
    DateTimeOffset? changedAt = null)   // ✅ tests pass a fixed value
{
    LastStatusChangedAt = changedAt ?? DateTimeOffset.UtcNow;
}

// DailyExecution.cs (current)
public DailyExecution(
    Guid executionId, Guid definitionId, string systemId,
    DateOnly executionDate,
    DailyExecutionStatus initialStatus = DailyExecutionStatus.InProgress,
    string? missedReason = null,
    DateTimeOffset? createdAt = null)   // ✅ tests pass a fixed value
{
    CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
}
```

---

```mermaid
%% Domain Layer — Aggregate and Value Object Relationships
classDiagram
    class AlertRecord {
        <<Aggregate Root>>
        +Guid AlertId
        +string SystemId
        +Acknowledge(operatorName, acknowledgedAt)
    }
    class DailyExecution {
        <<Aggregate Root>>
        +Guid ExecutionId
        +bool IsTerminal
        +AddCompletedComponent(componentId)
        +Complete(terminalStatus, evaluatedAt)
    }
    class HealthMonitorDefinition {
        <<Aggregate Root>>
        +Guid DefinitionId
        +SetWatchedComponents(components)
        +Activate() / Deactivate()
    }
    class MonitoredSystem {
        <<Aggregate Root>>
        +string SystemId
        +ActivateMaintenance(operatorName)
        +DeactivateMaintenance(operatorName)
    }
    class MonitoredComponent {
        <<Aggregate Root>>
        +string ComponentId
        +MailParsingRule? MailParsingRule
    }
    class ComponentState {
        <<Entity>>
        +UpdateStatus(newStatus, changedAt)
        +RecordHeartbeat(receivedAt)
    }
    class IDomainEvent {
        <<interface>>
        +DateTimeOffset OccurredAt
    }
    class EmailAddress {
        <<Value Object>>
        +string Value
    }
    class MarketSessionWindow {
        <<Value Object>>
        +TimeOnly StartTime
        +TimeOnly EndTime
        +IsWithinSession(time)
    }
    class HealthRuleSchedule {
        <<Value Object>>
        +ScheduleType ScheduleType
        +IsMatch(date)
    }

    MonitoredSystem "1" o-- "*" EmailAddress : AlertRecipients
    MonitoredSystem "1" *-- "1" MarketSessionWindow
    HealthMonitorDefinition "1" *-- "1" HealthRuleSchedule
    HealthMonitorDefinition "1" o-- "*" EmailAddress : EmailRecipients
    DailyExecution ..|> IDomainEvent : raises (via Application)
    AlertRecord ..|> IDomainEvent : raises (via Application)
```

> **Design Intent**: Aggregates own their invariants. Domain events are raised via the Application layer's `DomainEventDispatcher` rather than directly from aggregate methods — a pragmatic departure from classical DDD that trades strict boundary purity for implementation simplicity with Dapper.
