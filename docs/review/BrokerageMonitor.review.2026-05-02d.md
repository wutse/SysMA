# BrokerageMonitor — Change-Set Review (C1 + Web LOW Fix Pass)

> **Review Date**: 2026-05-02  
> **Reviewer**: Chief Software Architect  
> **Scope**: Commit `b0fdbd7` — 5 files changed (C1, Web LOW from `BrokerageMonitor.Application.review.2026-05-02c.md`)  
> **Test Result**: ✅ 592 / 592 passed, 0 failures  

---

## 📊 1. Architecture Health Scores (Updated)

| Project         | Previous | Updated  | Δ    | Remaining Risk                                        |
| --------------- | -------- | -------- | ---- | ----------------------------------------------------- |
| **Application** | 9.8 / 10 | 10 / 10  | +0.2 | None open (A8 advisory is Web-layer scope)            |
| **Web**         | 9.0 / 10 | 9.3 / 10 | +0.3 | Cron validation at startup (new, LOW); bunit advisory |

---

## ✅ 2. Architectural Strengths

### 2.1 C1 — Full `ConfigureAwait(false)` Propagation in `DomainEventDispatcher`

All 8 `SafeInvokeAsync` call sites across the three `Dispatch*Async` methods now chain `.ConfigureAwait(false)`. The complete async call graph is now uniformly context-free:

```
DispatchAsync                           → .ConfigureAwait(false)  ✅ (previous fix)
  └─ DispatchComponent*Async (×3)
       └─ SafeInvokeAsync (×8 total)    → .ConfigureAwait(false)  ✅ (this fix)
            └─ handler()               → .ConfigureAwait(false)  ✅ (previous fix)
```

The entire event-dispatch hot path is now fully context-free at every await boundary. No Blazor circuit `SynchronizationContext` resumption can occur anywhere in the chain.

---

### 2.2 Web LOW — Cron Strings Externalised to `appsettings.json`

The implementation is clean and follows the standard .NET Options pattern correctly:

**`SchedulerOptions`** — well-formed options class:
- `sealed` class prevents unintended inheritance.
- `init`-only properties: immutable after binding, compatible with .NET 8 options binding.
- Hardcoded defaults (`"0 0 1 * * ?"`, `"0 30 5 * * ?"`) preserved as fallback if the config section is absent — correct defensive practice.
- `SectionName` constant eliminates the magic string at the call site.

**`appsettings.json`** — `"Scheduler"` section added with both cron values, matching the property names exactly (case-insensitive binding is the .NET default).

**`Program.cs`** — `services.Configure<SchedulerOptions>(config.GetSection(SchedulerOptions.SectionName))` — idiomatic registration.

**`WebApplicationStartup`** — resolves `IOptions<SchedulerOptions>` directly from `app.Services` in the constructor. Since `WebApplicationStartup` is manually newed up with `new WebApplicationStartup(app)` (not DI-registered), service locator from the root container is the correct pattern here — no violation.

---

## ⚠️ 3. New Issues Found

### 🟡 LOW — `SchedulerOptions` Has No Cron Validation at Startup

**Location**: `SchedulerOptions.cs` + `Program.cs`

If an invalid cron expression is configured in `appsettings.json` (e.g., `"0 0 1 * *"` — missing field), `QuartzJobScheduler.ScheduleCronJobAsync` will throw a `FormatException` (or Quartz's own `SchedulerException`) at startup runtime rather than failing fast at DI build time.

.NET 8 provides `IValidateOptions<T>` for fail-fast option validation at startup.

**Fix**:
```csharp
// SchedulerOptionsValidator.cs (Application layer — Web)
public sealed class SchedulerOptionsValidator : IValidateOptions<SchedulerOptions>
{
    public ValidateOptionsResult Validate(string? name, SchedulerOptions options)
    {
        var errors = new List<string>();

        if (!IsValidQuartzCron(options.SmokeTestCron))
            errors.Add($"SmokeTestCron '{options.SmokeTestCron}' is not a valid 6-field Quartz cron.");

        if (!IsValidQuartzCron(options.DailyExecutionCreatorCron))
            errors.Add($"DailyExecutionCreatorCron '{options.DailyExecutionCreatorCron}' is not a valid 6-field Quartz cron.");

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }

    private static bool IsValidQuartzCron(string cron) =>
        !string.IsNullOrWhiteSpace(cron) && cron.Trim().Split(' ').Length == 6;
}

// Program.cs — register validator
builder.Services.AddSingleton<IValidateOptions<SchedulerOptions>, SchedulerOptionsValidator>();
```

---

### ⚪ ADVISORY — `Configure<SchedulerOptions>` Registration Order

**Location**: `Program.cs` — `Configure<SchedulerOptions>` is registered after `AddRazorComponents()`.

Not a functional issue (DI container is not built until `builder.Build()`), but conventional practice groups all `Configure<T>` option registrations in a dedicated block near the top of the service registration section. This aids discoverability.

---

## 💡 4. Refactoring Suggestions

### 4.1 Add `IValidateOptions<SchedulerOptions>` (from §3 above)

Fail-fast startup validation for cron expressions. One new small class + one DI registration.

---

## 📝 5. Implementation Example

**`SchedulerOptionsValidator`** — full implementation (see §3 above).

---

## 🔁 6. Action Items (Remaining)

| #   | Priority   | Item                                                                                                    |
| --- | ---------- | ------------------------------------------------------------------------------------------------------- |
| D1  | 🟡 LOW      | Add `IValidateOptions<SchedulerOptions>` to catch invalid cron strings at DI build / startup validation |
| A8  | ⚪ ADVISORY | Add `bunit` for Blazor component testing (Web layer, 0.6% coverage)                                     |

---

## 🏁 7. Overall Assessment

The `DomainEventDispatcher` is now architecturally complete: every `await` in the full dispatch call graph chains `.ConfigureAwait(false)`. The Application layer reaches a perfect 10/10 for the in-scope classes. The `SchedulerOptions` / `IOptions<T>` refactor is idiomatic — `init` properties, `SectionName` constant, correct service-locator resolution from a manually constructed class, and preserved defaults. One LOW issue is identified: there is no startup-time validation that the configured cron strings are valid Quartz expressions. Adding `IValidateOptions<SchedulerOptions>` would make the gap fail-fast at app startup rather than silently at scheduling time.
