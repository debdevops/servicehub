using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure;

namespace ServiceHub.UnitTests.Infrastructure;

public sealed class WebhookNotifierTests
{
    private static readonly Guid TestNamespaceId = Guid.NewGuid();
    private const string TestNamespaceName = "test-ns.servicebus.windows.net";

    private static WebhookOptions DefaultEnabledOptions(string url = "https://hooks.example.com/dlq") => new()
    {
        Enabled = true,
        Url = url,
        DlqSpikeThreshold = 10,
        CooldownSeconds = 300
    };

    private static IOptions<WebhookOptions> Wrap(WebhookOptions opts) =>
        Options.Create(opts);

    // ── Constructor ──────────────────────────────────────────

    [Fact]
    public void Constructor_NullHttpClient_Throws()
    {
        var act = () => new WebhookNotifier(null!, Wrap(DefaultEnabledOptions()), NullLogger<WebhookNotifier>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        var act = () => new WebhookNotifier(new HttpClient(), null!, NullLogger<WebhookNotifier>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new WebhookNotifier(new HttpClient(), Wrap(DefaultEnabledOptions()), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── Disabled ─────────────────────────────────────────────

    [Fact]
    public async Task NotifyDlqSpike_Disabled_ReturnsSuccessWithoutSending()
    {
        var opts = new WebhookOptions { Enabled = false, Url = "https://hooks.example.com" };
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var sut = new WebhookNotifier(client, Wrap(opts), NullLogger<WebhookNotifier>.Instance);

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 100);

        result.IsSuccess.Should().BeTrue();
        handler.CallCount.Should().Be(0, "no HTTP call should be made when disabled");
    }

    // ── No URL configured ────────────────────────────────────

    [Fact]
    public async Task NotifyDlqSpike_NoUrl_ReturnsFailure()
    {
        var opts = DefaultEnabledOptions(url: "");
        using var client = new HttpClient();
        var sut = new WebhookNotifier(client, Wrap(opts), NullLogger<WebhookNotifier>.Instance);

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 100);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task NotifyDlqSpike_WhitespaceUrl_ReturnsFailure()
    {
        var opts = DefaultEnabledOptions(url: "   ");
        using var client = new HttpClient();
        var sut = new WebhookNotifier(client, Wrap(opts), NullLogger<WebhookNotifier>.Instance);

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 100);

        result.IsFailure.Should().BeTrue();
    }

    // ── Below threshold ──────────────────────────────────────

    [Fact]
    public async Task NotifyDlqSpike_BelowThreshold_ReturnsSuccessWithoutSending()
    {
        var opts = DefaultEnabledOptions();
        opts.DlqSpikeThreshold = 50;

        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var sut = new WebhookNotifier(client, Wrap(opts), NullLogger<WebhookNotifier>.Instance);

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 10);

        result.IsSuccess.Should().BeTrue();
        handler.CallCount.Should().Be(0, "count is below threshold");
    }

    // ── Successful POST ──────────────────────────────────────

    [Fact]
    public async Task NotifyDlqSpike_AboveThreshold_SendsPostAndReturnsSuccess()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var sut = new WebhookNotifier(client, Wrap(DefaultEnabledOptions()), NullLogger<WebhookNotifier>.Instance);

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);

        result.IsSuccess.Should().BeTrue();
        handler.CallCount.Should().Be(1);
        handler.LastRequestUri.Should().Be("https://hooks.example.com/dlq");
        handler.LastMethod.Should().Be(HttpMethod.Post);
    }

    // ── HTTP failure ─────────────────────────────────────────

    [Fact]
    public async Task NotifyDlqSpike_HttpError_ReturnsFailure()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.InternalServerError);
        using var client = new HttpClient(handler);
        var sut = new WebhookNotifier(client, Wrap(DefaultEnabledOptions()), NullLogger<WebhookNotifier>.Instance);

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);

        result.IsFailure.Should().BeTrue();
    }

    // ── Network exception ────────────────────────────────────

    [Fact]
    public async Task NotifyDlqSpike_NetworkException_ReturnsFailure()
    {
        var handler = new FakeHttpHandler(new HttpRequestException("DNS failure"));
        using var client = new HttpClient(handler);
        var sut = new WebhookNotifier(client, Wrap(DefaultEnabledOptions()), NullLogger<WebhookNotifier>.Instance);

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);

        result.IsFailure.Should().BeTrue();
    }

    // ── Cooldown ─────────────────────────────────────────────

    [Fact]
    public async Task NotifyDlqSpike_SecondCallWithinCooldown_DoesNotSend()
    {
        var opts = DefaultEnabledOptions();
        opts.CooldownSeconds = 600; // 10 minutes

        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var sut = new WebhookNotifier(client, Wrap(opts), NullLogger<WebhookNotifier>.Instance);

        // First call should succeed and send
        var r1 = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);
        r1.IsSuccess.Should().BeTrue();
        handler.CallCount.Should().Be(1);

        // Second (immediate) call should be suppressed by cooldown
        var r2 = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 20);
        r2.IsSuccess.Should().BeTrue();
        handler.CallCount.Should().Be(1, "second call should be suppressed by cooldown");
    }

    [Fact]
    public async Task NotifyDlqSpike_DifferentNamespace_NotAffectedByCooldown()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var sut = new WebhookNotifier(client, Wrap(DefaultEnabledOptions()), NullLogger<WebhookNotifier>.Instance);

        var r1 = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15);
        r1.IsSuccess.Should().BeTrue();

        var otherId = Guid.NewGuid();
        var r2 = await sut.NotifyDlqSpikeAsync(otherId, "other-ns", 15);
        r2.IsSuccess.Should().BeTrue();

        handler.CallCount.Should().Be(2, "different namespaces have independent cooldowns");
    }

    // ── Cancellation ────────────────────────────────────────

    [Fact]
    public async Task NotifyDlqSpike_Cancelled_ReturnsFailure()
    {
        var handler = new FakeHttpHandler(new TaskCanceledException());
        using var client = new HttpClient(handler);
        var sut = new WebhookNotifier(client, Wrap(DefaultEnabledOptions()), NullLogger<WebhookNotifier>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await sut.NotifyDlqSpikeAsync(TestNamespaceId, TestNamespaceName, 15, cts.Token);

        result.IsFailure.Should().BeTrue();
    }

    // ── Helpers ──────────────────────────────────────────────

    /// <summary>
    /// A fake DelegatingHandler for testing HttpClient without real network calls.
    /// </summary>
    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode? _statusCode;
        private readonly Exception? _exception;

        public int CallCount { get; private set; }
        public string? LastRequestUri { get; private set; }
        public HttpMethod? LastMethod { get; private set; }

        public FakeHttpHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        public FakeHttpHandler(Exception exception)
        {
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestUri = request.RequestUri?.ToString();
            LastMethod = request.Method;

            if (_exception is not null)
                throw _exception;

            return Task.FromResult(new HttpResponseMessage(_statusCode!.Value));
        }
    }
}
