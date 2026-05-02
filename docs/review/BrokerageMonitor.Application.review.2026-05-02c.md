# BrokerageMonitor.Application — Change-Set Review (B1/B2/A6 Fix Pass)

> **Review Date**: 2026-05-02  
> **Reviewer**: Chief Software Architect  
> **Scope**: Commit `2f20c8f` — 6 files changed (B1, B2, A6 from `BrokerageMonitor.review.2026-05-02b.md`)  
> **Test Result**: ✅ 592 / 592 passed, 0 failures  

---

## 📊 1. Architecture Health Score

| Project         | Previous | Updated  | Δ    | Remaining Risk                                                              |
| --------------- | -------- | -------- | ---- | --------------------------------------------------------------------------- |
| **Application** | 9.5 / 10 | 9.8 / 10 | +0.3 | Missing `ConfigureAwait(false)` in `Dispatch*Async` inner awaits (new, LOW) |

---

## ✅ 2. Architectural Strengths

### 2.1 B1 — `ConfigureAwait(false)` Applied Correctly

**`DomainEventDispatcher.DispatchAsync`**:
```csharp
await handler(@event, ct).ConfigureAwait(false);
```
Prevents unnecessary Blazor circuit `SynchronizationContext` resumption on every domain event dispatch.

**`SafeInvokeAsync`** inner await also patched:
```csharp
await handler().ConfigureAwait(false);
```
Both hot-path awaits are now context-free.

---

### 2.2 B2 — `private static` Applied to All Dispatch Helpers

All four methods that no longer access `this` are correctly marked `private static`:

| Method                                  | Before               | After                       |
| --------------------------------------- | -------------------- | --------------------------- |
| `DispatchComponentStatusChangedAsync`   | `private async Task` | `private static async Task` |
| `DispatchComponentLostAsync`            | `private async Task` | `private static async Task` |
| `DispatchComponentStateOverriddenAsync` | `private async Task` | `private static async Task` |
| `SafeInvokeAsync`                       | `private async Task` | `private static async Task` |

The `_logger` instance field is **removed entirely**. `ILogger logger` is now captured by each constructor lambda and forwarded as a method parameter — a clean closure pattern that communicates intent and eliminates accidental `this`-access:

```csharp
// Constructor — logger captured by lambda
[typeof(ComponentStatusChanged)] = (e, ct) =>
    DispatchComponentStatusChangedAsync(..., logger),

// private static — no instance dependency
private static async Task DispatchComponentStatusChangedAsync(
    ..., ILogger logger) { ... }
```

---

### 2.3 A6 — `TimeProvider` Injection: Full Coverage

**`DailyExecutionCreatorService`**:
- `DateTime.Today` / `DateTime.Now` → single `var localNow = _timeProvider.GetLocalNow()` snap; both `today` and `now` derived from the same instant (TOCTOU-safe).
- `DateTimeOffset.UtcNow` (component reset) → `_timeProvider.GetUtcNow()`.

**`AggregateHealthEvaluationService`** — all 6 call sites replaced:

| Location                       | Before                  | After                                  |
| ------------------------------ | ----------------------- | -------------------------------------- |
| `UpdateComponentProgressAsync` | `DateTime.Today`        | `_timeProvider.GetLocalNow().DateTime` |
| `EvaluateDefinitionAsync`      | `DateTime.Today`        | `_timeProvider.GetLocalNow().DateTime` |
| `FinalizeExecutionAsync`       | `DateTimeOffset.UtcNow` | `_timeProvider.GetUtcNow()`            |
| `SendNotificationsAsync` ×2    | `DateTimeOffset.UtcNow` | `_timeProvider.GetUtcNow()`            |
| `WriteInboxItemAsync`          | `DateTimeOffset.UtcNow` | `_timeProvider.GetUtcNow()`            |
| `WriteDeliveryFailedItemAsync` | `DateTimeOffset.UtcNow` | `_timeProvider.GetUtcNow()`            |

**DI Registration** (`ApplicationServiceCollectionExtensions`):
```csharp
// ---- Time abstraction (singleton — allows tests to substitute a fake clock) ----
services.TryAddSingleton(TimeProvider.System);
```
`TryAddSingleton` (not `AddSingleton`) ensures test hosts can override with a `FakeTimeProvider` without touching production registration.

