using BrokerageMonitor.Domain.ValueObjects;

namespace BrokerageMonitor.Domain.Tests;

// ---------------------------------------------------------------------------
// MetricValue
// ---------------------------------------------------------------------------

[TestClass]
public sealed class MetricValueTests
{
  // Guard clauses

  [TestMethod]
  [DataRow("")]
  [DataRow("   ")]
  public void Constructor_BlankLabel_ThrowsArgumentException(string label)
  {
    Assert.ThrowsExactly<ArgumentException>(() => _ = new MetricValue(label, 1m));
  }

  // Property mapping

  [TestMethod]
  public void Constructor_ValidArguments_SetsPropertiesCorrectly()
  {
    var mv = new MetricValue("CPU", 42.5m);

    Assert.AreEqual("CPU", mv.Label);
    Assert.AreEqual(42.5m, mv.Value);
  }

  // Equality — same values

  [TestMethod]
  public void Equals_SameLabelAndValue_ReturnsTrue()
  {
    var a = new MetricValue("CPU", 42.5m);
    var b = new MetricValue("CPU", 42.5m);

    Assert.AreEqual(a, b);
    Assert.IsTrue(a.Equals((object)b));
  }

  // Equality — different label

  [TestMethod]
  public void Equals_DifferentLabel_ReturnsFalse()
  {
    var a = new MetricValue("CPU", 42.5m);
    var b = new MetricValue("MEM", 42.5m);

    Assert.AreNotEqual(a, b);
  }

  // Equality — different value

  [TestMethod]
  public void Equals_DifferentValue_ReturnsFalse()
  {
    var a = new MetricValue("CPU", 42.5m);
    var b = new MetricValue("CPU", 0m);

    Assert.AreNotEqual(a, b);
  }

  // Equality — null

  [TestMethod]
  public void Equals_Null_ReturnsFalse()
  {
    var a = new MetricValue("CPU", 1m);

    Assert.IsFalse(a.Equals((MetricValue?)null));
    Assert.IsFalse(a.Equals((object?)null));
  }

  // GetHashCode consistency

  [TestMethod]
  public void GetHashCode_EqualObjects_ReturnSameHash()
  {
    var a = new MetricValue("CPU", 42.5m);
    var b = new MetricValue("CPU", 42.5m);

    Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
  }

  [TestMethod]
  public void GetHashCode_DifferentObjects_ReturnDifferentHash()
  {
    var a = new MetricValue("CPU", 42.5m);
    var b = new MetricValue("MEM", 99.0m);

    // Hash collision is theoretically possible but extremely unlikely for these values
    Assert.AreNotEqual(a.GetHashCode(), b.GetHashCode());
  }
}

// ---------------------------------------------------------------------------
// SubIndicator
// ---------------------------------------------------------------------------

[TestClass]
public sealed class SubIndicatorTests
{
  // Guard clause

  [TestMethod]
  [DataRow("")]
  [DataRow("   ")]
  public void Constructor_BlankName_ThrowsArgumentException(string name)
  {
    Assert.ThrowsExactly<ArgumentException>(() =>
        _ = new SubIndicator(name, SubIndicatorStatus.Normal));
  }

  // Property mapping — without metric

  [TestMethod]
  public void Constructor_WithoutMetric_SetsPropertiesCorrectly()
  {
    var si = new SubIndicator("Latency", SubIndicatorStatus.Normal);

    Assert.AreEqual("Latency", si.Name);
    Assert.AreEqual(SubIndicatorStatus.Normal, si.Status);
    Assert.IsNull(si.Metric);
  }

  // Property mapping — with metric

  [TestMethod]
  public void Constructor_WithMetric_SetsMetricCorrectly()
  {
    var metric = new MetricValue("ms", 120m);
    var si = new SubIndicator("Latency", SubIndicatorStatus.Error, metric);

    Assert.IsNotNull(si.Metric);
    Assert.AreEqual("ms", si.Metric.Label);
  }

  // Equality — same values, no metric

  [TestMethod]
  public void Equals_SameValuesWithoutMetric_ReturnsTrue()
  {
    var a = new SubIndicator("Latency", SubIndicatorStatus.Normal);
    var b = new SubIndicator("Latency", SubIndicatorStatus.Normal);

    Assert.AreEqual(a, b);
    Assert.IsTrue(a.Equals((object)b));
  }

  // Equality — same values, with metric

  [TestMethod]
  public void Equals_SameValuesWithMetric_ReturnsTrue()
  {
    var metric = new MetricValue("ms", 120m);
    var a = new SubIndicator("Latency", SubIndicatorStatus.Error, metric);
    var b = new SubIndicator("Latency", SubIndicatorStatus.Error, new MetricValue("ms", 120m));

    Assert.AreEqual(a, b);
  }

  // Equality — different name

  [TestMethod]
  public void Equals_DifferentName_ReturnsFalse()
  {
    var a = new SubIndicator("Latency", SubIndicatorStatus.Normal);
    var b = new SubIndicator("Throughput", SubIndicatorStatus.Normal);

    Assert.AreNotEqual(a, b);
  }

