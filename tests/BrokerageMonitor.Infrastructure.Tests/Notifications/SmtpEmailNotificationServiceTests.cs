using BrokerageMonitor.Application.Notifications;
using BrokerageMonitor.Domain.ValueObjects;
using BrokerageMonitor.Infrastructure.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BrokerageMonitor.Infrastructure.Tests.Notifications;

[TestClass]
public sealed class SmtpEmailNotificationServiceTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static SmtpEmailNotificationService CreateService(
        string host = "127.0.0.1", int port = 65321) =>
        new(
            Options.Create(new SmtpOptions
            {
                Host = host,
                Port = port,
                EnableSsl = false,
                FromAddress = "monitor@test.local",
                FromDisplayName = "Test Monitor"
            }),
            NullLogger<SmtpEmailNotificationService>.Instance);

    private static AlertEmailRequest CreateAlertRequest(
        IReadOnlyList<string>? recipients = null) =>
        new(
            AlertId: Guid.NewGuid(),
            SystemId: "SYS-001",
            SystemName: "Test System",
            ComponentId: "COMP-001",
            ComponentName: "Test Component",
            AlertStatus: ComponentStatus.Lost,
            OccurredAt: DateTimeOffset.UtcNow,
            Recipients: recipients ?? ["recipient@test.local"]);

    private static HealthSummaryEmailRequest CreateHealthRequest(
        IReadOnlyList<string>? recipients = null,
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
            Recipients: recipients ?? ["recipient@test.local"]);

    // -----------------------------------------------------------------------
    // SendAlertAsync — guard tests
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task SendAlertAsync_NullRequest_ThrowsArgumentNullException()
    {
        var service = CreateService();

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => service.SendAlertAsync(null!));
    }

    [TestMethod]
    public async Task SendAlertAsync_NoRecipients_CompletesWithoutSendingEmail()
    {
        // Arrange — no recipients means the SMTP path is never entered
        var service = CreateService();
        var request = CreateAlertRequest(recipients: []);

        // Act & Assert — should complete without throwing
        await service.SendAlertAsync(request);
    }

    // -----------------------------------------------------------------------
    // SendAlertAsync — SMTP failure
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task SendAlertAsync_WhenSmtpUnavailable_ThrowsEmailDeliveryException()
    {
        // Arrange — loopback port 65321 is never open; connection refused comes back immediately
        var service = CreateService(host: "127.0.0.1", port: 65321);
        var request = CreateAlertRequest();

        // Act & Assert
        await Assert.ThrowsAsync<EmailDeliveryException>(
            () => service.SendAlertAsync(request));
    }

    // -----------------------------------------------------------------------
    // SendHealthSummaryAsync — guard tests
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task SendHealthSummaryAsync_NullRequest_ThrowsArgumentNullException()
    {
        var service = CreateService();

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            () => service.SendHealthSummaryAsync(null!));
    }

    [TestMethod]
    public async Task SendHealthSummaryAsync_NoRecipients_CompletesWithoutSendingEmail()
    {
        var service = CreateService();
        var request = CreateHealthRequest(recipients: []);

        await service.SendHealthSummaryAsync(request);
    }

    // -----------------------------------------------------------------------
    // SendHealthSummaryAsync — SMTP failure
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task SendHealthSummaryAsync_WhenSmtpUnavailable_ThrowsEmailDeliveryException()
    {
        var service = CreateService(host: "127.0.0.1", port: 65321);
        var request = CreateHealthRequest();

        await Assert.ThrowsAsync<EmailDeliveryException>(
            () => service.SendHealthSummaryAsync(request));
    }

    // -----------------------------------------------------------------------
    // SendHealthSummaryAsync — all status labels (guard path only, no SMTP)
    // -----------------------------------------------------------------------

    [TestMethod]
    [DataRow(DailyExecutionStatus.Success)]
    [DataRow(DailyExecutionStatus.Failed)]
    [DataRow(DailyExecutionStatus.Exempted)]
    [DataRow(DailyExecutionStatus.Missed)]
    public async Task SendHealthSummaryAsync_NoRecipients_AllStatuses_DoesNotThrow(
        DailyExecutionStatus status)
    {
        var service = CreateService();
        // No recipients → SMTP is never called; tests the status-label switch only
        var request = CreateHealthRequest(recipients: [], status: status);

        await service.SendHealthSummaryAsync(request);
    }

    [TestMethod]
    public async Task SendHealthSummaryAsync_WithFailedComponents_WhenSmtpUnavailable_ThrowsEmailDeliveryException()
    {
        var service = CreateService(host: "127.0.0.1", port: 65321);
        var request = CreateHealthRequest(
            status: DailyExecutionStatus.Failed,
            failedComponents: ["COMP-001", "COMP-002"]);

        await Assert.ThrowsAsync<EmailDeliveryException>(
            () => service.SendHealthSummaryAsync(request));
    }
}
