using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.AI;

namespace ServiceHub.UnitTests.Infrastructure.AI;

public sealed class AIServiceClientTests
{
    private static AIServiceClient CreateSut(
        AIServiceOptions? options = null,
        HttpMessageHandler? handler = null,
        ILogger<AIServiceClient>? logger = null)
    {
        options ??= new AIServiceOptions { Enabled = true, ServiceUrl = "http://ai.internal:8000" };
        handler ??= new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(options.ServiceUrl) };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(AIServiceClient.HttpClientName)).Returns(httpClient);

        return new AIServiceClient(
            factory.Object,
            Options.Create(options),
            logger ?? NullLogger<AIServiceClient>.Instance);
    }

    private static DlqMessage CreateDlqMessage(long id, string deadLetterReason = "MaxDeliveryCountExceeded")
    {
        var message = new DlqMessage
        {
            MessageId = $"msg-{id}",
            SequenceNumber = id,
            BodyHash = "deadbeef",
            NamespaceId = Guid.NewGuid(),
            OwnerId = "entra:test-owner",
            EntityName = "orders-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeadLetterReason = deadLetterReason,
            DeadLetterErrorDescription = "System.TimeoutException: the operation timed out",
        };

        typeof(DlqMessage).GetProperty(nameof(DlqMessage.Id))!.SetValue(message, id);
        return message;
    }

    [Fact]
    public void Constructor_NullHttpClientFactory_Throws()
    {
        var act = () => new AIServiceClient(null!, Options.Create(new AIServiceOptions()), NullLogger<AIServiceClient>.Instance);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        var act = () => new AIServiceClient(new Mock<IHttpClientFactory>().Object, null!, NullLogger<AIServiceClient>.Instance);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new AIServiceClient(new Mock<IHttpClientFactory>().Object, Options.Create(new AIServiceOptions()), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task AnalyzeMessagesAsync_Success_ReturnsClustersWithCorrectRefToMessageIdMap()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            request.RequestUri!.AbsolutePath.Should().Be("/analyze");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    clusters = new[]
                    {
                        new
                        {
                            size = 2,
                            representative_ref = "0",
                            top_terms = new[] { "timeout" },
                            first_occurrence_ref = "0",
                            last_occurrence_ref = "1",
                            dominant_entity = "orders-queue",
                            dominant_deadletter_reason = "MaxDeliveryCountExceeded",
                            member_refs = new[] { "0", "1" },
                            dominant_deadletter_reason_count = 2,
                        },
                    },
                    singletons = Array.Empty<object>(),
                    method = "clustered",
                }),
            };
        });
        var sut = CreateSut(handler: handler);
        var messages = new[] { CreateDlqMessage(101), CreateDlqMessage(102) };

        var result = await sut.AnalyzeMessagesAsync(messages);

        result.IsSuccess.Should().BeTrue();
        result.Value.Method.Should().Be("clustered");
        result.Value.Clusters.Should().ContainSingle();
        result.Value.Clusters[0].Size.Should().Be(2);
        result.Value.Clusters[0].RepresentativeRef.Should().Be("0");
        result.Value.Clusters[0].MemberRefs.Should().BeEquivalentTo(new[] { "0", "1" });
        result.Value.Clusters[0].DominantDeadletterReasonCount.Should().Be(2);
        result.Value.Singletons.Should().BeEmpty();
        result.Value.RefToMessageId.Should().BeEquivalentTo(new Dictionary<string, long>
        {
            ["0"] = 101,
            ["1"] = 102,
        });
    }

    [Fact]
    public async Task AnalyzeMessagesAsync_ClusterWithMissingMemberRefs_ReturnsFailure()
    {
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                clusters = new[]
                {
                    new
                    {
                        size = 2,
                        representative_ref = "0",
                        top_terms = new[] { "timeout" },
                        first_occurrence_ref = "0",
                        last_occurrence_ref = "1",
                        dominant_entity = "orders-queue",
                        dominant_deadletter_reason = "MaxDeliveryCountExceeded",
                        member_refs = (string[]?)null,
                        dominant_deadletter_reason_count = 2,
                    },
                },
                singletons = Array.Empty<object>(),
                method = "clustered",
            }),
        });
        var sut = CreateSut(handler: handler);

        var result = await sut.AnalyzeMessagesAsync(new[] { CreateDlqMessage(101), CreateDlqMessage(102) });

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task AnalyzeMessagesAsync_ClusterWithEmptyMemberRefs_ReturnsFailure()
    {
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                clusters = new[]
                {
                    new
                    {
                        size = 2,
                        representative_ref = "0",
                        top_terms = new[] { "timeout" },
                        first_occurrence_ref = "0",
                        last_occurrence_ref = "1",
                        dominant_entity = "orders-queue",
                        dominant_deadletter_reason = "MaxDeliveryCountExceeded",
                        member_refs = Array.Empty<string>(),
                        dominant_deadletter_reason_count = 2,
                    },
                },
                singletons = Array.Empty<object>(),
                method = "clustered",
            }),
        });
        var sut = CreateSut(handler: handler);

        var result = await sut.AnalyzeMessagesAsync(new[] { CreateDlqMessage(101), CreateDlqMessage(102) });

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task AnalyzeMessagesAsync_ServiceUnreachable_ReturnsFailureWithoutThrowing()
    {
        var handler = new StubHttpMessageHandler((Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>)((_, _) =>
            throw new HttpRequestException("connection refused")));
        var sut = CreateSut(handler: handler);

        var result = await sut.AnalyzeMessagesAsync(new[] { CreateDlqMessage(1) });

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task AnalyzeMessagesAsync_Timeout_ReturnsFailure()
    {
        var options = new AIServiceOptions { Enabled = true, ServiceUrl = "http://ai.internal:8000", TimeoutSeconds = 1 };
        var handler = new StubHttpMessageHandler((Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>)(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var sut = CreateSut(options, handler);

        var result = await sut.AnalyzeMessagesAsync(new[] { CreateDlqMessage(1) });

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task AnalyzeMessagesAsync_MalformedJson_ReturnsFailure()
    {
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json", System.Text.Encoding.UTF8, "application/json"),
        });
        var sut = CreateSut(handler: handler);

        var result = await sut.AnalyzeMessagesAsync(new[] { CreateDlqMessage(1) });

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task AnalyzeMessagesAsync_UnprocessableEntity_ReturnsFailureAndLogsReason()
    {
        const string reason = """{"detail":"records[].ref must be unique within a request"}""";
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage((HttpStatusCode)422)
        {
            Content = new StringContent(reason, System.Text.Encoding.UTF8, "application/json"),
        });
        var logger = new Mock<ILogger<AIServiceClient>>();
        var sut = CreateSut(handler: handler, logger: logger.Object);

        var result = await sut.AnalyzeMessagesAsync(new[] { CreateDlqMessage(1) });

        result.IsFailure.Should().BeTrue();
        logger.Invocations
            .Where(i => i.Method.Name == "Log" && i.Arguments[0]!.Equals(LogLevel.Warning))
            .Should().ContainSingle(i => i.Arguments[2]!.ToString()!.Contains("records[].ref must be unique"));
    }

    [Fact]
    public async Task AnalyzeMessagesAsync_RepeatedUnavailability_LogsOnce()
    {
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var logger = new Mock<ILogger<AIServiceClient>>();
        var sut = CreateSut(handler: handler, logger: logger.Object);
        var messages = new[] { CreateDlqMessage(1) };

        await sut.AnalyzeMessagesAsync(messages);
        await sut.AnalyzeMessagesAsync(messages);
        await sut.AnalyzeMessagesAsync(messages);

        logger.Invocations.Count(i => i.Method.Name == "Log" && i.Arguments[0]!.Equals(LogLevel.Warning))
            .Should().Be(1);
    }

    [Fact]
    public async Task GetMessageInsightsAsync_ReturnsFailure()
    {
        var sut = CreateSut();
        var message = new Message
        {
            MessageId = "test",
            SequenceNumber = 1,
            Body = "test body",
            EnqueuedTime = DateTimeOffset.UtcNow,
        };

        var result = await sut.GetMessageInsightsAsync(message);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("not yet implemented");
    }

    [Fact]
    public async Task DetectAnomaliesAsync_ReturnsFailure()
    {
        var sut = CreateSut();

        var result = await sut.DetectAnomaliesAsync(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("not yet implemented");
    }

    [Fact]
    public async Task GetAnomalyByIdAsync_ReturnsFailure()
    {
        var sut = CreateSut();

        var result = await sut.GetAnomalyByIdAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("not yet implemented");
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
        var sut = CreateSut(new AIServiceOptions { Enabled = false, ServiceUrl = "http://ai.internal:8000" }, handler);

        var result = await sut.IsAvailableAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
        called.Should().BeFalse();
    }

    [Fact]
    public async Task IsAvailableAsync_HealthyResponse_ReturnsTrue()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            request.RequestUri!.AbsolutePath.Should().Be("/health");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"status":"ok","version":"0.1.0","ready":true}""",
                    System.Text.Encoding.UTF8, "application/json"),
            };
        });
        var sut = CreateSut(handler: handler);

        var result = await sut.IsAvailableAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task IsAvailableAsync_NotReadyResponse_ReturnsFalse()
    {
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"status":"starting","version":"0.1.0","ready":false}""",
                System.Text.Encoding.UTF8, "application/json"),
        });
        var sut = CreateSut(handler: handler);

        var result = await sut.IsAvailableAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task IsAvailableAsync_NonSuccessStatusCode_ReturnsFalse()
    {
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = CreateSut(handler: handler);

        var result = await sut.IsAvailableAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task IsAvailableAsync_ConnectionRefused_ReturnsFalseWithoutThrowing()
    {
        var handler = new StubHttpMessageHandler((Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>)((_, _) =>
            throw new HttpRequestException("connection refused")));
        var sut = CreateSut(handler: handler);

        var result = await sut.IsAvailableAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task IsAvailableAsync_Timeout_ReturnsFalseWithoutThrowing()
    {
        var handler = new StubHttpMessageHandler((Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>)(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var sut = CreateSut(handler: handler);

        var result = await sut.IsAvailableAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task IsAvailableAsync_MalformedJson_ReturnsFalseWithoutThrowing()
    {
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json", System.Text.Encoding.UTF8, "application/json"),
        });
        var sut = CreateSut(handler: handler);

        var result = await sut.IsAvailableAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task IsAvailableAsync_NoServiceUrl_ReturnsFalseWithoutCallingHttp()
    {
        var called = false;
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            called = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var options = new AIServiceOptions { Enabled = true, ServiceUrl = string.Empty };
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://ai.internal:8000") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(AIServiceClient.HttpClientName)).Returns(httpClient);
        var sut = new AIServiceClient(factory.Object, Options.Create(options), NullLogger<AIServiceClient>.Instance);

        var result = await sut.IsAvailableAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
        called.Should().BeFalse();
    }

    [Fact]
    public async Task IsAvailableAsync_RepeatedFailures_LogsUnavailabilityOnce()
    {
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var logger = new Mock<ILogger<AIServiceClient>>();
        var sut = CreateSut(handler: handler, logger: logger.Object);

        // The 30s cache means a second call within the window wouldn't re-check anyway, so
        // directly exercise CheckHealthAsync's guard by calling IsAvailableAsync repeatedly
        // with a fresh SUT per "process" would defeat the point — instead assert the single
        // call logs exactly once.
        await sut.IsAvailableAsync();
        await sut.IsAvailableAsync();
        await sut.IsAvailableAsync();

        logger.Invocations.Count(i => i.Method.Name == "Log" && i.Arguments[0]!.Equals(LogLevel.Warning))
            .Should().Be(1);
    }

    [Fact]
    public async Task IsAvailableAsync_WithinCacheWindow_DoesNotRepeatHttpCall()
    {
        var callCount = 0;
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"status":"ok","version":"0.1.0","ready":true}""",
                    System.Text.Encoding.UTF8, "application/json"),
            };
        });
        var sut = CreateSut(handler: handler);

        await sut.IsAvailableAsync();
        await sut.IsAvailableAsync();
        await sut.IsAvailableAsync();

        callCount.Should().Be(1);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
        {
            _responder = (req, ct) => Task.FromResult(responder(req, ct));
        }

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _responder(request, cancellationToken);
    }
}
