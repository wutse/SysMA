# Test Quality Report
Generated: 2026-05-01

---

## 1. Test Execution Summary

| Project                               | Total   | Passed  | Skipped | Failed |
| ------------------------------------- | ------- | ------- | ------- | ------ |
| BrokerageMonitor.Domain.Tests         | 99      | 98      | 1       | 0      |
| BrokerageMonitor.Application.Tests    | 242     | 241     | 1       | 0      |
| BrokerageMonitor.Infrastructure.Tests | 148     | 147     | 1       | 0      |
| **Total**                             | **489** | **486** | **3**   | **0**  |

> Skipped = `[Ignore]` placeholder `TestMethod1` in each project's `Test1.cs`

---

## 2. Code Coverage Summary

| Assembly                        | Line    | Branch    | Method    |
| ------------------------------- | ------- | --------- | --------- |
| BrokerageMonitor.Application    | 84.6%   | —         | —         |
| BrokerageMonitor.Domain         | 81.0%   | —         | —         |
| BrokerageMonitor.Infrastructure | 74.7%   | —         | —         |
| BrokerageMonitor.Web            | 0.6%    | —         | —         |
| **Overall**                     | **62%** | **52.1%** | **60.1%** |

---

## 3. Test Project Fixes Applied

### 3.1 MSTEST0037 — Replaced `Assert.AreEqual(n, collection.Count)` with `IsEmpty`/`HasCount`

The following files used `Assert.AreEqual(0/n, collection.Count)` which triggers the
MSTEST0037 analyzer rule "Prefer `Assert.IsEmpty` / `Assert.HasCount` over `Assert.AreEqual`".

**Files fixed:**

| File                                                                  | Pattern replaced |
| --------------------------------------------------------------------- | ---------------- |
| `Application.Tests/Services/AlertEvaluationServiceTests.cs`           | 10 occurrences   |
| `Application.Tests/Services/AggregateHealthEvaluationServiceTests.cs` | 1 occurrence     |
| `Application.Tests/UseCases/AcknowledgeAlertHandlerTests.cs`          | 4 occurrences    |
| `Application.Tests/UseCases/OverrideComponentStateHandlerTests.cs`    | 5 occurrences    |
| `Application.Tests/UseCases/ToggleMaintenanceModeHandlerTests.cs`     | 9 occurrences    |

Replacement rules:
- `Assert.AreEqual(0, col.Count)` → `Assert.IsEmpty(col)`
- `Assert.AreEqual(n, col.Count)` → `Assert.HasCount(n, col)` *(count first)*

### 3.2 CS0067 — Suppressed unused-event warnings in `NullBroadcaster`

`SignalRNotificationServiceTests.cs` declares a `NullBroadcaster` test double that
implements `IMonitorBroadcaster`. The interface requires five events, but the test double
never raises them (intentional — the tests only call `Publish*` methods). Added
`#pragma warning disable CS0067 / #pragma warning restore CS0067` around those declarations.

### 3.3 Placeholder tests marked `[Ignore]`

`Test1.cs` in all three test projects contained empty `TestMethod1()` methods that
trivially pass without verifying any behaviour. The methods are now annotated with
`[Ignore("Placeholder — replace with real tests.")]` so they appear as "Skipped"
rather than silently passing.

---

## 4. Coverage Gaps — Classes with 0% or Very Low Coverage

These classes belong to **module projects** (`src/`) and are **not covered** by the
current test suite. No source changes were made; the gaps are recorded here for backlog.

### 4.1 BrokerageMonitor.Application (0% classes)

| Class                                                                                                                                                                                                                                           | Notes                                                                                |
| ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------ |
| `ApplicationServiceCollectionExtensions`                                                                                                                                                                                                        | DI wiring — integration test needed                                                  |
| `NullAuditLogger`                                                                                                                                                                                                                               | Null object — consider a dedicated null-guard contract test                          |
| `NullEmailNotificationService`                                                                                                                                                                                                                  | Same as above                                                                        |
| `NullHeartbeatTimerRegistry`                                                                                                                                                                                                                    | Same as above                                                                        |
| `NullTeamsNotificationService`                                                                                                                                                                                                                  | Same as above                                                                        |
| `GetHealthDefinitionsQuery` / `GetHealthDefinitionsQueryHandler`                                                                                                                                                                                | Query handler entirely untested; add unit tests for the health definitions read path |
| Various DTO records (`AlertDto`, `DailyExecutionDto`, `HealthMonitorDefinitionDto`, `NotificationInboxItemDto`, `WatchedComponentDto`, `OverrideComponentStateRequest`, `ToggleMaintenanceModeRequest`, `AcknowledgeAlertRequest`, `Result<T>`) | Pure data bags — coverage increases organically once mapping tests are added         |

