using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Tests;

[TestClass]
public sealed class ComponentStateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 4, 25, 10, 0, 0, TimeSpan.Zero);

    // ── Rehydrate factory ─────────────────────────────────────────────────────

    [TestMethod]
    public void Rehydrate_ValidArguments_RestoresAllFields()
    {
        var lastHeartbeat = T0.AddMinutes(-1);
        var subIndicators = new[] { new SubIndicator("db", SubIndicatorStatus.Normal, null) };

        var state = ComponentState.Rehydrate(
            componentId: "COMP-01",
            status: ComponentStatus.Normal,
            lastStatusChangedAt: T0,
            lastHeartbeatAt: lastHeartbeat,
            subIndicators: subIndicators);

        Assert.AreEqual("COMP-01", state.ComponentId);
        Assert.AreEqual(ComponentStatus.Normal, state.Status);
        Assert.AreEqual(T0, state.LastStatusChangedAt);
        Assert.AreEqual(lastHeartbeat, state.LastHeartbeatAt);
        Assert.HasCount(1, state.SubIndicators);
        Assert.AreEqual("db", state.SubIndicators[0].Name);
    }

    [TestMethod]
    public void Rehydrate_DoesNotUseUtcNow_LastStatusChangedAtMatchesInput()
    {
        // If the ctor used UtcNow this would be a different (later) timestamp.
        var historicTimestamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var state = ComponentState.Rehydrate(
            "COMP-02",
            ComponentStatus.Error,
            historicTimestamp,
            lastHeartbeatAt: null);

        Assert.AreEqual(historicTimestamp, state.LastStatusChangedAt);
    }

    [TestMethod]
    public void Rehydrate_NullSubIndicators_ReturnsEmptyList()
    {
        var state = ComponentState.Rehydrate("COMP-03", ComponentStatus.Unknown, T0, null);

        Assert.IsEmpty(state.SubIndicators);
    }

    [TestMethod]
    public void Rehydrate_EmptyComponentId_ThrowsArgumentException()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => ComponentState.Rehydrate("", ComponentStatus.Unknown, T0, null));
    }

    [TestMethod]
    public void Rehydrate_WhitespaceComponentId_ThrowsArgumentException()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => ComponentState.Rehydrate("   ", ComponentStatus.Unknown, T0, null));
    }

    // ── Public constructor ────────────────────────────────────────────────────

    [TestMethod]
    public void Constructor_ValidComponentId_DefaultsToUnknown()
    {
        var state = new ComponentState("COMP-04");

        Assert.AreEqual("COMP-04", state.ComponentId);
        Assert.AreEqual(ComponentStatus.Unknown, state.Status);
        Assert.IsNull(state.LastHeartbeatAt);
        Assert.IsEmpty(state.SubIndicators);
    }

    [TestMethod]
    public void Constructor_WithInitialStatus_SetsStatus()
    {
        var state = new ComponentState("COMP-05", ComponentStatus.Running);

        Assert.AreEqual(ComponentStatus.Running, state.Status);
    }

    // ── UpdateStatus ──────────────────────────────────────────────────────────

    [TestMethod]
    public void UpdateStatus_SetsStatusAndTimestamp()
    {
        var state = new ComponentState("COMP-06");

        state.UpdateStatus(ComponentStatus.Normal, T0);

        Assert.AreEqual(ComponentStatus.Normal, state.Status);
        Assert.AreEqual(T0, state.LastStatusChangedAt);
    }
}
