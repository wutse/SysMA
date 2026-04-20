using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Application.Tests.Services;

[TestClass]
public sealed class StateRollupServiceTests
{
    private readonly StateRollupService _sut = new();

    // ── Empty / single element ───────────────────────────────────────────────

    [TestMethod]
    public void ComputeSystemStatus_EmptyCollection_ReturnsNormal()
    {
        var result = _sut.ComputeSystemStatus([]);
        Assert.AreEqual(ComponentStatus.Normal, result);
    }

    [TestMethod]
    public void ComputeSystemStatus_SingleLost_ReturnsLost()
    {
        var result = _sut.ComputeSystemStatus([ComponentStatus.Lost]);
        Assert.AreEqual(ComponentStatus.Lost, result);
    }

    [TestMethod]
    public void ComputeSystemStatus_NullArgument_ThrowsArgumentNullException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => _sut.ComputeSystemStatus(null!));
    }

    // ── Severity order ───────────────────────────────────────────────────────

    [TestMethod]
    public void ComputeSystemStatus_LostAndNormal_ReturnsLost()
    {
        var result = _sut.ComputeSystemStatus(
            [ComponentStatus.Normal, ComponentStatus.Lost, ComponentStatus.Warning]);
        Assert.AreEqual(ComponentStatus.Lost, result);
    }

    [TestMethod]
    public void ComputeSystemStatus_ErrorAndWarning_ReturnsError()
    {
        var result = _sut.ComputeSystemStatus(
            [ComponentStatus.Warning, ComponentStatus.Error]);
        Assert.AreEqual(ComponentStatus.Error, result);
    }

    [TestMethod]
    public void ComputeSystemStatus_WarningAndUnknown_ReturnsWarning()
    {
        var result = _sut.ComputeSystemStatus(
            [ComponentStatus.Unknown, ComponentStatus.Warning]);
        Assert.AreEqual(ComponentStatus.Warning, result);
    }

    [TestMethod]
    public void ComputeSystemStatus_UnknownAndStopped_ReturnsUnknown()
    {
        var result = _sut.ComputeSystemStatus(
            [ComponentStatus.Stopped, ComponentStatus.Unknown]);
        Assert.AreEqual(ComponentStatus.Unknown, result);
    }

    [TestMethod]
    public void ComputeSystemStatus_StoppedAndIdle_ReturnsStopped()
    {
        var result = _sut.ComputeSystemStatus(
            [ComponentStatus.Idle, ComponentStatus.Stopped]);
        Assert.AreEqual(ComponentStatus.Stopped, result);
    }

    [TestMethod]
    public void ComputeSystemStatus_IdleAndRunning_ReturnsIdle()
    {
        var result = _sut.ComputeSystemStatus(
            [ComponentStatus.Running, ComponentStatus.Idle]);
        Assert.AreEqual(ComponentStatus.Idle, result);
    }

    [TestMethod]
    public void ComputeSystemStatus_RunningAndNormal_ReturnsRunning()
    {
        var result = _sut.ComputeSystemStatus(
            [ComponentStatus.Normal, ComponentStatus.Running]);
        Assert.AreEqual(ComponentStatus.Running, result);
    }

    // ── Maintenance exclusion ────────────────────────────────────────────────

    [TestMethod]
    public void ComputeSystemStatus_MaintenanceAndNormal_ReturnsNormal()
    {
        // Maintenance is excluded from rollup; Normal wins over Maintenance
        var result = _sut.ComputeSystemStatus(
            [ComponentStatus.Maintenance, ComponentStatus.Normal]);
        Assert.AreEqual(ComponentStatus.Normal, result);
    }

    [TestMethod]
    public void ComputeSystemStatus_OnlyMaintenance_ReturnsNormal()
    {
        // Even all-Maintenance yields Normal (excluded, so falls back to baseline)
        var result = _sut.ComputeSystemStatus(
            [ComponentStatus.Maintenance, ComponentStatus.Maintenance]);
        Assert.AreEqual(ComponentStatus.Normal, result);
    }

    // ── All statuses in correct order (canonical order test) ────────────────

    [TestMethod]
    public void ComputeSystemStatus_AllStatusesTogether_ReturnsLost()
    {
        var all = new[]
        {
            ComponentStatus.Normal, ComponentStatus.Running, ComponentStatus.Idle,
            ComponentStatus.Stopped, ComponentStatus.Unknown, ComponentStatus.Warning,
            ComponentStatus.Error, ComponentStatus.Lost, ComponentStatus.Maintenance
        };
        var result = _sut.ComputeSystemStatus(all);
        Assert.AreEqual(ComponentStatus.Lost, result);
    }

    // ── GetSeverity (internal) ───────────────────────────────────────────────

    [TestMethod]
    public void GetSeverity_FullOrder_DescendingFromLost()
    {
        var ordered = new[]
        {
            ComponentStatus.Lost,
            ComponentStatus.Error,
            ComponentStatus.Warning,
            ComponentStatus.Unknown,
            ComponentStatus.Stopped,
            ComponentStatus.Idle,
            ComponentStatus.Running,
            ComponentStatus.Normal
        };

        for (int i = 0; i < ordered.Length - 1; i++)
        {
            Assert.IsGreaterThan(
                StateRollupService.GetSeverity(ordered[i + 1]),
                StateRollupService.GetSeverity(ordered[i]),
                $"{ordered[i]} should be more severe than {ordered[i + 1]}");
        }
    }

    [TestMethod]
    public void GetSeverity_Failed_SameRankAsError()
    {
        Assert.AreEqual(
            StateRollupService.GetSeverity(ComponentStatus.Error),
            StateRollupService.GetSeverity(ComponentStatus.Failed));
    }

    [TestMethod]
    public void GetSeverity_Completed_SameRankAsNormal()
    {
        Assert.AreEqual(
            StateRollupService.GetSeverity(ComponentStatus.Normal),
            StateRollupService.GetSeverity(ComponentStatus.Completed));
    }
}
