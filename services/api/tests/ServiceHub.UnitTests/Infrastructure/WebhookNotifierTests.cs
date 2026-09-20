using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure;
using ServiceHub.Infrastructure.Webhooks;

namespace ServiceHub.UnitTests.Infrastructure;

public sealed class WebhookNotifierTests
{
    private static readonly Guid TestNamespaceId = Guid.NewGuid();
    private const string TestNamespaceName = "test-ns.servicebus.windows.net";
    private static readonly Guid TestJobId = Guid.NewGuid();

    private static readonly IWebhookMessageFormatter[] AllFormatters =
    [
        new GenericWebhookFormatter(),
        new SlackWebhookFormatter(),
        new TeamsWebhookFormatter(),
    ];

    private static WebhookOptions DefaultEnabledOptions(
        string url = "https://hooks.example.com/dlq",
        WebhookFormat format = WebhookFormat.Generic,
        string? publicUrl = null) => new()
    {
        Enabled = true,
        Url = url,
        DlqSpikeThreshold = 10,
        CooldownSeconds = 300,
        Format = format,
        PublicUrl = publicUrl,
    };

    private static IOptions<WebhookOptions> Wrap(WebhookOptions opts) =>
        Options.Create(opts);

    private static WebhookNotifier CreateSut(WebhookOptions opts, FakeHttpHandler handler) =>
        new(new HttpClient(handler), Wrap(opts), AllFormatters, NullLogger<WebhookNotifier>.Instance);

    // ── Constructor ──────────────────────────────────────────

    [Fact]
    public void Constructor_NullHttpClient_Throws()
    {
        var act = () => new WebhookNotifier(null!, Wrap(DefaultEnabledOptions()), AllFormatters, NullLogger<WebhookNotifier>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        var act = () => new WebhookNotifier(new HttpClient(), null!, AllFormatters, NullLogger<WebhookNotifier>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_NullFormatters_Throws()
    {
        var act = () => new WebhookNotifier(new HttpClient(), Wrap(DefaultEnabledOptions()), null!, NullLogger<WebhookNotifier>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("formatters");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new WebhookNotifier(new HttpClient(), Wrap(DefaultEnabledOptions()), AllFormatters, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── NotifyDlqSpikeAsync — existing behavior preserved ────

    [Fact]
    public async Task NotifyDlqSpike_Disabled_ReturnsSuccessWithoutSending()
    {
        var opts = new WebhookOptions { Enabled = false, Url = "https://hooks.example.com" };
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(opts, handler);

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 100);

        result.IsSuccess.Should().BeTrue();
        handler.CallCount.Should().Be(0, "no HTTP call should be made when disabled");
    }

    [Fact]
    public async Task NotifyDlqSpike_NoUrl_ReturnsFailure()
    {
        var opts = DefaultEnabledOptions(url: "");
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(opts, handler);

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 100);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task NotifyDlqSpike_WhitespaceUrl_ReturnsFailure()
    {
        var opts = DefaultEnabledOptions(url: "   ");
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(opts, handler);

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 100);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task NotifyDlqSpike_BelowThreshold_ReturnsSuccessWithoutSending()
    {
        var opts = DefaultEnabledOptions();
        opts.DlqSpikeThreshold = 50;
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(opts, handler);

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 10);

        result.IsSuccess.Should().BeTrue();
        handler.CallCount.Should().Be(0, "count is below threshold");
    }

    [Fact]
    public async Task NotifyDlqSpike_AboveThreshold_SendsPostAndReturnsSuccess()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(DefaultEnabledOptions(), handler);

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);

        result.IsSuccess.Should().BeTrue();
        handler.CallCount.Should().Be(1);
        handler.LastRequestUri.Should().Be("https://hooks.example.com/dlq");
        handler.LastMethod.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task NotifyDlqSpike_HttpError_ReturnsFailure()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.InternalServerError);
        var sut = CreateSut(DefaultEnabledOptions(), handler);

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task NotifyDlqSpike_NetworkException_ReturnsFailure()
    {
        var handler = new FakeHttpHandler(new HttpRequestException("DNS failure"));
        var sut = CreateSut(DefaultEnabledOptions(), handler);

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task NotifyDlqSpike_SecondCallWithinCooldown_DoesNotSend()
    {
        var opts = DefaultEnabledOptions();
        opts.CooldownSeconds = 600;
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(opts, handler);

        var r1 = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);
        r1.IsSuccess.Should().BeTrue();
        handler.CallCount.Should().Be(1);

        var r2 = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 20);
        r2.IsSuccess.Should().BeTrue();
        handler.CallCount.Should().Be(1, "second call should be suppressed by cooldown");
    }

    [Fact]
    public async Task NotifyDlqSpike_DifferentNamespace_NotAffectedByCooldown()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(DefaultEnabledOptions(), handler);

        var r1 = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);
        r1.IsSuccess.Should().BeTrue();

