using System.Net;
using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Infrastructure.Webhooks;
using ServiceHub.Core.Results;

namespace ServiceHub.UnitTests.Webhooks;

// Ported from 4.0.0 (unit 5.5). Unedited except: the namespace, and the bulk-operation-completed tests are not ported —
// 4.1.0's BulkOperationStatus differs (no CompletedWithErrors), so that one notifier method was left out. Every SSRF /
// redirect / failover test is 4.0.0's, byte for byte.

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

    private static WebhookNotifier CreateSut(WebhookOptions opts, FakeHttpHandler handler, IDnsResolver? dnsResolver = null) =>
        new(new HttpClient(handler), Wrap(opts), AllFormatters, NullLogger<WebhookNotifier>.Instance,
            dnsResolver ?? new FakeDnsResolver(IPAddress.Parse("203.0.113.10")));

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


    // ── NotifyBulkOperationCompletedAsync ─────────────────────









    // ── NotifyAutonomyTransitionAsync ─────────────────────────

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

    // ── SSRF guard: hostname DNS resolution ───────────────────

    [Fact]
    public async Task NotifyDlqSpike_HostnameResolvesToLoopback_ReturnsFailureWithoutSending()
    {
        // Regression: TryGetSafeWebhookUriAsync used to validate only IP-literal hosts, so a
        // hostname resolving to 127.0.0.1/169.254.169.254/etc. sailed straight through.
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var resolver = new FakeDnsResolver(IPAddress.Loopback);
        var sut = CreateSut(DefaultEnabledOptions(), handler, resolver);

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);

        result.IsFailure.Should().BeTrue();
        handler.CallCount.Should().Be(0, "a hostname resolving to a loopback address must never be contacted");
    }

    [Fact]
    public async Task NotifyDlqSpike_HostnameResolvesToLinkLocalMetadataAddress_ReturnsFailureWithoutSending()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var resolver = new FakeDnsResolver(IPAddress.Parse("169.254.169.254"));
        var sut = CreateSut(DefaultEnabledOptions(), handler, resolver);

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);

        result.IsFailure.Should().BeTrue();
        handler.CallCount.Should().Be(0, "a hostname resolving to a cloud metadata address must never be contacted");
    }

    [Fact]
    public async Task NotifyDlqSpike_HostnameResolvesToOnePrivateAddressAmongMultiple_ReturnsFailureWithoutSending()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var resolver = new FakeDnsResolver(IPAddress.Parse("203.0.113.10"), IPAddress.Parse("10.0.0.5"));
        var sut = CreateSut(DefaultEnabledOptions(), handler, resolver);

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);

        result.IsFailure.Should().BeTrue("every resolved address must be safe, not merely one of them");
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task NotifyDlqSpike_HostnameResolutionFails_ReturnsFailureWithoutSending()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var resolver = new FakeDnsResolver();
        var sut = CreateSut(DefaultEnabledOptions(), handler, resolver);

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);

        result.IsFailure.Should().BeTrue("a hostname that cannot be resolved must fail closed, not be treated as safe");
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task NotifyDlqSpike_HostnameResolvesToPublicAddress_Sends()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var resolver = new FakeDnsResolver(IPAddress.Parse("203.0.113.10"));
        var sut = CreateSut(DefaultEnabledOptions(), handler, resolver);

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);

        result.IsSuccess.Should().BeTrue();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task NotifyDlqSpike_IpLiteralHost_DoesNotConsultDnsResolver()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var resolver = new FakeDnsResolver();
        var opts = DefaultEnabledOptions(url: "https://203.0.113.10/dlq");
        var sut = CreateSut(opts, handler, resolver);

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);

        result.IsSuccess.Should().BeTrue("an IP-literal host is validated directly, without DNS resolution");
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task NotifyDlqSpike_HostnameResolvesToPublicAddress_PinsConnectionToTheValidatedAddress()
    {
        // Closes the DNS-rebinding gap: the guard must pin the TCP connection to the exact
        // address it validated, not let HttpClient re-resolve the hostname a second time.
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var publicAddress = IPAddress.Parse("203.0.113.10");
        var resolver = new FakeDnsResolver(publicAddress);
        var sut = CreateSut(DefaultEnabledOptions(), handler, resolver);

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);

        result.IsSuccess.Should().BeTrue();
        handler.LastRequestOptions.Should().NotBeNull();
        handler.LastRequestOptions!.TryGetValue(WebhookConnectCallback.PinnedAddressesKey, out var pinned)
            .Should().BeTrue("the connection must be pinned to the address(es) the SSRF guard validated");
        pinned.Should().ContainSingle().Which.Should().Be(publicAddress);
    }

    [Fact]
    public async Task NotifyDlqSpike_HostnameResolvesToMultiplePublicAddresses_PinsEveryValidatedAddressForFailover()
    {
        // Regression: pinning only the first resolved address left no failover if that one
        // specific address was unreachable, even though DNS returned other validated addresses.
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var first = IPAddress.Parse("203.0.113.10");
        var second = IPAddress.Parse("203.0.113.11");
        var resolver = new FakeDnsResolver(first, second);
        var sut = CreateSut(DefaultEnabledOptions(), handler, resolver);

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);

        result.IsSuccess.Should().BeTrue();
        handler.LastRequestOptions!.TryGetValue(WebhookConnectCallback.PinnedAddressesKey, out var pinned)
            .Should().BeTrue();
        pinned.Should().Equal(first, second);
    }

    [Fact]
    public async Task NotifyDlqSpike_IpLiteralHost_DoesNotPinAConnectionAddress()
    {
        // An IP-literal host has no second, divergent DNS lookup to pin against — the literal
        // itself already is the connect target, so no pinned-address option should be set.
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var opts = DefaultEnabledOptions(url: "https://203.0.113.10/dlq");
        var sut = CreateSut(opts, handler, new FakeDnsResolver());

        await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);

        handler.LastRequestOptions!.TryGetValue(WebhookConnectCallback.PinnedAddressesKey, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("::ffff:127.0.0.1")]   // IPv4-mapped loopback
    [InlineData("::ffff:10.0.0.5")]    // IPv4-mapped RFC-1918
    [InlineData("::ffff:169.254.169.254")]  // IPv4-mapped cloud metadata
    public async Task NotifyDlqSpike_HostnameResolvesToIPv4MappedIPv6LocalAddress_ReturnsFailureWithoutSending(string mapped)
    {
        // Regression: an unnormalized IPv4-mapped IPv6 address matched neither IPAddress.IsLoopback
        // nor either IPv6 prefix in IsRfc1918OrLinkLocal, so it passed the guard as "public."
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var resolver = new FakeDnsResolver(IPAddress.Parse(mapped));
        var sut = CreateSut(DefaultEnabledOptions(), handler, resolver);

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);

        result.IsFailure.Should().BeTrue();
        handler.CallCount.Should().Be(0, "an IPv4-mapped IPv6 local address must be recognized after normalization");
    }

    [Fact]
    public async Task NotifyDlqSpike_IpLiteralIPv4MappedIPv6Loopback_ReturnsFailureWithoutSending()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var opts = DefaultEnabledOptions(url: "https://[::ffff:127.0.0.1]/dlq");
        var sut = CreateSut(opts, handler, new FakeDnsResolver());

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);

        result.IsFailure.Should().BeTrue();
        handler.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    public async Task NotifyDlqSpike_HostnameResolvesToUnspecifiedAddress_ReturnsFailureWithoutSending(string unspecified)
    {
        // Regression: 0.0.0.0/:: are neither loopback nor RFC-1918/link-local, but connecting to
        // either is treated by most platforms as connecting to the local host — the same SSRF
        // destination as 127.0.0.1, just under a different classification.
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var resolver = new FakeDnsResolver(IPAddress.Parse(unspecified));
        var sut = CreateSut(DefaultEnabledOptions(), handler, resolver);

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);

        result.IsFailure.Should().BeTrue();
        handler.CallCount.Should().Be(0, "an unspecified address must be rejected by the shared classification guard");
    }

    [Theory]
    [InlineData("https://0.0.0.0/dlq")]
    [InlineData("https://[::]/dlq")]
    public async Task NotifyDlqSpike_IpLiteralUnspecifiedAddress_ReturnsFailureWithoutSending(string url)
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var opts = DefaultEnabledOptions(url: url);
        var sut = CreateSut(opts, handler, new FakeDnsResolver());

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);

        result.IsFailure.Should().BeTrue();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task NotifyDlqSpike_DnsResolutionCancelled_ReturnsFailureInsteadOfThrowing()
    {
        // Regression: Dns.GetHostAddressesAsync throwing OperationCanceledException/
        // TaskCanceledException during resolution used to escape uncaught instead of coming back
        // as the same Result.Failure contract PostAsync's own cancellation handling already gives.
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var resolver = new CancellingDnsResolver();
        var sut = CreateSut(DefaultEnabledOptions(), handler, resolver);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task<Result>> act = () => sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15, cts.Token);

        var result = await act.Should().NotThrowAsync();
        result.Subject.IsFailure.Should().BeTrue();
        handler.CallCount.Should().Be(0);
    }

    // ── WebhookConnectCallback ────────────────────────────────

    [Fact]
    public void SelectConnectTargets_PinnedAddressPresent_ReturnsPinnedEndpoint()
    {
        var pinned = IPAddress.Parse("203.0.113.10");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://hooks.example.com/dlq");
        request.Options.Set(WebhookConnectCallback.PinnedAddressesKey, new[] { pinned });
        var fallback = new DnsEndPoint("hooks.example.com", 443);

        var targets = WebhookConnectCallback.SelectConnectTargets(request.Options, fallback);

        targets.Should().ContainSingle().Which.Should().BeOfType<IPEndPoint>();
        var endpoint = (IPEndPoint)targets[0];
        endpoint.Address.Should().Be(pinned);
        endpoint.Port.Should().Be(443);
    }

    [Fact]
    public void SelectConnectTargets_MultiplePinnedAddresses_ReturnsEachAsAFailoverCandidateInOrder()
    {
        // Regression: pinning collapsed a multi-address DNS result down to a single connect
        // target, so a validated-but-unreachable first address had no fallback to the others.
        var first = IPAddress.Parse("203.0.113.10");
        var second = IPAddress.Parse("203.0.113.11");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://hooks.example.com/dlq");
        request.Options.Set(WebhookConnectCallback.PinnedAddressesKey, new[] { first, second });
        var fallback = new DnsEndPoint("hooks.example.com", 443);

        var targets = WebhookConnectCallback.SelectConnectTargets(request.Options, fallback);

        targets.Should().HaveCount(2);
        ((IPEndPoint)targets[0]).Address.Should().Be(first);
        ((IPEndPoint)targets[1]).Address.Should().Be(second);
    }

    [Fact]
    public void SelectConnectTargets_NoPinnedAddresses_FallsBackToDnsEndPoint()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://hooks.example.com/dlq");
        var fallback = new DnsEndPoint("hooks.example.com", 443);

        var targets = WebhookConnectCallback.SelectConnectTargets(request.Options, fallback);

        targets.Should().ContainSingle().Which.Should().Be(fallback);
    }

    [Fact]
    public async Task ConnectToFirstAvailableAsync_FirstTargetRefusesConnection_FailsOverToSecondRealListener()
    {
        // Real-socket proof of the failover fix: the first candidate is a real TCP listener that
        // is immediately closed (so the OS refuses the connection, matching an unreachable
        // advertised route), the second is a real listener left open. Exercises the exact
        // connect-with-failover loop WebhookConnectCallback.ConnectAsync uses in production,
        // without needing to go through the SSRF guard (a separate, already unit-tested concern
        // that would otherwise reject any loopback address this test could use).
        using var deadListener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        deadListener.Start();
        var deadPort = ((IPEndPoint)deadListener.LocalEndpoint).Port;
        deadListener.Stop(); // now nothing is listening on deadPort — connection attempts are refused

        using var liveListener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        liveListener.Start();
        var livePort = ((IPEndPoint)liveListener.LocalEndpoint).Port;
        var acceptTask = liveListener.AcceptSocketAsync();

        var targets = new EndPoint[]
        {
            new IPEndPoint(IPAddress.Loopback, deadPort),
            new IPEndPoint(IPAddress.Loopback, livePort),
        };

        await using var stream = await WebhookConnectCallback.ConnectToFirstAvailableAsync(targets, CancellationToken.None);

        using var acceptedSocket = await acceptTask;
        acceptedSocket.Should().NotBeNull("the connection must have failed over to the live listener on the second address");
    }

    [Fact]
    public async Task ConnectToFirstAvailableAsync_AllTargetsRefuseConnection_Throws()
    {
        using var deadListener1 = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        deadListener1.Start();
        var deadPort1 = ((IPEndPoint)deadListener1.LocalEndpoint).Port;
        deadListener1.Stop();

        using var deadListener2 = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        deadListener2.Start();
        var deadPort2 = ((IPEndPoint)deadListener2.LocalEndpoint).Port;
        deadListener2.Stop();

        var targets = new EndPoint[]
        {
            new IPEndPoint(IPAddress.Loopback, deadPort1),
            new IPEndPoint(IPAddress.Loopback, deadPort2),
        };

        Func<Task> act = async () => await WebhookConnectCallback.ConnectToFirstAvailableAsync(targets, CancellationToken.None);

        await act.Should().ThrowAsync<System.Net.Sockets.SocketException>();
    }

    [Fact]
    public async Task ConnectToFirstAvailableAsync_IPv6Target_ConnectsSuccessfully()
    {
        // Regression: the socket was created with the two-argument Socket constructor, which
        // defaults to AddressFamily.InterNetwork. Connecting to an IPv6 IPEndPoint with an
        // IPv4-only socket fails with an address-family mismatch, so a pinned IPv6 address (or an
        // IPv6-only DNS result) could never actually be reached even though the guard validated it.
        using var liveListener = new System.Net.Sockets.TcpListener(IPAddress.IPv6Loopback, 0);
        liveListener.Start();
        var livePort = ((IPEndPoint)liveListener.LocalEndpoint).Port;
        var acceptTask = liveListener.AcceptSocketAsync();

        var targets = new EndPoint[] { new IPEndPoint(IPAddress.IPv6Loopback, livePort) };

        await using var stream = await WebhookConnectCallback.ConnectToFirstAvailableAsync(targets, CancellationToken.None);

        using var acceptedSocket = await acceptTask;
        acceptedSocket.Should().NotBeNull("the socket must be created with the IPv6 address family to connect to an IPv6 target");
    }

    // ── DependencyInjection wiring ────────────────────────────

    [Fact]
    public async Task RealSocketsHttpHandler_RedirectResponse_IsNotFollowedToTheRedirectTarget()
    {
        // Real-wire proof of the redirect fix, one layer below the DI-config assertion below: a
        // real local HTTP server returns a 302 pointing at a second real local server (standing in
        // for an internal address the SSRF guard never saw), sent through the exact
        // SocketsHttpHandler configuration AddWebhooks wires up (AllowAutoRedirect: false). Asserts
        // both that the 302 comes back to the caller unfollowed AND that the redirect target never
        // receives a request at all.
        using var redirectTarget = new System.Net.HttpListener();
        var targetPrefix = $"http://127.0.0.1:{GetFreeTcpPort()}/";
        redirectTarget.Prefixes.Add(targetPrefix);
        redirectTarget.Start();
        var targetHitCount = 0;
        var targetListenTask = Task.Run(async () =>
        {
            try
            {
                var ctx = await redirectTarget.GetContextAsync();
                Interlocked.Increment(ref targetHitCount);
                ctx.Response.StatusCode = 200;
                ctx.Response.Close();
            }
            catch (Exception) when (!redirectTarget.IsListening)
            {
                // listener was stopped before a request arrived — expected on the "not followed" path
            }
        });

        using var redirector = new System.Net.HttpListener();
        var redirectorPrefix = $"http://127.0.0.1:{GetFreeTcpPort()}/";
        redirector.Prefixes.Add(redirectorPrefix);
        redirector.Start();
        var redirectorTask = Task.Run(async () =>
        {
            var ctx = await redirector.GetContextAsync();
            ctx.Response.StatusCode = 302;
            ctx.Response.RedirectLocation = targetPrefix;
            ctx.Response.Close();
        });

        using var handler = new System.Net.Http.SocketsHttpHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler);

        using var response = await client.PostAsync(redirectorPrefix, new StringContent("{}"));

        ((int)response.StatusCode).Should().Be(302, "the redirect must come back to the caller, not be auto-followed");
        await redirectorTask;

        redirectTarget.Stop();
        await Task.WhenAny(targetListenTask, Task.Delay(TimeSpan.FromSeconds(1)));
        targetHitCount.Should().Be(0, "the redirect target must never receive a request when AllowAutoRedirect is false");

        redirector.Stop();
    }

    private static int GetFreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public void AddWebhooks_PrimaryHandler_DisablesAutomaticRedirects()
    {
        // Regression: the SSRF guard only ever validates WebhookOptions.Url, never a 3xx
        // response's Location header. SocketsHttpHandler.AllowAutoRedirect defaults to true, so
        // leaving it unset would let a webhook endpoint redirect straight past the guard.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWebhooks();

        using var provider = services.BuildServiceProvider();
        var handlerFactory = provider.GetRequiredService<IHttpMessageHandlerFactory>();
        using var handler = handlerFactory.CreateHandler(nameof(IWebhookNotifier));

        var socketsHandler = FindSocketsHttpHandler(handler);
        socketsHandler.Should().NotBeNull("the webhook HttpClient's primary handler must be reachable to assert its redirect setting");
        socketsHandler!.AllowAutoRedirect.Should().BeFalse();
    }

    private static System.Net.Http.SocketsHttpHandler? FindSocketsHttpHandler(HttpMessageHandler handler) => handler switch
    {
        System.Net.Http.SocketsHttpHandler socketsHandler => socketsHandler,
        DelegatingHandler { InnerHandler: { } inner } => FindSocketsHttpHandler(inner),
        _ => null,
    };

    // ── Helpers ──────────────────────────────────────────────

    /// <summary>
    /// A fake <see cref="IDnsResolver"/> — returns the configured addresses for any host, or
    /// throws <see cref="SocketException"/> (matching <see cref="Dns.GetHostAddressesAsync(string, CancellationToken)"/>'s
    /// real failure mode) when constructed with none.
    /// </summary>
    private sealed class FakeDnsResolver : IDnsResolver
    {
        private readonly IPAddress[] _addresses;

        public FakeDnsResolver(params IPAddress[] addresses) => _addresses = addresses;

        public Task<IPAddress[]> ResolveHostAddressesAsync(string host, CancellationToken cancellationToken) =>
            _addresses.Length == 0
                ? throw new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostNotFound)
                : Task.FromResult(_addresses);
    }

    /// <summary>
    /// A fake <see cref="IDnsResolver"/> that throws <see cref="OperationCanceledException"/> when
    /// the supplied token is already cancelled — matching how a real
    /// <see cref="Dns.GetHostAddressesAsync(string, CancellationToken)"/> call behaves when
    /// cancelled mid-resolution, as opposed to <see cref="FakeDnsResolver"/>, which ignores the
    /// token entirely.
    /// </summary>
    private sealed class CancellingDnsResolver : IDnsResolver
    {
        public Task<IPAddress[]> ResolveHostAddressesAsync(string host, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Array.Empty<IPAddress>());
        }
    }

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
        public HttpRequestOptions? LastRequestOptions { get; private set; }

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
            LastRequestOptions = request.Options;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            if (_exception is not null)
                throw _exception;

            return new HttpResponseMessage(_statusCode!.Value);
        }
    }
}
