using BrokerageMonitor.Domain.Events;
using BrokerageMonitor.Domain.ValueObjects;
using BrokerageMonitor.Infrastructure.Notifications;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrokerageMonitor.Infrastructure.Tests.Notifications;

// ---------------------------------------------------------------------------
// Test double: captures every SendAsync call
// ---------------------------------------------------------------------------

internal sealed class SpyClientProxy : IClientProxy
{
    public record Invocation(string Method, object?[] Args);
    private readonly List<Invocation> _calls = [];
    public IReadOnlyList<Invocation> Calls => _calls;

    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        _calls.Add(new Invocation(method, args));
        return Task.CompletedTask;
    }
}

internal sealed class SpyHubClients(SpyClientProxy all) : IHubClients
{
    IClientProxy IHubClients<IClientProxy>.All => all;
    IClientProxy IHubClients<IClientProxy>.AllExcept(IReadOnlyList<string> e) => all;
    IClientProxy IHubClients<IClientProxy>.Client(string connectionId) => all;
    ISingleClientProxy IHubClients.Client(string connectionId) => null!;
    IClientProxy IHubClients<IClientProxy>.Clients(IReadOnlyList<string> c) => all;
    IClientProxy IHubClients<IClientProxy>.Group(string g) => all;
    IClientProxy IHubClients<IClientProxy>.GroupExcept(string g, IReadOnlyList<string> e) => all;
    IClientProxy IHubClients<IClientProxy>.Groups(IReadOnlyList<string> gs) => all;
    IClientProxy IHubClients<IClientProxy>.User(string u) => all;
    IClientProxy IHubClients<IClientProxy>.Users(IReadOnlyList<string> us) => all;
}

internal sealed class SpyHubContext : IHubContext<MonitorHub>
{
    public SpyClientProxy AllClients { get; } = new();
    IHubClients IHubContext<MonitorHub>.Clients => new SpyHubClients(AllClients);
    IGroupManager IHubContext<MonitorHub>.Groups => null!;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

[TestClass]
public sealed class SignalRNotificationServiceTests
{
    private readonly SpyHubContext _hubContext = new();
    private readonly SignalRNotificationService _sut;

    public SignalRNotificationServiceTests()
    {
        _sut = new SignalRNotificationService(
            _hubContext,
            NullLogger<SignalRNotificationService>.Instance);
    }

    // -----------------------------------------------------------------------
    // NotifyComponentStatusChangedAsync
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task NotifyComponentStatusChangedAsync_ValidEvent_SendsOnComponentStatusChanged()
    {
        // Arrange
        var evt = new ComponentStatusChanged(
            ComponentId: "comp-1",
            SystemId: "sys-1",
            PreviousStatus: ComponentStatus.Running,
            NewStatus: ComponentStatus.Warning,
            OccurredAt: DateTimeOffset.UtcNow);

        // Act
        await _sut.NotifyComponentStatusChangedAsync(evt);

        // Assert
        var call = _hubContext.AllClients.Calls.Single();
        Assert.AreEqual("OnComponentStatusChanged", call.Method);
    }

    [TestMethod]
    public async Task NotifyComponentStatusChangedAsync_NullEvent_ThrowsArgumentNullException()
    {
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => _sut.NotifyComponentStatusChangedAsync(null!));
    }

    // -----------------------------------------------------------------------
    // NotifyAlertTriggeredAsync
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task NotifyAlertTriggeredAsync_ValidIds_SendsOnAlertTriggered()
    {
        // Act
        await _sut.NotifyAlertTriggeredAsync("sys-1", "comp-1");

        // Assert
        var call = _hubContext.AllClients.Calls.Single();
        Assert.AreEqual("OnAlertTriggered", call.Method);
    }

    // -----------------------------------------------------------------------
    // NotifyAlertAcknowledgedAsync
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task NotifyAlertAcknowledgedAsync_ValidSystemId_SendsOnAlertAcknowledged()
    {
        // Act
        await _sut.NotifyAlertAcknowledgedAsync("sys-1");

        // Assert
        var call = _hubContext.AllClients.Calls.Single();
        Assert.AreEqual("OnAlertAcknowledged", call.Method);
    }

    // -----------------------------------------------------------------------
    // NotifyMaintenanceModeChangedAsync
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task NotifyMaintenanceModeChangedAsync_Activate_SendsOnMaintenanceModeChanged()
    {
        // Act
        await _sut.NotifyMaintenanceModeChangedAsync("sys-1", isActive: true);

        // Assert
        var call = _hubContext.AllClients.Calls.Single();
        Assert.AreEqual("OnMaintenanceModeChanged", call.Method);
    }

    [TestMethod]
    public async Task NotifyMaintenanceModeChangedAsync_Deactivate_SendsOnMaintenanceModeChanged()
    {
        // Act
        await _sut.NotifyMaintenanceModeChangedAsync("sys-1", isActive: false);

        // Assert
        var call = _hubContext.AllClients.Calls.Single();
        Assert.AreEqual("OnMaintenanceModeChanged", call.Method);
    }

    // -----------------------------------------------------------------------
    // NotifyDailyExecutionUpdatedAsync
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task NotifyDailyExecutionUpdatedAsync_ValidId_SendsOnDailyExecutionUpdated()
    {
        // Arrange
        var executionId = Guid.NewGuid();

        // Act
        await _sut.NotifyDailyExecutionUpdatedAsync(executionId);

        // Assert
        var call = _hubContext.AllClients.Calls.Single();
        Assert.AreEqual("OnDailyExecutionUpdated", call.Method);
    }

    // -----------------------------------------------------------------------
    // Error handling: push failures must not propagate
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task NotifyAlertTriggeredAsync_HubThrows_DoesNotRethrow()
    {
        var sut = new SignalRNotificationService(
            new ThrowingHubContext(),
            NullLogger<SignalRNotificationService>.Instance);

        await sut.NotifyAlertTriggeredAsync("sys-1", "comp-1");  // no throw expected
    }
}

// ---------------------------------------------------------------------------
// Throwing test double
// ---------------------------------------------------------------------------

internal sealed class ThrowingClientProxy : IClientProxy
{
    public Task SendCoreAsync(string method, object?[] args, CancellationToken ct = default)
        => throw new InvalidOperationException("Simulated SignalR transport failure.");
}

internal sealed class ThrowingHubClients : IHubClients
{
    private readonly ThrowingClientProxy _proxy = new();
    IClientProxy IHubClients<IClientProxy>.All => _proxy;
    IClientProxy IHubClients<IClientProxy>.AllExcept(IReadOnlyList<string> e) => _proxy;
    IClientProxy IHubClients<IClientProxy>.Client(string connectionId) => _proxy;
    ISingleClientProxy IHubClients.Client(string connectionId) => null!;
    IClientProxy IHubClients<IClientProxy>.Clients(IReadOnlyList<string> c) => _proxy;
    IClientProxy IHubClients<IClientProxy>.Group(string g) => _proxy;
    IClientProxy IHubClients<IClientProxy>.GroupExcept(string g, IReadOnlyList<string> e) => _proxy;
    IClientProxy IHubClients<IClientProxy>.Groups(IReadOnlyList<string> gs) => _proxy;
    IClientProxy IHubClients<IClientProxy>.User(string u) => _proxy;
    IClientProxy IHubClients<IClientProxy>.Users(IReadOnlyList<string> us) => _proxy;
}

internal sealed class ThrowingHubContext : IHubContext<MonitorHub>
{
    IHubClients IHubContext<MonitorHub>.Clients => new ThrowingHubClients();
    IGroupManager IHubContext<MonitorHub>.Groups => null!;
}