  // Equality — different status

  [TestMethod]
  public void Equals_DifferentStatus_ReturnsFalse()
  {
    var a = new SubIndicator("Latency", SubIndicatorStatus.Normal);
    var b = new SubIndicator("Latency", SubIndicatorStatus.Error);

    Assert.AreNotEqual(a, b);
  }

  // Equality — one has metric, other does not

  [TestMethod]
  public void Equals_OnlyOneHasMetric_ReturnsFalse()
  {
    var a = new SubIndicator("Latency", SubIndicatorStatus.Normal, new MetricValue("ms", 1m));
    var b = new SubIndicator("Latency", SubIndicatorStatus.Normal);

    Assert.AreNotEqual(a, b);
  }

  // Equality — null

  [TestMethod]
  public void Equals_Null_ReturnsFalse()
  {
    var a = new SubIndicator("Latency", SubIndicatorStatus.Normal);

    Assert.IsFalse(a.Equals((SubIndicator?)null));
    Assert.IsFalse(a.Equals((object?)null));
  }

  // GetHashCode consistency

  [TestMethod]
  public void GetHashCode_EqualObjects_ReturnSameHash()
  {
    var a = new SubIndicator("Latency", SubIndicatorStatus.Normal);
    var b = new SubIndicator("Latency", SubIndicatorStatus.Normal);

    Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
  }

  [TestMethod]
  public void GetHashCode_WithSameMetric_ReturnSameHash()
  {
    var a = new SubIndicator("Latency", SubIndicatorStatus.Error, new MetricValue("ms", 100m));
    var b = new SubIndicator("Latency", SubIndicatorStatus.Error, new MetricValue("ms", 100m));

    Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
  }
}

// ---------------------------------------------------------------------------
// WatchedComponent
// ---------------------------------------------------------------------------

[TestClass]
public sealed class WatchedComponentTests
{
  // Guard clause

  [TestMethod]
  [DataRow("")]
  [DataRow("   ")]
  public void Constructor_BlankComponentId_ThrowsArgumentException(string componentId)
  {
    Assert.ThrowsExactly<ArgumentException>(() =>
        _ = new WatchedComponent(componentId, ComponentType.Service));
  }

  // Property mapping

  [TestMethod]
  public void Constructor_ValidArguments_SetsPropertiesCorrectly()
  {
    var wc = new WatchedComponent("COMP-01", ComponentType.ScheduledJob);

    Assert.AreEqual("COMP-01", wc.ComponentId);
    Assert.AreEqual(ComponentType.ScheduledJob, wc.ComponentType);
  }

  // Equality — same values

  [TestMethod]
  public void Equals_SameIdAndType_ReturnsTrue()
  {
    var a = new WatchedComponent("COMP-01", ComponentType.Service);
    var b = new WatchedComponent("COMP-01", ComponentType.Service);

    Assert.AreEqual(a, b);
    Assert.IsTrue(a.Equals((object)b));
  }

  // Equality — different id

  [TestMethod]
  public void Equals_DifferentId_ReturnsFalse()
  {
    var a = new WatchedComponent("COMP-01", ComponentType.Service);
    var b = new WatchedComponent("COMP-02", ComponentType.Service);

    Assert.AreNotEqual(a, b);
  }

  // Equality — different type

  [TestMethod]
  public void Equals_DifferentType_ReturnsFalse()
  {
    var a = new WatchedComponent("COMP-01", ComponentType.Service);
    var b = new WatchedComponent("COMP-01", ComponentType.ScheduledJob);

    Assert.AreNotEqual(a, b);
  }

  // Equality — null

  [TestMethod]
  public void Equals_Null_ReturnsFalse()
  {
    var a = new WatchedComponent("COMP-01", ComponentType.Service);

    Assert.IsFalse(a.Equals((WatchedComponent?)null));
    Assert.IsFalse(a.Equals((object?)null));
  }

  // GetHashCode consistency

  [TestMethod]
  public void GetHashCode_EqualObjects_ReturnSameHash()
  {
    var a = new WatchedComponent("COMP-01", ComponentType.Service);
    var b = new WatchedComponent("COMP-01", ComponentType.Service);

    Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
  }

  [TestMethod]
  public void GetHashCode_DifferentObjects_ReturnDifferentHash()
  {
    var a = new WatchedComponent("COMP-01", ComponentType.Service);
    var b = new WatchedComponent("COMP-02", ComponentType.ScheduledJob);

    Assert.AreNotEqual(a.GetHashCode(), b.GetHashCode());
  }

  // HashSet deduplication (practical equality contract test)

  [TestMethod]
  public void WatchedComponent_UsedInHashSet_DeduplicatesCorrectly()
  {
    var set = new HashSet<WatchedComponent>
        {
            new("COMP-01", ComponentType.Service),
            new("COMP-01", ComponentType.Service), // duplicate
            new("COMP-02", ComponentType.Service),
        };

    Assert.HasCount(2, set);
  }
}
