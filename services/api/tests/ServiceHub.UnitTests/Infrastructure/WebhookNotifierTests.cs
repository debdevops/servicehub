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
