using System.Net;
using BrokerageMonitor.Application.Notifications;
using BrokerageMonitor.Domain.ValueObjects;
using BrokerageMonitor.Infrastructure.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrokerageMonitor.Infrastructure.Tests.Notifications;

[TestClass]
public sealed class TeamsNotificationServiceTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static TeamsNotificationService CreateService(
        HttpStatusCode responseStatus,
        out FakeHttpMessageHandler handler)
    {
        handler = new FakeHttpMessageHandler(responseStatus);
        var httpClient = new HttpClient(handler);
        return new TeamsNotificationService(httpClient, NullLogger<TeamsNotificationService>.Instance);
    }

    private static HealthSummaryEmailRequest CreateRequest(
        DailyExecutionStatus status = DailyExecutionStatus.Success,
        IReadOnlyList<string>? failedComponents = null) =>
        new(
            DefinitionId: Guid.NewGuid(),
            ExecutionId: Guid.NewGuid(),
            DefinitionName: "Test Definition",
            SystemId: "SYS-001",
            SystemName: "Test System",
            ExecutionStatus: status,
            ExecutionDate: DateOnly.FromDateTime(DateTime.Today),
            FailedComponents: failedComponents ?? [],
            Recipients: ["unused@test.local"]);

    // -----------------------------------------------------------------------
    // Guard tests
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task SendHealthSummaryAsync_NullRequest_ThrowsArgumentNullException()
    {
        var service = CreateService(HttpStatusCode.OK, out _);

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => service.SendHealthSummaryAsync(null!, "https://webhook.test/"));
    }

    [TestMethod]
    public async Task SendHealthSummaryAsync_EmptyWebhookUrl_ThrowsArgumentException()
    {
        var service = CreateService(HttpStatusCode.OK, out _);

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => service.SendHealthSummaryAsync(CreateRequest(), ""));
    }

    [TestMethod]
    public async Task SendHealthSummaryAsync_WhitespaceWebhookUrl_ThrowsArgumentException()
    {
        var service = CreateService(HttpStatusCode.OK, out _);

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => service.SendHealthSummaryAsync(CreateRequest(), "   "));
    }

    // -----------------------------------------------------------------------
    // Success path
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task SendHealthSummaryAsync_WhenWebhookReturns200_CallsHttpPost()
    {
        var service = CreateService(HttpStatusCode.OK, out var handler);

        await service.SendHealthSummaryAsync(CreateRequest(), "https://webhook.test/incoming");

        Assert.IsTrue(handler.WasCalled, "HttpClient should have posted to the webhook URL.");
    }

    [TestMethod]
    [DataRow(DailyExecutionStatus.Success)]
    [DataRow(DailyExecutionStatus.Failed)]
    [DataRow(DailyExecutionStatus.Exempted)]
    [DataRow(DailyExecutionStatus.Missed)]
    public async Task SendHealthSummaryAsync_AllStatuses_CallsHttpPost(DailyExecutionStatus status)
    {
        var service = CreateService(HttpStatusCode.OK, out var handler);

        await service.SendHealthSummaryAsync(CreateRequest(status), "https://webhook.test/incoming");

        Assert.IsTrue(handler.WasCalled);
    }

    [TestMethod]
    public async Task SendHealthSummaryAsync_WithFailedComponents_CallsHttpPost()
    {
        var service = CreateService(HttpStatusCode.OK, out var handler);
        var request = CreateRequest(
            DailyExecutionStatus.Failed,
            failedComponents: ["COMP-001", "COMP-002"]);

        await service.SendHealthSummaryAsync(request, "https://webhook.test/incoming");

        Assert.IsTrue(handler.WasCalled);
    }

    // -----------------------------------------------------------------------
    // Failure paths
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task SendHealthSummaryAsync_WhenWebhookReturns500_ThrowsTeamsWebhookException()
    {
        var service = CreateService(HttpStatusCode.InternalServerError, out _);

        await Assert.ThrowsAsync<TeamsWebhookException>(
            () => service.SendHealthSummaryAsync(CreateRequest(), "https://webhook.test/incoming"));
    }

    [TestMethod]
    public async Task SendHealthSummaryAsync_WhenWebhookReturns400_ThrowsTeamsWebhookException()
    {
        var service = CreateService(HttpStatusCode.BadRequest, out _);

        await Assert.ThrowsAsync<TeamsWebhookException>(
            () => service.SendHealthSummaryAsync(CreateRequest(), "https://webhook.test/incoming"));
    }

    [TestMethod]
    public async Task SendHealthSummaryAsync_WhenHttpClientThrows_ThrowsTeamsWebhookException()
    {
        var httpClient = new HttpClient(new ThrowingHttpMessageHandler());
        var service = new TeamsNotificationService(
            httpClient,
            NullLogger<TeamsNotificationService>.Instance);

        await Assert.ThrowsAsync<TeamsWebhookException>(
            () => service.SendHealthSummaryAsync(CreateRequest(), "https://webhook.test/incoming"));
    }

    [TestMethod]
    public async Task SendHealthSummaryAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var httpClient = new HttpClient(new DelayingHttpMessageHandler());
        var service = new TeamsNotificationService(
            httpClient,
            NullLogger<TeamsNotificationService>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.SendHealthSummaryAsync(CreateRequest(), "https://webhook.test/incoming", cts.Token));
    }

    // -----------------------------------------------------------------------
    // Fake HTTP message handlers
    // -----------------------------------------------------------------------

    internal sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;

        public bool WasCalled { get; private set; }

        public FakeHttpMessageHandler(HttpStatusCode status = HttpStatusCode.OK)
        {
            _status = status;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            WasCalled = true;
            return Task.FromResult(new HttpResponseMessage(_status));
        }
    }

    internal sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("Simulated network failure.");
    }

    internal sealed class DelayingHttpMessageHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
