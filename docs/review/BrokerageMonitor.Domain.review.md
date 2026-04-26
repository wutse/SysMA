# BrokerageMonitor.Domain — Architecture Review

> **Reviewer**: Chief Software Architect
> **Date**: 2026-04-25 _(previous: 2026-04-24)_
> **Layer**: Domain (innermost — no project references)

### Δ Changes Since Previous Review

| #   | Issue                                             | Status           |
| --- | ------------------------------------------------- | ---------------- |
| 1   | Value objects missing `IEquatable<T>`             | ✅ **FIXED**      |
| 2   | `AlertRecord.Acknowledge()` allows overwriting    | ✅ **FIXED**      |
| 3   | `HealthRuleSchedule.MatchesCron()` fragile parser | ✅ **FIXED**      |
| 4   | Read models in `Repositories/` folder             | ✅ **FIXED**      |
| 5   | `ComponentState.Rehydrate()` missing              | ✅ **FIXED**      |
| 6   | Non-deterministic public constructors             | 🟡 **STILL OPEN** |

---

## 📊 Architecture Health Score: 8.5 / 10

The Domain layer is now in excellent shape. All previous violations have been resolved: value objects implement `IEquatable<T>`, `AlertRecord.Acknowledge()` guards against double-acknowledgment, `MatchesCron()` uses safe parsing with a documented intent for 3-of-5 field matching, read models are correctly placed in `ReadModels/`, and `Rehydrate()` factories exist on `ComponentState` and `DailyExecution` for deterministic DB hydration. The only remaining concern is that the public constructors of `ComponentState` and `DailyExecution` still capture `DateTimeOffset.UtcNow` directly, which affects unit test precision.

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

## ⚠️ Remaining Violation

### 1. Non-Deterministic Public Constructors Impede Unit Testing

The public constructors of `ComponentState` and `DailyExecution` still capture `DateTimeOffset.UtcNow` internally:

```csharp
// ComponentState.cs
public ComponentState(string componentId, ComponentStatus initialStatus = ComponentStatus.Unknown)
{
    // ...
    LastStatusChangedAt = DateTimeOffset.UtcNow; // ❌ non-deterministic
}

// DailyExecution.cs
public DailyExecution(Guid executionId, Guid definitionId, string systemId, DateOnly executionDate, ...)
{
    // ...
    CreatedAt = DateTimeOffset.UtcNow; // ❌ non-deterministic
}
```

**Impact**: Unit tests cannot assert exact `CreatedAt` / `LastStatusChangedAt` values when constructing entities directly. The `Rehydrate()` factory mitigates this for DB loads, but any test creating a new `DailyExecution` or `ComponentState` to verify timestamp behavior must work around the non-determinism.

---

## 💡 Refactoring Suggestion

Accept the timestamp as an optional constructor parameter, defaulting to `DateTimeOffset.UtcNow` at the call site:

```csharp
// DailyExecution.cs (proposed)
public DailyExecution(
    Guid executionId,
    Guid definitionId,
    string systemId,
    DateOnly executionDate,
    DailyExecutionStatus initialStatus = DailyExecutionStatus.InProgress,
    string? missedReason = null,
    DateTimeOffset? createdAt = null)   // ✅ injected; callers pass UtcNow; tests pass a fixed value
{
    // ...
    CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
}
```

This is a backward-compatible, zero-friction change — no callers need to update unless they want deterministic tests.

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
