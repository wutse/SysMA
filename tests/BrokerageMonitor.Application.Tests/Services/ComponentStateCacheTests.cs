using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Application.Tests.Services;

[TestClass]
public sealed class ComponentStateCacheTests
{
    [TestMethod]
    public void GetState_UnknownComponent_ReturnsNull()
    {
        var cache = new ComponentStateCache();
        Assert.IsNull(cache.GetState("UNKNOWN"));
    }

    [TestMethod]
    public void SetState_ThenGetState_ReturnsSameInstance()
    {
        var cache = new ComponentStateCache();
        var state = new ComponentState("COMP-01", ComponentStatus.Normal);

        cache.SetState(state);

        Assert.AreSame(state, cache.GetState("COMP-01"));
    }

    [TestMethod]
    public void SetState_OverwritesPreviousEntry()
    {
        var cache = new ComponentStateCache();
        var first = new ComponentState("COMP-01", ComponentStatus.Normal);
        var second = new ComponentState("COMP-01", ComponentStatus.Warning);

        cache.SetState(first);
        cache.SetState(second);

        Assert.AreSame(second, cache.GetState("COMP-01"));
    }

    [TestMethod]
    public void GetAllStates_ReturnsAllEntries()
    {
        var cache = new ComponentStateCache();
        cache.SetState(new ComponentState("C1"));
        cache.SetState(new ComponentState("C2"));
        cache.SetState(new ComponentState("C3"));

        Assert.HasCount(3, cache.GetAllStates());
    }

    [TestMethod]
    public void LoadAll_ReplacesExistingEntries()
    {
        var cache = new ComponentStateCache();
        cache.SetState(new ComponentState("OLD-01"));

        var newStates = new[]
        {
            new ComponentState("NEW-01"),
            new ComponentState("NEW-02")
        };

        cache.LoadAll(newStates);

        Assert.IsNull(cache.GetState("OLD-01"));
        Assert.IsNotNull(cache.GetState("NEW-01"));
        Assert.IsNotNull(cache.GetState("NEW-02"));
    }

    [TestMethod]
    public void LoadAll_NullArgument_ThrowsArgumentNullException()
    {
        var cache = new ComponentStateCache();
        Assert.ThrowsExactly<ArgumentNullException>(() => cache.LoadAll(null!));
    }

    [TestMethod]
    public void SetState_NullArgument_ThrowsArgumentNullException()
    {
        var cache = new ComponentStateCache();
        Assert.ThrowsExactly<ArgumentNullException>(() => cache.SetState(null!));
    }
}
