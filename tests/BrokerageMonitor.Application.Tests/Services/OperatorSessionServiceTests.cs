using BrokerageMonitor.Web.Services;

namespace BrokerageMonitor.Application.Tests.Services;

/// <summary>
/// Unit tests for <see cref="OperatorSessionService"/> — FR-009.
/// </summary>
[TestClass]
public sealed class OperatorSessionServiceTests
{
    private readonly OperatorSessionService _sut = new();

    // ── IsIdentified ──────────────────────────────────────────────────────────

    [TestMethod]
    public void IsIdentified_WhenNew_ReturnsFalse()
    {
        Assert.IsFalse(_sut.IsIdentified);
    }

    [TestMethod]
    public void IsIdentified_AfterIdentify_ReturnsTrue()
    {
        _sut.Identify("Alice");
        Assert.IsTrue(_sut.IsIdentified);
    }

    // ── Identify ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void Identify_ValidName_StoresOperatorName()
    {
        _sut.Identify("Bob");
        Assert.AreEqual("Bob", _sut.OperatorName);
    }

    [TestMethod]
    public void Identify_TrimsWhitespace()
    {
        _sut.Identify("  Carol  ");
        Assert.AreEqual("Carol", _sut.OperatorName);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Identify_EmptyOrWhitespace_ThrowsArgumentException(string name)
    {
        Assert.Throws<ArgumentException>(() => _sut.Identify(name));
    }

    // ── OperatorName ─────────────────────────────────────────────────────────

    [TestMethod]
    public void OperatorName_WhenNew_ReturnsNull()
    {
        Assert.IsNull(_sut.OperatorName);
    }

    // ── Clear ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Clear_AfterIdentify_ResetsIdentificationState()
    {
        _sut.Identify("Dave");
        _sut.Clear();

        Assert.IsFalse(_sut.IsIdentified);
        Assert.IsNull(_sut.OperatorName);
    }
}
