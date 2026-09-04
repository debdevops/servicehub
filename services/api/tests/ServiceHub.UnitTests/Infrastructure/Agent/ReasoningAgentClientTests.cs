using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Agent;

namespace ServiceHub.UnitTests.Infrastructure.Agent;

public sealed class ReasoningAgentClientTests
{
    private static ReasoningAgentClient CreateSut(
        ReasoningAgentOptions? options = null,
        HttpMessageHandler? handler = null,
        ILogger<ReasoningAgentClient>? logger = null)
    {
        options ??= new ReasoningAgentOptions { Enabled = true, ServiceUrl = "http://agent.internal:8010" };
        handler ??= new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(options.ServiceUrl) };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(ReasoningAgentClient.HttpClientName)).Returns(httpClient);

        return new ReasoningAgentClient(
            factory.Object,
            Options.Create(options),
            logger ?? NullLogger<ReasoningAgentClient>.Instance);
    }

    private static ReasoningEvidenceRecord CreateEvidence(string @ref = "ns-1:sig-1") => new(
        Ref: @ref,
        OwnerId: "entra:test-owner",
        NamespaceId: Guid.NewGuid(),
        SignatureHash: "sig-1",
        LifecycleStatus: "Active",
        Severity: "Warning",
        Provider: "AzureServiceBus",
        DominantDeadletterReason: "MaxDeliveryCountExceeded",
        TopTerms: ["timeout"],
        OccurrenceCount: 5,
        BlastRadius: 5,
        IsRecurring: false,
        PendingDecisionCount: 1,
        RecoveryEntryCount: 2,
        OpenRecoveryEntryCount: 0,
        AnomalyFlagCount: 1,
        DriftFindingCount: 0,
        CorrelationHypothesisCount: 0,
        PreventionTriggerCount: 0,
        ReplayPlanCount: 0);

    [Fact]
    public void Constructor_NullHttpClientFactory_Throws()
    {
        var act = () => new ReasoningAgentClient(null!, Options.Create(new ReasoningAgentOptions()), NullLogger<ReasoningAgentClient>.Instance);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        var act = () => new ReasoningAgentClient(new Mock<IHttpClientFactory>().Object, null!, NullLogger<ReasoningAgentClient>.Instance);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new ReasoningAgentClient(new Mock<IHttpClientFactory>().Object, Options.Create(new ReasoningAgentOptions()), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ProposeAsync_Disabled_ReturnsEmptyWithoutCallingHttp()
    {
        var called = false;
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            called = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var sut = CreateSut(options: new ReasoningAgentOptions { Enabled = false, ServiceUrl = "http://agent.internal:8010" }, handler: handler);

        var result = await sut.ProposeAsync([CreateEvidence()]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        called.Should().BeFalse();
    }

    [Fact]
    public async Task ProposeAsync_EmptyEvidence_ReturnsEmptyWithoutCallingHttp()
    {
        var called = false;
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            called = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var sut = CreateSut(handler: handler);

        var result = await sut.ProposeAsync([]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        called.Should().BeFalse();
    }

    [Fact]
    public async Task ProposeAsync_Success_ReturnsMappedProposals()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            request.RequestUri!.AbsolutePath.Should().Be("/propose");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    proposals = new[]
                    {
                        new
                        {
                            @ref = "ns-1:sig-1",
                            summary = "This signature has a pending decision and recurring timeouts.",
                            considerations = new[] { "Review the downstream timeout budget." },
                        },
                    },
                    method = "ollama",
                    model = "llama3.1",
                }),
            };
        });
        var sut = CreateSut(handler: handler);

        var result = await sut.ProposeAsync([CreateEvidence()]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value[0].Ref.Should().Be("ns-1:sig-1");
        result.Value[0].Summary.Should().Contain("pending decision");
        result.Value[0].Considerations.Should().ContainSingle();
    }

    [Fact]
    public async Task ProposeAsync_ProposalWithUnknownRef_IsDropped()
    {
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                proposals = new[]
                {
                    new { @ref = "not-a-real-ref", summary = "hallucinated", considerations = Array.Empty<string>() },
                },
                method = "ollama",
                model = "llama3.1",
            }),
        });
        var sut = CreateSut(handler: handler);

        var result = await sut.ProposeAsync([CreateEvidence("ns-1:sig-1")]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task ProposeAsync_ProposalWithBlankSummary_IsDropped()
    {
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                proposals = new[]
                {
                    new { @ref = "ns-1:sig-1", summary = "   ", considerations = Array.Empty<string>() },
                },
                method = "ollama",
                model = "llama3.1",
            }),
        });
        var sut = CreateSut(handler: handler);

        var result = await sut.ProposeAsync([CreateEvidence("ns-1:sig-1")]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task ProposeAsync_NonSuccessStatusCode_ReturnsEmptySuccess()
    {
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = CreateSut(handler: handler);

        var result = await sut.ProposeAsync([CreateEvidence()]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task ProposeAsync_HttpRequestException_ReturnsEmptySuccess()
    {
        var handler = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("connection refused"));
        var sut = CreateSut(handler: handler);

        var result = await sut.ProposeAsync([CreateEvidence()]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task ProposeAsync_MalformedJson_ReturnsEmptySuccess()
    {
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json", System.Text.Encoding.UTF8, "application/json"),
        });
        var sut = CreateSut(handler: handler);

        var result = await sut.ProposeAsync([CreateEvidence()]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task IsAvailableAsync_Disabled_ReturnsFalseWithoutCallingHttp()
    {
        var called = false;
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            called = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var sut = CreateSut(options: new ReasoningAgentOptions { Enabled = false, ServiceUrl = "http://agent.internal:8010" }, handler: handler);

        var result = await sut.IsAvailableAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
        called.Should().BeFalse();
    }

    [Fact]
    public async Task IsAvailableAsync_ReadyButNoReasoningBackendConfigured_ReturnsFalse()
    {
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"status":"ok","version":"0.1.0","ready":true,"reasoning_configured":false}""",
                System.Text.Encoding.UTF8, "application/json"),
        });
        var sut = CreateSut(handler: handler);

        var result = await sut.IsAvailableAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task IsAvailableAsync_ReadyAndReasoningConfigured_ReturnsTrue()
    {
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"status":"ok","version":"0.1.0","ready":true,"reasoning_configured":true}""",
                System.Text.Encoding.UTF8, "application/json"),
        });
        var sut = CreateSut(handler: handler);

        var result = await sut.IsAvailableAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task IsAvailableAsync_Unreachable_ReturnsFalse()
    {
        var handler = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("connection refused"));
        var sut = CreateSut(handler: handler);

        var result = await sut.IsAvailableAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
        {
            _responder = (req, ct) => Task.FromResult(responder(req, ct));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _responder(request, cancellationToken);
    }
}