### 4.2 BrokerageMonitor.Domain (0% or low)

| Class                                                             | Coverage | Notes                                                                                                                                         |
| ----------------------------------------------------------------- | -------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| `AggregateHealthDeadlineReached` event                            | 0%       | Domain event — never constructed in tests; test indirectly via handler                                                                        |
| `AlertAcknowledged` event                                         | 0%       | Raised by `AlertRecord.Acknowledge()`; the domain test for acknowledge verifies the aggregate, but the event object itself has no direct test |
| `DailyExecutionCompleted/Created/Exempted/Missed` events          | 0%       | Raised inside `DailyExecution` aggregate; aggregate tests cover state but event objects carry 0%                                              |
| `HealthNotificationSent` event                                    | 0%       | Not fired in current tests                                                                                                                    |
| `MaintenanceModeToggled` event                                    | 0%       | Not fired in current tests                                                                                                                    |
| `ComponentHeartbeatReceived` event                                | 40%      | Partially tested                                                                                                                              |
| `AlertTriggered` / `MailChannelMessageReceived` events            | 33%      | Partially tested                                                                                                                              |
| `NotificationInboxItem` aggregate                                 | 77.5%    | Missing branch tests for `MarkAsRead` edge cases                                                                                              |
| `MetricValue` / `SubIndicator` / `WatchedComponent` value objects | 67–71%   | Missing equality/hash-code branch tests                                                                                                       |

### 4.3 BrokerageMonitor.Infrastructure (0% classes)

| Class                                       | Coverage | Notes                                                                                        |
| ------------------------------------------- | -------- | -------------------------------------------------------------------------------------------- |
| `MonitorHub` (SignalR hub)                  | 0%       | Hub methods are thin; consider an integration test with `TestServer`                         |
| `QuartzHealthJobScheduler`                  | 0%       | No unit test for DI-based Quartz scheduler setup                                             |
| `SmokeTestJob`                              | 0%       | No tests at all                                                                              |
| `ZeroMQSubscriberService`                   | 0%       | Hard to unit-test (depends on real ZeroMQ socket); mock `NetMQ` or exclude from coverage     |
| `ZeroMqOptions`                             | 0%       | Plain POCO — covered by any integration test that reads config                               |
| `InfrastructureServiceCollectionExtensions` | 41%      | Partial DI wiring tests                                                                      |
| `HealthMonitorDefinitionRepository`         | 26.3%    | **Critical gap** — only `GetAllAsync` is tested; `GetByIdAsync`, `UpsertAsync` have no tests |

### 4.4 BrokerageMonitor.Web (0.6%)

The Web project (Blazor components) has near-zero coverage. `bUnit` or Playwright tests
would be required to exercise components. `OperatorSessionService` is the only covered
class (100%), but all Razor components, pages, and the startup class are at 0%.

---

## 5. Logical / Correctness Observations

### 5.1 `EvaluateAsync_NonAlertableStatus_NoAlertCreated` — Missing `Failed` status in DataRow

`AlertEvaluationServiceTests.cs` includes `ComponentStatus.Failed` in the "non-alertable"
DataRow list:

```csharp
[DataRow(ComponentStatus.Failed)]
```

However, looking at `AlertEvaluationService`, `ComponentStatus.Failed` may or may not be
treated as alertable depending on the implementation. **Verify** that `Failed` is
intentionally non-alertable per spec before confirming this test is correct.
*(No source change made — needs spec clarification.)*

### 5.2 `MonitorBroadcaster` service — 27.2% coverage

`MonitorBroadcaster` is the in-process event hub between Application and Infrastructure
layers. Only the happy-path notification path is exercised. Exception-in-subscriber
branches are not tested; this is the lowest-coverage non-null Application service.

### 5.3 `HeartbeatTimeoutMonitor` — 63.5% coverage

The timeout monitor's internal timer callback branches (e.g., component-lost path when
heartbeat is never received after registration) have partial branch coverage. The async
timer path is exercised but the `ScheduledJob` vs `Service` distinction branches are not
all covered.

### 5.4 `HealthMonitorDefinitionRepository` — 26.3% (critical)

Only `GetAllAsync` is tested. `GetByIdAsync` and `UpsertAsync` are exercised by the
`UpsertHealthMonitorDefinitionHandlerTests` (Application layer), but those tests use
in-memory fakes, **not** the real repository. Add repository integration tests in
`RepositoryIntegrationTests.cs` for this class (similar to the other repository tests).

---

## 6. NuGet / Infrastructure Notes (Not Code Issues)

- `MailAgent` project references `Microsoft.Office.Interop.Outlook` via a .NET Framework
  fallback (NU1603 / NU1701). This is a known limitation of the COM interop package and
  does not affect test execution or production code correctness.