**Constructor signature** uses `TimeProvider` (abstract base class) directly — the correct .NET 8 pattern. No custom `ITimeProvider` wrapper was introduced (KISS).

---

## ⚠️ 3. New Issues Found

### 🟡 LOW — `await SafeInvokeAsync(...)` Missing `ConfigureAwait(false)` Inside `Dispatch*Async`

**Location**: `DomainEventDispatcher.cs` — `DispatchComponentStatusChangedAsync` (4 calls), `DispatchComponentLostAsync` (3 calls), `DispatchComponentStateOverriddenAsync` (1 call)

```csharp
// Current — missing ConfigureAwait(false) on each SafeInvokeAsync call
await SafeInvokeAsync(
    () => alertEvaluation.EvaluateAsync(evt, ct),
    nameof(IAlertEvaluationService), logger);
```

In practice this is harmless: `DispatchAsync` already applied `ConfigureAwait(false)` before entering the lambda, so continuations are already on the thread pool. However, for internal consistency and to uphold the project-wide "all awaits in library code use `ConfigureAwait(false)`" convention, these 8 call sites should also be patched.

**Fix**:
```csharp
await SafeInvokeAsync(
    () => alertEvaluation.EvaluateAsync(evt, ct),
    nameof(IAlertEvaluationService), logger).ConfigureAwait(false);
```

---

## 💡 4. Refactoring Suggestions

### 4.1 Propagate `ConfigureAwait(false)` to `SafeInvokeAsync` call sites (from §3)

8 one-line additions across 3 private static methods. No logic change.

---

## 📝 5. Implementation Example

**Before vs After** — `DispatchComponentStatusChangedAsync`:

```csharp
// BEFORE (missing ConfigureAwait on each SafeInvokeAsync await)
private static async Task DispatchComponentStatusChangedAsync(
    ComponentStatusChanged evt, CancellationToken ct,
    IAlertEvaluationService alertEvaluation,
    IAggregateHealthEvaluationService healthEvaluation,
    IRealtimeNotificationService realtimeNotification,
    IAuditLogger auditLogger,
    ILogger logger)
{
    await SafeInvokeAsync(
        () => alertEvaluation.EvaluateAsync(evt, ct),
        nameof(IAlertEvaluationService), logger);

    await SafeInvokeAsync(
        () => healthEvaluation.UpdateComponentProgressAsync(evt, ct),
        nameof(IAggregateHealthEvaluationService), logger);

    // ...
}

// AFTER
private static async Task DispatchComponentStatusChangedAsync(...)
{
    await SafeInvokeAsync(
        () => alertEvaluation.EvaluateAsync(evt, ct),
        nameof(IAlertEvaluationService), logger).ConfigureAwait(false);

    await SafeInvokeAsync(
        () => healthEvaluation.UpdateComponentProgressAsync(evt, ct),
        nameof(IAggregateHealthEvaluationService), logger).ConfigureAwait(false);

    // ...
}
```

---

## 🔁 6. Action Items (Remaining)

| #   | Priority   | Item                                                                                           |
| --- | ---------- | ---------------------------------------------------------------------------------------------- |
| C1  | 🟡 LOW      | Add `.ConfigureAwait(false)` to all 8 `SafeInvokeAsync` call sites in `Dispatch*Async` methods |
| A8  | ⚪ ADVISORY | Add `bunit` for Blazor component testing (Web coverage currently 0.6%)                         |

---

## 🏁 7. Overall Assessment

All B1, B2, and A6 action items are resolved correctly. The `TimeProvider` injection is well-structured — single-snap TOCTOU-safe in `RecoverTodayAsync`, all 6 `DateTimeOffset.UtcNow` / `DateTime.Today` call sites in `AggregateHealthEvaluationService` migrated, and the DI registration is override-friendly. The `private static` refactor is clean: the `_logger` field removal is a net reduction of complexity, not just a cosmetic change. One LOW issue is identified — incomplete `ConfigureAwait(false)` coverage within the `Dispatch*Async` private static methods (8 call sites). The codebase remains production-ready.
