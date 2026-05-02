using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Events;
using BrokerageMonitor.Domain.Repositories;
using BrokerageMonitor.Domain.ValueObjects;
using BrokerageMonitor.Infrastructure.Monitoring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrokerageMonitor.Infrastructure.Tests.Monitoring;

[TestClass]
public sealed class HeartbeatTimeoutMonitorTests
{
    // ---------------------------------------------------------------
    // ShouldRaiseComponentLost — pure logic tests (no DI required)
    // ---------------------------------------------------------------

    [TestMethod]
    [DataRow(ComponentType.Service, ComponentStatus.Normal, true)]
    [DataRow(ComponentType.Service, ComponentStatus.Warning, true)]
    [DataRow(ComponentType.Service, ComponentStatus.Error, true)]
    [DataRow(ComponentType.Service, ComponentStatus.Lost, true)]
    [DataRow(ComponentType.Service, ComponentStatus.Unknown, true)]
    [DataRow(ComponentType.Service, ComponentStatus.Running, true)]
    [DataRow(ComponentType.Service, ComponentStatus.Idle, true)]
    [DataRow(ComponentType.Service, ComponentStatus.Stopped, false)]
    [DataRow(ComponentType.Service, ComponentStatus.Maintenance, true)]
    public void ShouldRaiseComponentLost_ServiceComponent_CorrectResult(
        ComponentType type, ComponentStatus status, bool expected)
    {
        var result = HeartbeatTimeoutMonitor.ShouldRaiseComponentLost(type, status);
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [DataRow(ComponentType.ScheduledJob, ComponentStatus.Running, true)]
    [DataRow(ComponentType.ScheduledJob, ComponentStatus.Normal, false)]
    [DataRow(ComponentType.ScheduledJob, ComponentStatus.Idle, false)]
    [DataRow(ComponentType.ScheduledJob, ComponentStatus.Unknown, false)]
    [DataRow(ComponentType.ScheduledJob, ComponentStatus.Stopped, false)]
    [DataRow(ComponentType.ScheduledJob, ComponentStatus.Lost, false)]
    [DataRow(ComponentType.ScheduledJob, ComponentStatus.Completed, false)]
    [DataRow(ComponentType.ScheduledJob, ComponentStatus.Failed, false)]
    public void ShouldRaiseComponentLost_ScheduledJobComponent_CorrectResult(
        ComponentType type, ComponentStatus status, bool expected)
    {
        var result = HeartbeatTimeoutMonitor.ShouldRaiseComponentLost(type, status);
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void ShouldRaiseComponentLost_ServiceWithNullStatus_ReturnsTrue()
    {
        var result = HeartbeatTimeoutMonitor.ShouldRaiseComponentLost(ComponentType.Service, null);
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void ShouldRaiseComponentLost_ScheduledJobWithNullStatus_ReturnsFalse()
    {
        var result = HeartbeatTimeoutMonitor.ShouldRaiseComponentLost(ComponentType.ScheduledJob, null);
        Assert.IsFalse(result);
    }

    // ---------------------------------------------------------------
    // RegisterComponent / ResetTimer / UnregisterComponent
    // ---------------------------------------------------------------

    private static HeartbeatTimeoutMonitor CreateMonitor(
        IServiceProvider? serviceProvider = null)
    {
        var scopeFactory = serviceProvider is not null
            ? serviceProvider.GetRequiredService<IServiceScopeFactory>()
            : new ServiceCollection()
                .AddLogging()
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>();

        return new HeartbeatTimeoutMonitor(scopeFactory, TimeProvider.System, NullLogger<HeartbeatTimeoutMonitor>.Instance);
    }

    [TestMethod]
    public void RegisterComponent_ValidComponent_DoesNotThrow()
    {
        var monitor = CreateMonitor();

        // Should complete without exception
        monitor.RegisterComponent("comp-01", "sys-01", ComponentType.Service, 30);
    }

    [TestMethod]
    public void RegisterComponent_SameComponentTwice_ReplacesExistingTimer()
    {
        var monitor = CreateMonitor();

        // Register once
        monitor.RegisterComponent("comp-01", "sys-01", ComponentType.Service, 30);
        // Register again — should not throw and should replace
        monitor.RegisterComponent("comp-01", "sys-01", ComponentType.Service, 60);

        // No exception = test passes
    }

    [TestMethod]
    public void ResetTimer_RegisteredComponent_DoesNotThrow()
    {
        var monitor = CreateMonitor();
        monitor.RegisterComponent("comp-01", "sys-01", ComponentType.Service, 30);

        // Should not throw
        monitor.ResetTimer("comp-01");
    }

    [TestMethod]
    public void ResetTimer_UnknownComponent_DoesNotThrow()
    {
        var monitor = CreateMonitor();

        // Calling reset on a non-registered component should be a no-op, not an exception
        monitor.ResetTimer("non-existent");
    }

    [TestMethod]
    public void UnregisterComponent_RegisteredComponent_DoesNotThrow()
    {
        var monitor = CreateMonitor();
        monitor.RegisterComponent("comp-01", "sys-01", ComponentType.Service, 30);

        monitor.UnregisterComponent("comp-01");
    }

    [TestMethod]
    public void UnregisterComponent_UnknownComponent_DoesNotThrow()
    {
        var monitor = CreateMonitor();

        // Should be a no-op
        monitor.UnregisterComponent("non-existent");
    }

    // ---------------------------------------------------------------
    // Timer fires → ComponentLost dispatched (short-timeout integration)
    // ---------------------------------------------------------------

    [TestMethod]
    [Timeout(5000, CooperativeCancellation = true)]
    public async Task Timer_WhenServiceComponentInNormalState_DispatchesComponentLost()
    {
        // Arrange
        var dispatcherSpy = new FakeDomainEventDispatcher();
        var stateRepo = new FakeComponentStateRepository(ComponentStatus.Normal);
        var componentRepo = new FakeMonitoredComponentRepository([]);

        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventDispatcher>(dispatcherSpy);
        services.AddSingleton<IComponentStateRepository>(stateRepo);
        services.AddSingleton<IMonitoredComponentRepository>(componentRepo);

        var sp = services.BuildServiceProvider();
        var monitor = CreateMonitor(sp);

        // Register with a 100 ms timeout so the test stays fast
        monitor.RegisterComponent("comp-01", "sys-01", ComponentType.Service, 0);

        // Act — wait up to 2 s for the event to be dispatched (timer fires in ~100 ms)
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!dispatcherSpy.HasReceived<ComponentLost>() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50).ConfigureAwait(false);
        }

        // Assert
        Assert.IsTrue(dispatcherSpy.HasReceived<ComponentLost>(),
            "ComponentLost should have been dispatched within 2 s.");
    }

    [TestMethod]
    [Timeout(5000, CooperativeCancellation = true)]
    public async Task Timer_WhenServiceComponentInStoppedState_DoesNotDispatchComponentLost()
    {
        // Arrange
        var dispatcherSpy = new FakeDomainEventDispatcher();
        var stateRepo = new FakeComponentStateRepository(ComponentStatus.Stopped);
        var componentRepo = new FakeMonitoredComponentRepository([]);

        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventDispatcher>(dispatcherSpy);
        services.AddSingleton<IComponentStateRepository>(stateRepo);
        services.AddSingleton<IMonitoredComponentRepository>(componentRepo);

        var sp = services.BuildServiceProvider();
        var monitor = CreateMonitor(sp);

        monitor.RegisterComponent("comp-01", "sys-01", ComponentType.Service, 0);

        // Act — wait 500 ms and ensure no event fired
        await Task.Delay(500).ConfigureAwait(false);

        // Assert
        Assert.IsFalse(dispatcherSpy.HasReceived<ComponentLost>(),
            "ComponentLost should NOT be dispatched when service is Stopped.");
    }

    [TestMethod]
    [Timeout(5000, CooperativeCancellation = true)]
    public async Task Timer_WhenScheduledJobNotInRunning_DoesNotDispatchComponentLost()
    {
        // Arrange
        var dispatcherSpy = new FakeDomainEventDispatcher();
        var stateRepo = new FakeComponentStateRepository(ComponentStatus.Idle);
        var componentRepo = new FakeMonitoredComponentRepository([]);

        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventDispatcher>(dispatcherSpy);
        services.AddSingleton<IComponentStateRepository>(stateRepo);
        services.AddSingleton<IMonitoredComponentRepository>(componentRepo);

        var sp = services.BuildServiceProvider();
        var monitor = CreateMonitor(sp);

        monitor.RegisterComponent("job-01", "sys-01", ComponentType.ScheduledJob, 0);

        // Act
        await Task.Delay(500).ConfigureAwait(false);

        // Assert
        Assert.IsFalse(dispatcherSpy.HasReceived<ComponentLost>(),
            "ComponentLost should NOT be dispatched when ScheduledJob is not Running.");
    }

    // ---------------------------------------------------------------
    // Fake collaborators
    // ---------------------------------------------------------------

    private sealed class FakeDomainEventDispatcher : IDomainEventDispatcher
    {
        private readonly List<IDomainEvent> _received = [];

        public bool HasReceived<T>() where T : IDomainEvent =>
            _received.OfType<T>().Any();

        public Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default)
            where TEvent : IDomainEvent
        {
            _received.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeComponentStateRepository : IComponentStateRepository
    {
        private readonly ComponentStatus _status;

        public FakeComponentStateRepository(ComponentStatus status)
        {
            _status = status;
        }

        public Task<ComponentState?> GetByComponentIdAsync(string componentId, CancellationToken ct = default)
        {
            var state = new ComponentState(componentId, _status);
            return Task.FromResult<ComponentState?>(state);
        }

        public Task<IReadOnlyList<ComponentState>> GetByComponentIdsAsync(
            IEnumerable<string> componentIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ComponentState>>([]);

        public Task<IReadOnlyList<ComponentState>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ComponentState>>([]);

        public Task UpsertAsync(ComponentState state, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeMonitoredComponentRepository : IMonitoredComponentRepository
    {
        private readonly IReadOnlyList<MonitoredComponent> _components;

        public FakeMonitoredComponentRepository(IReadOnlyList<MonitoredComponent> components)
        {
            _components = components;
        }

        public Task<MonitoredComponent?> GetByIdAsync(string componentId, CancellationToken ct = default) =>
            Task.FromResult<MonitoredComponent?>(null);

        public Task<IReadOnlyList<MonitoredComponent>> GetBySystemIdAsync(
            string systemId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MonitoredComponent>>([]);

        public Task<IReadOnlyList<MonitoredComponent>> GetAllActiveAsync(CancellationToken ct = default) =>
            Task.FromResult(_components);

        public Task UpsertAsync(MonitoredComponent component, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