        var otherId = Guid.NewGuid();
        var r2 = await sut.NotifyDlqSpikeAsync(otherId, "other-ns", 15);
        r2.IsSuccess.Should().BeTrue();

        handler.CallCount.Should().Be(2, "different namespaces have independent cooldowns");
    }

    [Fact]
    public async Task NotifyDlqSpike_Cancelled_ReturnsFailure()
    {
        var handler = new FakeHttpHandler(new TaskCanceledException());
        var sut = CreateSut(DefaultEnabledOptions(), handler);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15, cts.Token);

        result.IsFailure.Should().BeTrue();
    }

    // ── Format selection ─────────────────────────────────────

    [Fact]
    public async Task NotifyDlqSpike_GenericFormat_SendsFlatJsonMatchingOriginalShape()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(DefaultEnabledOptions(format: WebhookFormat.Generic), handler);

        await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);

        var json = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        json.GetProperty("namespaceId").GetGuid().Should().Be(TestNamespaceId);
        json.GetProperty("namespaceName").GetString().Should().Be(TestNamespaceName);
        json.GetProperty("newMessageCount").GetInt32().Should().Be(15);
        json.GetProperty("threshold").GetInt32().Should().Be(10);
        json.TryGetProperty("blocks", out _).Should().BeFalse("generic format must not include Slack-specific fields");
    }

    [Fact]
    public async Task NotifyDlqSpike_SlackFormat_SendsBlockKitPayload()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(DefaultEnabledOptions(format: WebhookFormat.Slack), handler);

        await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);

        var json = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        json.GetProperty("text").GetString().Should().Contain(TestNamespaceName);
        var blocks = json.GetProperty("blocks");
        blocks.GetArrayLength().Should().BeGreaterThan(0);
        blocks[0].GetProperty("type").GetString().Should().Be("header");
    }

    [Fact]
    public async Task NotifyDlqSpike_TeamsFormat_SendsMessageCardPayload()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(DefaultEnabledOptions(format: WebhookFormat.Teams), handler);

        await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);

        var json = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        json.GetProperty("@type").GetString().Should().Be("MessageCard");
        json.GetProperty("title").GetString().Should().Contain("DLQ Spike");
        json.GetProperty("sections")[0].GetProperty("facts").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task NotifyDlqSpike_SlackFormat_WithPublicUrl_IncludesInvestigateButton()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var opts = DefaultEnabledOptions(format: WebhookFormat.Slack, publicUrl: "https://servicehub.example.com");
        var sut = CreateSut(opts, handler);

        await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);

        var json = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        var blocks = json.GetProperty("blocks");
        var actionsBlock = blocks.EnumerateArray().FirstOrDefault(b => b.GetProperty("type").GetString() == "actions");
        actionsBlock.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        actionsBlock.GetProperty("elements")[0].GetProperty("url").GetString()
            .Should().Be($"https://servicehub.example.com/dlq-history?namespace={TestNamespaceId}");
    }

    [Fact]
    public async Task NotifyDlqSpike_SlackFormat_WithoutPublicUrl_OmitsInvestigateButton()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(DefaultEnabledOptions(format: WebhookFormat.Slack), handler);

        await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);

        var json = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        var blocks = json.GetProperty("blocks");
        blocks.EnumerateArray().Any(b => b.GetProperty("type").GetString() == "actions").Should().BeFalse();
    }

    // ── NotifyBulkOperationCompletedAsync ─────────────────────

    [Fact]
    public async Task NotifyBulkOperationCompleted_Disabled_ReturnsSuccessWithoutSending()
    {
        var opts = new WebhookOptions { Enabled = false, Url = "https://hooks.example.com" };
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(opts, handler);

        var result = await sut.NotifyBulkOperationCompletedAsync(
            TestJobId, BulkOperationType.Replay, BulkOperationStatus.Completed,
            TestNamespaceId, TestNamespaceName, 10, 10, 0, 0);

        result.IsSuccess.Should().BeTrue();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task NotifyBulkOperationCompleted_NoThresholdGate_AlwaysSendsWhenEnabled()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        // DlqSpikeThreshold intentionally left high — bulk-op notifications are not gated by it.
        var opts = DefaultEnabledOptions();
        opts.DlqSpikeThreshold = 1000;
        var sut = CreateSut(opts, handler);

        var result = await sut.NotifyBulkOperationCompletedAsync(
            TestJobId, BulkOperationType.Purge, BulkOperationStatus.Completed,
            TestNamespaceId, TestNamespaceName, 3, 3, 0, 0);

        result.IsSuccess.Should().BeTrue();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task NotifyBulkOperationCompleted_NotAffectedByDlqSpikeCooldown()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(DefaultEnabledOptions(), handler);

        // Send a DLQ spike alert first (starts its cooldown for this namespace)...
        await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);
        handler.CallCount.Should().Be(1);

        // ...a bulk-op completion for the same namespace should still send immediately.
        var result = await sut.NotifyBulkOperationCompletedAsync(
            TestJobId, BulkOperationType.Replay, BulkOperationStatus.Completed,
            TestNamespaceId, TestNamespaceName, 5, 5, 0, 0);

        result.IsSuccess.Should().BeTrue();
        handler.CallCount.Should().Be(2, "bulk-operation notifications are not subject to the DLQ-spike cooldown");
    }

    [Fact]
    public async Task NotifyBulkOperationCompleted_GenericFormat_SendsExpectedFields()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(DefaultEnabledOptions(), handler);

        await sut.NotifyBulkOperationCompletedAsync(
            TestJobId, BulkOperationType.Replay, BulkOperationStatus.CompletedWithErrors,
            TestNamespaceId, TestNamespaceName, 10, 7, 2, 1);

        var json = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        json.GetProperty("jobId").GetGuid().Should().Be(TestJobId);
        json.GetProperty("operationType").GetString().Should().Be("Replay");
        json.GetProperty("status").GetString().Should().Be("CompletedWithErrors");
        json.GetProperty("totalMatched").GetInt32().Should().Be(10);
        json.GetProperty("successCount").GetInt32().Should().Be(7);
        json.GetProperty("failureCount").GetInt32().Should().Be(2);
        json.GetProperty("skippedCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task NotifyBulkOperationCompleted_SlackFormat_SendsBlockKitWithCounts()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(DefaultEnabledOptions(format: WebhookFormat.Slack), handler);

        await sut.NotifyBulkOperationCompletedAsync(
            TestJobId, BulkOperationType.Purge, BulkOperationStatus.Completed,
            TestNamespaceId, TestNamespaceName, 3, 3, 0, 0);

        var json = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        json.GetProperty("text").GetString().Should().Contain("purge");
        json.GetProperty("blocks")[0].GetProperty("type").GetString().Should().Be("header");
    }

    [Fact]
    public async Task NotifyBulkOperationCompleted_TeamsFormat_SendsMessageCardWithCounts()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(DefaultEnabledOptions(format: WebhookFormat.Teams), handler);

        await sut.NotifyBulkOperationCompletedAsync(
            TestJobId, BulkOperationType.Replay, BulkOperationStatus.Failed,
            TestNamespaceId, TestNamespaceName, 5, 0, 0, 0);

        var json = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        json.GetProperty("@type").GetString().Should().Be("MessageCard");
        json.GetProperty("title").GetString().Should().Contain("failed");
    }

    [Fact]
    public async Task NotifyBulkOperationCompleted_InvalidUrl_ReturnsFailure()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(DefaultEnabledOptions(url: "http://not-https.example.com"), handler);

        var result = await sut.NotifyBulkOperationCompletedAsync(
            TestJobId, BulkOperationType.Replay, BulkOperationStatus.Completed,
            TestNamespaceId, TestNamespaceName, 1, 1, 0, 0);

        result.IsFailure.Should().BeTrue();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task NotifyBulkOperationCompleted_InvalidUrl_NeverLogsTheUrl()
    {
        // A webhook URL (e.g. a Slack/Teams incoming webhook) is a bearer secret in itself —
        // rejecting it for being non-HTTPS must not put it in plaintext logs at Warning level.
        const string secretUrl = "http://hooks.slack.com/services/T00/B00/XXXXSECRETXXXX";
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var logger = new Moq.Mock<Microsoft.Extensions.Logging.ILogger<WebhookNotifier>>();
        var sut = new WebhookNotifier(
            new HttpClient(handler), Wrap(DefaultEnabledOptions(url: secretUrl)), AllFormatters, logger.Object);

        var result = await sut.NotifyBulkOperationCompletedAsync(
            TestJobId, BulkOperationType.Replay, BulkOperationStatus.Completed,
            TestNamespaceId, TestNamespaceName, 1, 1, 0, 0);

        result.IsFailure.Should().BeTrue();
        logger.Invocations
            .Where(i => i.Method.Name == "Log")
            .Should().NotContain(i => i.Arguments[2]!.ToString()!.Contains(secretUrl));
    }

    // ── NotifyAutonomyTransitionAsync ─────────────────────────

    [Fact]
    public async Task NotifyAutonomyTransition_Disabled_ReturnsSuccessWithoutSending()
    {
        var opts = new WebhookOptions { Enabled = false, Url = "https://hooks.example.com" };
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(opts, handler);

        var result = await sut.NotifyAutonomyTransitionAsync(
            "sig-abc", AutonomyLevel.Approve, AutonomyLevel.Standing, "Promoted");

        result.IsSuccess.Should().BeTrue();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task NotifyAutonomyTransition_NoThresholdGate_AlwaysSendsWhenEnabled()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var opts = DefaultEnabledOptions();
        opts.DlqSpikeThreshold = 1000;
        var sut = CreateSut(opts, handler);

        var result = await sut.NotifyAutonomyTransitionAsync(
            "sig-abc", AutonomyLevel.Approve, AutonomyLevel.Standing, "Promoted");

        result.IsSuccess.Should().BeTrue();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task NotifyAutonomyTransition_InvalidUrl_ReturnsFailure()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(DefaultEnabledOptions(url: "http://not-https.example.com"), handler);

        var result = await sut.NotifyAutonomyTransitionAsync(
            "sig-abc", AutonomyLevel.Approve, AutonomyLevel.Standing, "Promoted");

        result.IsFailure.Should().BeTrue();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task NotifyAutonomyTransition_GenericFormat_SendsExpectedFields()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(DefaultEnabledOptions(), handler);

        await sut.NotifyAutonomyTransitionAsync(
            "sig-abc", AutonomyLevel.Standing, AutonomyLevel.Approve, "Demoted: rate below floor");

        var json = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        json.GetProperty("signatureHash").GetString().Should().Be("sig-abc");
        json.GetProperty("previousLevel").GetString().Should().Be("Standing");
        json.GetProperty("newLevel").GetString().Should().Be("Approve");
        json.GetProperty("reason").GetString().Should().Be("Demoted: rate below floor");
    }

    [Fact]
    public async Task NotifyAutonomyTransition_SlackFormat_SendsBlockKitPayload()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(DefaultEnabledOptions(format: WebhookFormat.Slack), handler);

        await sut.NotifyAutonomyTransitionAsync(
            "sig-abc", AutonomyLevel.Approve, AutonomyLevel.Standing, "Promoted");

        var json = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        json.GetProperty("text").GetString().Should().Contain("sig-abc");
        json.GetProperty("blocks")[0].GetProperty("type").GetString().Should().Be("header");
    }

    [Fact]
    public async Task NotifyAutonomyTransition_TeamsFormat_SendsMessageCardPayload()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(DefaultEnabledOptions(format: WebhookFormat.Teams), handler);

        await sut.NotifyAutonomyTransitionAsync(
            "sig-abc", AutonomyLevel.Approve, AutonomyLevel.Standing, "Promoted");

        var json = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        json.GetProperty("@type").GetString().Should().Be("MessageCard");
        json.GetProperty("sections")[0].GetProperty("facts").GetArrayLength().Should().Be(4);
    }

    [Fact]
    public async Task NotifyAutonomyTransition_WithPublicUrl_BuildsSignatureInvestigateLink()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var opts = DefaultEnabledOptions(format: WebhookFormat.Slack, publicUrl: "https://servicehub.example.com");
        var sut = CreateSut(opts, handler);

        await sut.NotifyAutonomyTransitionAsync(
            "sig-abc", AutonomyLevel.Approve, AutonomyLevel.Standing, "Promoted");

        var json = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        var actionsBlock = json.GetProperty("blocks").EnumerateArray()
            .FirstOrDefault(b => b.GetProperty("type").GetString() == "actions");
        actionsBlock.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        actionsBlock.GetProperty("elements")[0].GetProperty("url").GetString()
            .Should().Be("https://servicehub.example.com/signatures/sig-abc");
    }

    // ── NotifyCircuitBreakerTrippedAsync ───────────────────────

    [Fact]
    public async Task NotifyCircuitBreakerTripped_Disabled_ReturnsSuccessWithoutSending()
    {
        var opts = new WebhookOptions { Enabled = false, Url = "https://hooks.example.com" };
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(opts, handler);

        var result = await sut.NotifyCircuitBreakerTrippedAsync(42, "orders-dlq-rule", 20, 0.35);

        result.IsSuccess.Should().BeTrue();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task NotifyCircuitBreakerTripped_InvalidUrl_ReturnsFailure()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(DefaultEnabledOptions(url: "http://not-https.example.com"), handler);

        var result = await sut.NotifyCircuitBreakerTrippedAsync(42, "orders-dlq-rule", 20, 0.35);

        result.IsFailure.Should().BeTrue();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task NotifyCircuitBreakerTripped_GenericFormat_SendsExpectedFields()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(DefaultEnabledOptions(), handler);

        await sut.NotifyCircuitBreakerTrippedAsync(42, "orders-dlq-rule", 20, 0.35);

        var json = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        json.GetProperty("ruleId").GetInt64().Should().Be(42);
        json.GetProperty("ruleName").GetString().Should().Be("orders-dlq-rule");
        json.GetProperty("sampleSize").GetInt32().Should().Be(20);
        json.GetProperty("verifiedSuccessRate").GetDouble().Should().Be(0.35);
    }

    [Fact]
    public async Task NotifyCircuitBreakerTripped_SlackFormat_SendsBlockKitPayload()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(DefaultEnabledOptions(format: WebhookFormat.Slack), handler);

        await sut.NotifyCircuitBreakerTrippedAsync(42, "orders-dlq-rule", 20, 0.35);

        var json = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        json.GetProperty("text").GetString().Should().Contain("orders-dlq-rule");
        json.GetProperty("blocks")[0].GetProperty("type").GetString().Should().Be("header");
    }

    [Fact]
    public async Task NotifyCircuitBreakerTripped_TeamsFormat_SendsMessageCardPayload()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(DefaultEnabledOptions(format: WebhookFormat.Teams), handler);

        await sut.NotifyCircuitBreakerTrippedAsync(42, "orders-dlq-rule", 20, 0.35);

        var json = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        json.GetProperty("@type").GetString().Should().Be("MessageCard");
        json.GetProperty("title").GetString().Should().Contain("Circuit breaker");
    }

    [Fact]
    public async Task NotifyCircuitBreakerTripped_WithPublicUrl_BuildsRulesInvestigateLink()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var opts = DefaultEnabledOptions(format: WebhookFormat.Slack, publicUrl: "https://servicehub.example.com");
        var sut = CreateSut(opts, handler);

        await sut.NotifyCircuitBreakerTrippedAsync(42, "orders-dlq-rule", 20, 0.35);

        var json = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        var actionsBlock = json.GetProperty("blocks").EnumerateArray()
            .FirstOrDefault(b => b.GetProperty("type").GetString() == "actions");
        actionsBlock.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        actionsBlock.GetProperty("elements")[0].GetProperty("url").GetString()
            .Should().Be("https://servicehub.example.com/rules");
    }

    // ── NotifyInsightDetectedAsync ───────────────────────

    [Fact]
    public async Task NotifyInsightDetected_Disabled_ReturnsSuccessWithoutSending()
    {
        var opts = new WebhookOptions { Enabled = false, Url = "https://hooks.example.com" };
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(opts, handler);

        var result = await sut.NotifyInsightDetectedAsync(
            InsightKind.Anomaly, Guid.NewGuid(), TestNamespaceId, TestNamespaceName, "orders-queue", "spike", 85);

        result.IsSuccess.Should().BeTrue();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task NotifyInsightDetected_InvalidUrl_ReturnsFailure()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(DefaultEnabledOptions(url: "http://not-https.example.com"), handler);

        var result = await sut.NotifyInsightDetectedAsync(
            InsightKind.Anomaly, Guid.NewGuid(), TestNamespaceId, TestNamespaceName, "orders-queue", "spike", 85);

        result.IsFailure.Should().BeTrue();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task NotifyInsightDetected_GenericFormat_SendsExpectedFields()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(DefaultEnabledOptions(), handler);
        var findingId = Guid.NewGuid();

        await sut.NotifyInsightDetectedAsync(
            InsightKind.Drift, findingId, TestNamespaceId, TestNamespaceName, "orders-queue", "shape changed", 90);

        var json = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        json.GetProperty("kind").GetString().Should().Be("Drift");
        json.GetProperty("findingId").GetGuid().Should().Be(findingId);
        json.GetProperty("namespaceId").GetGuid().Should().Be(TestNamespaceId);
        json.GetProperty("entityName").GetString().Should().Be("orders-queue");
        json.GetProperty("severity").GetInt32().Should().Be(90);
    }

    [Fact]
    public async Task NotifyInsightDetected_CorrelationWithNoNamespace_OmitsNamespaceFromDeepLink()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var opts = DefaultEnabledOptions(publicUrl: "https://servicehub.example.com");
        var sut = CreateSut(opts, handler);

        await sut.NotifyInsightDetectedAsync(
            InsightKind.Correlation, Guid.NewGuid(), null, null, null, "correlated spike", 80);

        var json = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        json.GetProperty("namespaceId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task NotifyInsightDetected_SlackFormat_SendsBlockKitPayload()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(DefaultEnabledOptions(format: WebhookFormat.Slack), handler);

        await sut.NotifyInsightDetectedAsync(
            InsightKind.Anomaly, Guid.NewGuid(), TestNamespaceId, TestNamespaceName, "orders-queue", "spike", 85);

        var json = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        json.GetProperty("blocks")[0].GetProperty("type").GetString().Should().Be("header");
    }

    [Fact]
    public async Task NotifyInsightDetected_TeamsFormat_SendsMessageCardPayload()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var sut = CreateSut(DefaultEnabledOptions(format: WebhookFormat.Teams), handler);

        await sut.NotifyInsightDetectedAsync(
            InsightKind.Anomaly, Guid.NewGuid(), TestNamespaceId, TestNamespaceName, "orders-queue", "spike", 85);

        var json = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        json.GetProperty("@type").GetString().Should().Be("MessageCard");
    }

    [Fact]
    public async Task NotifyInsightDetected_WithPublicUrlAndNamespace_BuildsNamespaceScopedInvestigateLink()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var opts = DefaultEnabledOptions(format: WebhookFormat.Slack, publicUrl: "https://servicehub.example.com");
        var sut = CreateSut(opts, handler);

        await sut.NotifyInsightDetectedAsync(
            InsightKind.Anomaly, Guid.NewGuid(), TestNamespaceId, TestNamespaceName, "orders-queue", "spike", 85);

        var json = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        var actionsBlock = json.GetProperty("blocks").EnumerateArray()
            .FirstOrDefault(b => b.GetProperty("type").GetString() == "actions");
        actionsBlock.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        actionsBlock.GetProperty("elements")[0].GetProperty("url").GetString()
            .Should().Be($"https://servicehub.example.com/dlq-history?namespace={TestNamespaceId}");
    }

    // ── Helpers ──────────────────────────────────────────────

    /// <summary>
    /// A fake DelegatingHandler for testing HttpClient without real network calls. Captures the
    /// request body so tests can assert the exact payload shape a formatter produced.
    /// </summary>
    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode? _statusCode;
        private readonly Exception? _exception;

        public int CallCount { get; private set; }
        public string? LastRequestUri { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public string? LastRequestBody { get; private set; }

        public FakeHttpHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        public FakeHttpHandler(Exception exception)
        {
            _exception = exception;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestUri = request.RequestUri?.ToString();
            LastMethod = request.Method;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            if (_exception is not null)
                throw _exception;

            return new HttpResponseMessage(_statusCode!.Value);
        }
    }
}
