# BrokerageMonitor.Domain — Architecture Review

> **Reviewer**: Chief Software Architect
> **Date**: 2026-04-22
> **Layer**: Domain (innermost — no project references)

---

## 📊 Architecture Health Score: 7.5 / 10

The Domain layer demonstrates solid DDD discipline with well-encapsulated aggregates, immutable domain events, and value objects that enforce invariants at construction. The primary concerns are non-deterministic constructors, a misplaced read-model folder, and missing `IEquatable<T>` implementations on value objects.

---

## ✅ Architectural Strengths

1. **Strong Aggregate Encapsulation** — All aggregates use `private init` / `private set` for properties, with a dedicated `private` Dapper-materialization constructor. Business-mutating methods are the only public surface for state changes.

2. **Domain Events as Records** — `sealed record` types for events ensure immutability and structural equality without boilerplate. The `IDomainEvent` marker with `OccurredAt` is clean and minimal.

3. **Value Object Invariants Enforced at Construction** — `EmailAddress`, `MarketSessionWindow`, `MailParsingRule`, and `HealthRuleSchedule` all throw `ArgumentException` when invalid, ensuring that no invalid value objects can exist at runtime.

4. **ReDoS Protection on `EmailAddress`** — The compiled regex includes `TimeSpan.FromMilliseconds(100)` timeout, preventing catastrophic backtracking on malicious input.

5. **Terminal State Guards** — `DailyExecution.Complete()` rejects non-terminal statuses and prevents terminal-state overwrites (BI-013). The `TerminalStatuses` set is defined as a static `IReadOnlySet<DailyExecutionStatus>`, which is efficient.

6. **Business Rule Traceability** — Every aggregate and most methods reference their FR/BI requirement IDs in XML documentation, making traceability clear.

---

## ⚠️ Critical Violations

### 1. Non-Deterministic Constructors Impede Testability

`ComponentState` and `DailyExecution` set timestamps via `DateTimeOffset.UtcNow` in their constructors, making them non-deterministic and hard to test without time-mocking.

```csharp
// ComponentState.cs — line 26
LastStatusChangedAt = DateTimeOffset.UtcNow;

// DailyExecution.cs — line 64
CreatedAt = DateTimeOffset.UtcNow;
```

**Impact**: Tests cannot verify exact timestamp values. Infrastructure is forced to use reflection to override these (see `DailyExecutionRepository`).

---

### 2. Read Models Misplaced in `Repositories/` Folder

`AuditLogEntry` and `ExecutionHistoryEntry` are `sealed record` types co-located with repository interfaces. They are read models/projections — not repository contracts.

**Impact**: The `Repositories/` folder conflates two responsibilities: interface definitions and data shapes. Any developer browsing the folder cannot distinguish contracts from data models.

---

### 3. Value Objects Missing `IEquatable<T>`

All value objects (`EmailAddress`, `MarketSessionWindow`, `HealthRuleSchedule`, `MailParsingRule`, `MetricValue`, `SubIndicator`, `WatchedComponent`) override `Equals(object?)` but do not implement `IEquatable<T>`.

**Impact**: When used in generic collections (`IReadOnlyList<EmailAddress>`, `HashSet<WatchedComponent>`), the runtime falls back to boxing for equality comparisons instead of the type-safe generic path.

---

### 4. `AlertRecord.Acknowledge()` Allows Overwriting

There is no guard preventing a second call to `Acknowledge()` from overwriting the original `AcknowledgedBy` and `AcknowledgedAt` values.

**Impact**: Audit integrity is at risk — the original acknowledging operator can be silently replaced.

---

### 5. `HealthRuleSchedule.MatchesCron()` Custom Parser Is Fragile

The domain contains a bespoke 5-field cron parser. Three issues:
- `int.Parse(stepParts[0])` throws `FormatException` on malformed expressions (e.g., `*/a`).
- Quartz uses **6-field** cron expressions (`seconds minutes hours ...`). The domain's parser evaluates only `day-of-month`, `month`, and `day-of-week`, silently ignoring minute/hour fields passed in from the scheduler.
- Step divisors of `0` are not guarded, which would cause `DivideByZeroException` in a range loop.

---

## 💡 Refactoring Suggestions

1. **Accept timestamps as constructor parameters** — Pass `DateTimeOffset createdAt` into `DailyExecution` and `ComponentState` constructors. Callers provide `DateTimeOffset.UtcNow`; tests provide a fixed value.

2. **Create a `ReadModels/` folder** — Move `AuditLogEntry` and `ExecutionHistoryEntry` there. Reserve `Repositories/` exclusively for interface definitions.

3. **Implement `IEquatable<T>` on all Value Objects** — Follow the standard pattern: implement the interface, override `==` and `!=` operators, and call the typed `Equals(T other)` from `Equals(object?)`.

4. **Guard `Acknowledge()` against re-acknowledgement** — Add an `if (IsAcknowledged) throw new InvalidOperationException(...)` guard to prevent overwriting.

5. **Replace custom cron parser** — Either remove `MatchesCron()` from the domain and push cron schedule evaluation to the scheduler (Quartz) entirely, or validate the expression with Quartz's `CronExpression.IsValidExpression()` at construction time.

---

## 📝 Implementation Example

### Before — Non-Deterministic Constructor

```csharp
// DailyExecution.cs (current)
public DailyExecution(Guid executionId, Guid definitionId, string systemId,
    DateOnly executionDate, DailyExecutionStatus initialStatus = DailyExecutionStatus.InProgress,
    string? missedReason = null)
{
    // ...
    CreatedAt = DateTimeOffset.UtcNow; // ❌ non-deterministic
}
```

### After — Deterministic, Testable Constructor

```csharp
// DailyExecution.cs (proposed)
public DailyExecution(Guid executionId, Guid definitionId, string systemId,
    DateOnly executionDate,
    DateTimeOffset createdAt,                                    // ✅ injected
    DailyExecutionStatus initialStatus = DailyExecutionStatus.InProgress,
    string? missedReason = null)
{
    // ...
    CreatedAt = createdAt;
}
```

---

### Before — Value Object Without `IEquatable<T>`

```csharp
// EmailAddress.cs (current)
public sealed class EmailAddress
{
    public override bool Equals(object? obj) =>
        obj is EmailAddress other && Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
```

### After — With `IEquatable<T>`

```csharp
// EmailAddress.cs (proposed)
public sealed class EmailAddress : IEquatable<EmailAddress>
{
    public bool Equals(EmailAddress? other) =>
        other is not null && Value == other.Value;

    public override bool Equals(object? obj) =>
        obj is EmailAddress other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(EmailAddress? left, EmailAddress? right) =>
        left?.Equals(right) ?? right is null;

    public static bool operator !=(EmailAddress? left, EmailAddress? right) =>
        !(left == right);
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
