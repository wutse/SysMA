using BrokerageMonitor.Application.Messaging;
using BrokerageMonitor.Application.Services;
using BrokerageMonitor.Domain.Aggregates;
using BrokerageMonitor.Domain.Events;
using BrokerageMonitor.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrokerageMonitor.Application.Tests.Services;

[TestClass]
public sealed class MailChannelProcessorTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static MonitoredComponent MakeServiceComponent(
        string id = "svc01",
        string systemId = "SYS",
        MailParsingRule? rule = null) =>
        new(
            componentId: id,
            systemId: systemId,
            name: "Service Component",
            componentType: ComponentType.Service,
            zeroMQTopic: "sys.service.svc01",
            heartbeatTimeoutSeconds: 60,
            mailParsingRule: rule);

    private static MonitoredComponent MakeJobComponent(
        string id = "job01",
        string systemId = "SYS",
        MailParsingRule? rule = null) =>
        new(
            componentId: id,
            systemId: systemId,
            name: "Scheduled Job",
            componentType: ComponentType.ScheduledJob,
            zeroMQTopic: "sys.job.job01",
            heartbeatTimeoutSeconds: 120,
            cronExpression: "0 8 * * *",
            mailParsingRule: rule);

    private static MailParsingRule MakeRule(
        string from = "scheduler@company.local",
        string subject = "日結清算",
        string[]? success = null,
        string[]? failure = null) =>
        new(
            fromPattern: from,
            subjectPattern: subject,
            successKeywords: (success ?? ["成功", "完成"]).AsReadOnly(),
            failureKeywords: (failure ?? ["失敗", "錯誤"]).AsReadOnly());

    private static MailRelayMessage MakeMessage(
        string from = "scheduler@company.local",
        string subject = "日結清算 成功",
        string body = "執行完成") =>
        new("MailRelay", from, subject, body, DateTimeOffset.UtcNow);

    private static MailChannelProcessor CreateProcessor(
        FakeComponentRepository componentRepo,
        FakeStateRepository stateRepo,
        ComponentStateCache stateCache,
        FakeEventDispatcher dispatcher) =>
        new(
            componentRepo,
            stateRepo,
            stateCache,
            dispatcher,
            TimeProvider.System,
            NullLogger<MailChannelProcessor>.Instance);

    // -----------------------------------------------------------------------
    // ProcessAsync — happy path
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task ProcessAsync_ServiceComponent_SuccessKeyword_SetsStatusNormal()
    {
        // Arrange
        var rule = MakeRule(success: ["完成"], failure: ["失敗"]);
        var component = MakeServiceComponent(rule: rule);

        var repo = new FakeComponentRepository();
        repo.Add(component);
        var stateRepo = new FakeStateRepository();
        var cache = new ComponentStateCache();
        var dispatcher = new FakeEventDispatcher();
        var processor = CreateProcessor(repo, stateRepo, cache, dispatcher);

        var message = MakeMessage(subject: "日結清算 完成", body: "執行完成");

        // Act
        await processor.ProcessAsync(message);

        // Assert
        var state = cache.GetState("svc01");
        Assert.IsNotNull(state);
        Assert.AreEqual(ComponentStatus.Normal, state.Status);
    }

    [TestMethod]
    public async Task ProcessAsync_ServiceComponent_FailureKeyword_SetsStatusError()
    {
        // Arrange
        var rule = MakeRule(success: ["完成"], failure: ["失敗"]);
        var component = MakeServiceComponent(rule: rule);

        var repo = new FakeComponentRepository();
        repo.Add(component);
        var stateRepo = new FakeStateRepository();
        var cache = new ComponentStateCache();
        var dispatcher = new FakeEventDispatcher();
        var processor = CreateProcessor(repo, stateRepo, cache, dispatcher);

        var message = MakeMessage(subject: "日結清算 失敗", body: "執行失敗");

        // Act
        await processor.ProcessAsync(message);

        // Assert
        var state = cache.GetState("svc01");
        Assert.IsNotNull(state);
        Assert.AreEqual(ComponentStatus.Error, state.Status);
    }

    [TestMethod]
    public async Task ProcessAsync_ScheduledJobComponent_SuccessKeyword_SetsStatusCompleted()
    {
        // Arrange
        var rule = MakeRule(success: ["完成"], failure: ["失敗"]);
        var component = MakeJobComponent(rule: rule);

        var repo = new FakeComponentRepository();
        repo.Add(component);
        var stateRepo = new FakeStateRepository();
        var cache = new ComponentStateCache();
        var dispatcher = new FakeEventDispatcher();
        var processor = CreateProcessor(repo, stateRepo, cache, dispatcher);

        var message = MakeMessage(subject: "日結清算 完成");

        // Act
        await processor.ProcessAsync(message);

        // Assert
        var state = cache.GetState("job01");
        Assert.IsNotNull(state);
        Assert.AreEqual(ComponentStatus.Completed, state.Status);
    }

    [TestMethod]
    public async Task ProcessAsync_ScheduledJobComponent_FailureKeyword_SetsStatusFailed()
    {
        // Arrange
        var rule = MakeRule(success: ["完成"], failure: ["失敗"]);
        var component = MakeJobComponent(rule: rule);

        var repo = new FakeComponentRepository();
        repo.Add(component);
        var stateRepo = new FakeStateRepository();
        var cache = new ComponentStateCache();
        var dispatcher = new FakeEventDispatcher();
        var processor = CreateProcessor(repo, stateRepo, cache, dispatcher);

        var message = MakeMessage(subject: "日結清算 失敗");

        // Act
        await processor.ProcessAsync(message);

        // Assert
        var state = cache.GetState("job01");
        Assert.IsNotNull(state);
        Assert.AreEqual(ComponentStatus.Failed, state.Status);
    }

    // -----------------------------------------------------------------------
    // ProcessAsync — BI-016: failure keyword priority over success
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task ProcessAsync_BothKeywordsInBody_FailureKeywordTakesPriority()
    {
        // Arrange — body contains both a success and a failure keyword
        var rule = MakeRule(success: ["完成"], failure: ["失敗"]);
        var component = MakeServiceComponent(rule: rule);

        var repo = new FakeComponentRepository();
        repo.Add(component);
        var stateRepo = new FakeStateRepository();
        var cache = new ComponentStateCache();
        var dispatcher = new FakeEventDispatcher();
        var processor = CreateProcessor(repo, stateRepo, cache, dispatcher);

        // Both "完成" and "失敗" appear — failure must win (BI-016)
        var message = MakeMessage(subject: "日結清算 完成 but 失敗");

        // Act
        await processor.ProcessAsync(message);

        // Assert
        var state = cache.GetState("svc01");
        Assert.IsNotNull(state);
        Assert.AreEqual(ComponentStatus.Error, state.Status);
    }

    // -----------------------------------------------------------------------
    // ProcessAsync — ERR_MAIL_NO_RULE_MATCH
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task ProcessAsync_NoFromMatch_NoStateChange_NoEvent()
    {
        // Arrange
        var rule = MakeRule(from: "expected@company.local");
        var component = MakeServiceComponent(rule: rule);

        var repo = new FakeComponentRepository();
        repo.Add(component);
        var stateRepo = new FakeStateRepository();
        var cache = new ComponentStateCache();
        var dispatcher = new FakeEventDispatcher();
        var processor = CreateProcessor(repo, stateRepo, cache, dispatcher);

        // Different sender — should not match
        var message = MakeMessage(from: "other@company.local");

        // Act
        await processor.ProcessAsync(message);

        // Assert
        Assert.IsNull(cache.GetState("svc01"));
        Assert.IsFalse(dispatcher.Dispatched.OfType<ComponentStatusChanged>().Any());
    }

    [TestMethod]
    public async Task ProcessAsync_NoSubjectMatch_NoStateChange()
    {
        // Arrange
        var rule = MakeRule(subject: "日結清算");
        var component = MakeServiceComponent(rule: rule);

        var repo = new FakeComponentRepository();
        repo.Add(component);
        var stateRepo = new FakeStateRepository();
        var cache = new ComponentStateCache();
        var dispatcher = new FakeEventDispatcher();
        var processor = CreateProcessor(repo, stateRepo, cache, dispatcher);

        // Different subject — should not match
        var message = MakeMessage(subject: "完全不同主題 完成");

        // Act
        await processor.ProcessAsync(message);

        // Assert
        Assert.IsNull(cache.GetState("svc01"));
    }

    [TestMethod]
    public async Task ProcessAsync_NoKeywordMatch_NoStateChange()
    {
        // Arrange — pattern matches but neither success nor failure keyword in mail
        var rule = MakeRule(success: ["完成"], failure: ["失敗"]);
        var component = MakeServiceComponent(rule: rule);

        var repo = new FakeComponentRepository();
        repo.Add(component);
        var stateRepo = new FakeStateRepository();
        var cache = new ComponentStateCache();
        var dispatcher = new FakeEventDispatcher();
        var processor = CreateProcessor(repo, stateRepo, cache, dispatcher);

        var message = MakeMessage(subject: "日結清算 執行中", body: "無關鍵字");

        // Act
        await processor.ProcessAsync(message);

        // Assert
        Assert.IsNull(cache.GetState("svc01"));
    }

    // -----------------------------------------------------------------------
    // ProcessAsync — ComponentStatusChanged event dispatch
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task ProcessAsync_StatusChanges_DispatchesComponentStatusChangedEvent()
    {
        // Arrange
        var rule = MakeRule(success: ["完成"], failure: ["失敗"]);
        var component = MakeServiceComponent(rule: rule);

        var repo = new FakeComponentRepository();
        repo.Add(component);
        var stateRepo = new FakeStateRepository();
        var cache = new ComponentStateCache();

        // Pre-populate cache so previous status is known.
        cache.SetState(new ComponentState("svc01", ComponentStatus.Error));

        var dispatcher = new FakeEventDispatcher();
        var processor = CreateProcessor(repo, stateRepo, cache, dispatcher);

        var message = MakeMessage(subject: "日結清算 完成");

        // Act
        await processor.ProcessAsync(message);

        // Assert
        var evt = dispatcher.Dispatched.OfType<ComponentStatusChanged>().SingleOrDefault();
        Assert.IsNotNull(evt);
        Assert.AreEqual("svc01", evt.ComponentId);
        Assert.AreEqual(ComponentStatus.Error, evt.PreviousStatus);
        Assert.AreEqual(ComponentStatus.Normal, evt.NewStatus);
    }

    [TestMethod]
    public async Task ProcessAsync_StatusUnchanged_DoesNotDispatchComponentStatusChanged()
    {
        // Arrange — status is already Normal and mail resolves to Normal
        var rule = MakeRule(success: ["完成"], failure: ["失敗"]);
        var component = MakeServiceComponent(rule: rule);

        var repo = new FakeComponentRepository();
        repo.Add(component);
        var stateRepo = new FakeStateRepository();
        var cache = new ComponentStateCache();
        cache.SetState(new ComponentState("svc01", ComponentStatus.Normal));

        var dispatcher = new FakeEventDispatcher();
        var processor = CreateProcessor(repo, stateRepo, cache, dispatcher);

        var message = MakeMessage(subject: "日結清算 完成");

        // Act
        await processor.ProcessAsync(message);

        // Assert
        Assert.IsFalse(dispatcher.Dispatched.OfType<ComponentStatusChanged>().Any());
    }

    // -----------------------------------------------------------------------
    // ProcessAsync — MailChannelMessageReceived always dispatched
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task ProcessAsync_Always_DispatchesMailChannelMessageReceived()
    {
        // Arrange — no components at all → no match, but event is still raised
        var repo = new FakeComponentRepository();
        var stateRepo = new FakeStateRepository();
        var cache = new ComponentStateCache();
        var dispatcher = new FakeEventDispatcher();
        var processor = CreateProcessor(repo, stateRepo, cache, dispatcher);

        var message = MakeMessage();

        // Act
        await processor.ProcessAsync(message);

        // Assert
        Assert.IsTrue(dispatcher.Dispatched.OfType<MailChannelMessageReceived>().Any());
    }

    // -----------------------------------------------------------------------
    // Static helper unit tests: MatchesFrom
    // -----------------------------------------------------------------------

    [TestMethod]
    public void MatchesFrom_ExactMatch_ReturnsTrue()
    {
        Assert.IsTrue(MailChannelProcessor.MatchesFrom(
            "scheduler@company.local", "scheduler@company.local"));
    }

    [TestMethod]
    public void MatchesFrom_ExactMatch_CaseInsensitive_ReturnsTrue()
    {
        Assert.IsTrue(MailChannelProcessor.MatchesFrom(
            "Scheduler@Company.LOCAL", "scheduler@company.local"));
    }

    [TestMethod]
    public void MatchesFrom_WildcardStar_MatchesAnyDomain()
    {
        Assert.IsTrue(MailChannelProcessor.MatchesFrom(
            "alerts@prod.company.local", "*@*.company.local"));
    }

    [TestMethod]
    public void MatchesFrom_WildcardQuestion_MatchesSingleChar()
    {
        Assert.IsTrue(MailChannelProcessor.MatchesFrom(
            "abc@domain.local", "???@domain.local"));
    }

    [TestMethod]
    public void MatchesFrom_NoMatch_ReturnsFalse()
    {
        Assert.IsFalse(MailChannelProcessor.MatchesFrom(
            "other@external.com", "scheduler@company.local"));
    }

    // -----------------------------------------------------------------------
    // Static helper unit tests: MatchesSubject
    // -----------------------------------------------------------------------

    [TestMethod]
    public void MatchesSubject_SubstringMatch_ReturnsTrue()
    {
        Assert.IsTrue(MailChannelProcessor.MatchesSubject(
            "[Alert] 日結清算 執行完成", "日結清算"));
    }

    [TestMethod]
    public void MatchesSubject_CaseInsensitive_ReturnsTrue()
    {
        Assert.IsTrue(MailChannelProcessor.MatchesSubject(
            "settlement completed", "SETTLEMENT"));
    }

    [TestMethod]
    public void MatchesSubject_NoMatch_ReturnsFalse()
    {
        Assert.IsFalse(MailChannelProcessor.MatchesSubject(
            "Daily Report", "日結清算"));
    }

    // -----------------------------------------------------------------------
    // Static helper unit tests: MapToStatus
    // -----------------------------------------------------------------------

    [TestMethod]
    [DataRow(ComponentType.Service, true, ComponentStatus.Normal)]
    [DataRow(ComponentType.Service, false, ComponentStatus.Error)]
    [DataRow(ComponentType.ScheduledJob, true, ComponentStatus.Completed)]
    [DataRow(ComponentType.ScheduledJob, false, ComponentStatus.Failed)]
    public void MapToStatus_VariousInputs_ReturnsExpectedStatus(
        ComponentType componentType,
        bool isSuccess,
        ComponentStatus expected)
    {
        Assert.AreEqual(expected, MailChannelProcessor.MapToStatus(componentType, isSuccess));
    }

    // -----------------------------------------------------------------------
    // Guard clauses
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task ProcessAsync_NullMessage_ThrowsArgumentNullException()
    {
        // Arrange
        var processor = CreateProcessor(
            new FakeComponentRepository(),
            new FakeStateRepository(),
            new ComponentStateCache(),
            new FakeEventDispatcher());

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => processor.ProcessAsync(null!));
    }
}
