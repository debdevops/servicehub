using FluentAssertions;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Enums;
using ServiceHub.Simulator.Providers.Aws;
using ServiceHub.Simulator.Providers.Azure;
using ServiceHub.Simulator.Providers.Gcp;
using ServiceHub.Simulator.Store;

namespace ServiceHub.UnitTests.Simulator;

/// <summary>
/// Unit tests for SimulatedAwsSender, SimulatedAzureSender, and SimulatedGcpSender.
/// Uses the real <see cref="InMemorySimulatorStore"/> to exercise the full send path.
/// </summary>
public sealed class SimulatedSendersTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Shared helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly Guid TestNsId = Guid.NewGuid();
    private const string TestEntityName = "test-queue";

    /// <summary>
    /// Builds a store pre-populated with one entity for <see cref="TestNsId"/>.
    /// </summary>
    private static InMemorySimulatorStore BuildStoreWithEntity(
        string entityName = TestEntityName,
        CloudProviderType provider = CloudProviderType.Aws)
    {
        var store = new InMemorySimulatorStore();
        store.RegisterEntity(new SimulatorEntity
        {
            Name = entityName,
            EntityType = "queue",
            Provider = provider
        }, TestNsId);
        return store;
    }

    private static SendMessageRequest ValidRequest(string? body = "hello") =>
        new(TestNsId, TestEntityName, body ?? "");

    // ═════════════════════════════════════════════════════════════════════════
    // SimulatedAwsSender
    // ═════════════════════════════════════════════════════════════════════════

    #region SimulatedAwsSender

    [Fact]
    public void AwsSender_Constructor_NullStore_Throws()
    {
        var act = () => new SimulatedAwsSender(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("store");
    }

    [Fact]
    public async Task AwsSender_SendAsync_NullRequest_Throws()
    {
        var sender = new SimulatedAwsSender(BuildStoreWithEntity());
        var act = async () => await sender.SendAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task AwsSender_SendAsync_NullNamespaceId_ReturnsValidationFailure()
    {
        var sender = new SimulatedAwsSender(BuildStoreWithEntity());
        var req = new SendMessageRequest(null, TestEntityName, "body");
        var result = await sender.SendAsync(req);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Simulator.MissingNamespace");
    }

    [Fact]
    public async Task AwsSender_SendAsync_EmptyEntityName_ReturnsValidationFailure()
    {
        var sender = new SimulatedAwsSender(BuildStoreWithEntity());
        var req = new SendMessageRequest(TestNsId, "   ", "body");
        var result = await sender.SendAsync(req);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Simulator.MissingEntity");
    }

    [Fact]
    public async Task AwsSender_SendAsync_EntityNotFound_ReturnsNotFound()
    {
        var store = new InMemorySimulatorStore(); // empty store
        var sender = new SimulatedAwsSender(store);
        var req = new SendMessageRequest(TestNsId, "nonexistent-queue", "body");
        var result = await sender.SendAsync(req);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Simulator.EntityNotFound");
    }

    [Fact]
    public async Task AwsSender_SendAsync_ValidRequest_ReturnsSuccess()
    {
        var store = BuildStoreWithEntity();
        var sender = new SimulatedAwsSender(store);

        var result = await sender.SendAsync(ValidRequest("aws-message"));

        result.IsSuccess.Should().BeTrue();
        var entity = store.GetEntity(TestNsId, TestEntityName);
        entity.Should().NotBeNull();
        entity!.PeekMessages(10).Should().HaveCount(1);
        entity.PeekMessages(10)[0].Body.Should().Be("aws-message");
        entity.PeekMessages(10)[0].Provider.Should().Be(CloudProviderType.Aws);
    }

    [Fact]
    public async Task AwsSender_SendAsync_SetsCorrelationId()
    {
        var store = BuildStoreWithEntity();
        var sender = new SimulatedAwsSender(store);
        var req = new SendMessageRequest(TestNsId, TestEntityName, "body", CorrelationId: "corr-123");

        await sender.SendAsync(req);

        var msg = store.GetEntity(TestNsId, TestEntityName)!.PeekMessages(1)[0];
        msg.CorrelationId.Should().Be("corr-123");
    }

    [Fact]
    public async Task AwsSender_SendBatchAsync_NullRequests_Throws()
    {
        var sender = new SimulatedAwsSender(BuildStoreWithEntity());
        var act = async () => await sender.SendBatchAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task AwsSender_SendBatchAsync_EmptyCollection_ReturnsSuccess()
    {
        var sender = new SimulatedAwsSender(BuildStoreWithEntity());
        var result = await sender.SendBatchAsync(Array.Empty<SendMessageRequest>());
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AwsSender_SendBatchAsync_MultipleMessages_AllEnqueued()
    {
        var store = BuildStoreWithEntity();
        var sender = new SimulatedAwsSender(store);
        var requests = new[]
        {
            new SendMessageRequest(TestNsId, TestEntityName, "msg-1"),
            new SendMessageRequest(TestNsId, TestEntityName, "msg-2"),
            new SendMessageRequest(TestNsId, TestEntityName, "msg-3")
        };

        var result = await sender.SendBatchAsync(requests);

        result.IsSuccess.Should().BeTrue();
        store.GetEntity(TestNsId, TestEntityName)!.PeekMessages(10).Should().HaveCount(3);
    }

    [Fact]
    public async Task AwsSender_SendBatchAsync_FirstFailure_StopsProcessing()
    {
        var store = new InMemorySimulatorStore(); // no entities registered
        var sender = new SimulatedAwsSender(store);
        var requests = new[]
        {
            new SendMessageRequest(TestNsId, "missing-queue", "msg-1"),
            new SendMessageRequest(TestNsId, "missing-queue", "msg-2")
        };

        var result = await sender.SendBatchAsync(requests);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Simulator.EntityNotFound");
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    // SimulatedAzureSender
    // ═════════════════════════════════════════════════════════════════════════

    #region SimulatedAzureSender

    [Fact]
    public void AzureSender_Constructor_NullStore_Throws()
    {
        var act = () => new SimulatedAzureSender(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("store");
    }

    [Fact]
    public async Task AzureSender_SendAsync_NullRequest_Throws()
    {
        var sender = new SimulatedAzureSender(BuildStoreWithEntity(provider: CloudProviderType.Azure));
        var act = async () => await sender.SendAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task AzureSender_SendAsync_NullNamespaceId_ReturnsValidationFailure()
    {
        var sender = new SimulatedAzureSender(BuildStoreWithEntity(provider: CloudProviderType.Azure));
        var req = new SendMessageRequest(null, TestEntityName, "body");
        var result = await sender.SendAsync(req);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Simulator.MissingNamespace");
    }

    [Fact]
    public async Task AzureSender_SendAsync_EmptyEntityName_ReturnsValidationFailure()
    {
        var sender = new SimulatedAzureSender(BuildStoreWithEntity(provider: CloudProviderType.Azure));
        var req = new SendMessageRequest(TestNsId, "", "body");
        var result = await sender.SendAsync(req);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Simulator.MissingEntity");
    }

    [Fact]
    public async Task AzureSender_SendAsync_EntityNotFound_ReturnsNotFound()
    {
        var store = new InMemorySimulatorStore();
        var sender = new SimulatedAzureSender(store);
        var req = new SendMessageRequest(TestNsId, "no-such-queue", "body");
        var result = await sender.SendAsync(req);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Simulator.EntityNotFound");
    }

    [Fact]
    public async Task AzureSender_SendAsync_ValidRequest_EnqueuesMessage()
    {
        var store = BuildStoreWithEntity(provider: CloudProviderType.Azure);
        var sender = new SimulatedAzureSender(store);

        var result = await sender.SendAsync(new SendMessageRequest(TestNsId, TestEntityName, "azure-msg"));

        result.IsSuccess.Should().BeTrue();
        var msgs = store.GetEntity(TestNsId, TestEntityName)!.PeekMessages(10);
        msgs.Should().HaveCount(1);
        msgs[0].Body.Should().Be("azure-msg");
        msgs[0].Provider.Should().Be(CloudProviderType.Azure);
    }

    [Fact]
    public async Task AzureSender_SendAsync_SetsSessionId()
    {
        var store = BuildStoreWithEntity(provider: CloudProviderType.Azure);
        var sender = new SimulatedAzureSender(store);
        var req = new SendMessageRequest(TestNsId, TestEntityName, "body", SessionId: "sess-abc");

        await sender.SendAsync(req);

        var msg = store.GetEntity(TestNsId, TestEntityName)!.PeekMessages(1)[0];
        msg.SessionId.Should().Be("sess-abc");
    }

    [Fact]
    public async Task AzureSender_SendBatchAsync_NullRequests_Throws()
    {
        var sender = new SimulatedAzureSender(BuildStoreWithEntity(provider: CloudProviderType.Azure));
        var act = async () => await sender.SendBatchAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task AzureSender_SendBatchAsync_EmptyList_ReturnsSuccess()
    {
        var sender = new SimulatedAzureSender(BuildStoreWithEntity(provider: CloudProviderType.Azure));
        var result = await sender.SendBatchAsync(Array.Empty<SendMessageRequest>());
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AzureSender_SendBatchAsync_Multiple_AllEnqueued()
    {
        var store = BuildStoreWithEntity(provider: CloudProviderType.Azure);
        var sender = new SimulatedAzureSender(store);

        var result = await sender.SendBatchAsync(new[]
        {
            new SendMessageRequest(TestNsId, TestEntityName, "a"),
            new SendMessageRequest(TestNsId, TestEntityName, "b"),
        });

        result.IsSuccess.Should().BeTrue();
        store.GetEntity(TestNsId, TestEntityName)!.PeekMessages(10).Should().HaveCount(2);
    }

    [Fact]
    public async Task AzureSender_SendBatchAsync_FailsOnFirstError()
    {
        var store = new InMemorySimulatorStore();
        var sender = new SimulatedAzureSender(store);

        var result = await sender.SendBatchAsync(new[]
        {
            new SendMessageRequest(TestNsId, "ghost-queue", "x")
        });

        result.IsFailure.Should().BeTrue();
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    // SimulatedGcpSender
    // ═════════════════════════════════════════════════════════════════════════

    #region SimulatedGcpSender

    [Fact]
    public void GcpSender_Constructor_NullStore_Throws()
    {
        var act = () => new SimulatedGcpSender(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("store");
    }

    [Fact]
    public async Task GcpSender_SendAsync_NullRequest_Throws()
    {
        var sender = new SimulatedGcpSender(BuildStoreWithEntity(provider: CloudProviderType.Gcp));
        var act = async () => await sender.SendAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GcpSender_SendAsync_NullNamespaceId_ReturnsValidationFailure()
    {
        var sender = new SimulatedGcpSender(BuildStoreWithEntity(provider: CloudProviderType.Gcp));
        var req = new SendMessageRequest(null, TestEntityName, "body");
        var result = await sender.SendAsync(req);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Simulator.MissingNamespace");
    }

    [Fact]
    public async Task GcpSender_SendAsync_EmptyEntityName_ReturnsValidationFailure()
    {
        var sender = new SimulatedGcpSender(BuildStoreWithEntity(provider: CloudProviderType.Gcp));
        var req = new SendMessageRequest(TestNsId, "  ", "body");
        var result = await sender.SendAsync(req);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Simulator.MissingEntity");
    }

    [Fact]
    public async Task GcpSender_SendAsync_EntityNotFound_ReturnsNotFound()
    {
        var store = new InMemorySimulatorStore();
        var sender = new SimulatedGcpSender(store);
        var req = new SendMessageRequest(TestNsId, "nonexistent-topic", "body");
        var result = await sender.SendAsync(req);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Simulator.EntityNotFound");
    }

    [Fact]
    public async Task GcpSender_SendAsync_ValidRequest_EnqueuesMessage()
    {
        var store = BuildStoreWithEntity(provider: CloudProviderType.Gcp);
        var sender = new SimulatedGcpSender(store);

        var result = await sender.SendAsync(new SendMessageRequest(TestNsId, TestEntityName, "gcp-msg"));

        result.IsSuccess.Should().BeTrue();
        var msgs = store.GetEntity(TestNsId, TestEntityName)!.PeekMessages(10);
        msgs.Should().HaveCount(1);
        msgs[0].Body.Should().Be("gcp-msg");
        msgs[0].Provider.Should().Be(CloudProviderType.Gcp);
    }

    [Fact]
    public async Task GcpSender_SendAsync_SetsOrderingKey_FromSessionId()
    {
        var store = BuildStoreWithEntity(provider: CloudProviderType.Gcp);
        var sender = new SimulatedGcpSender(store);
        var req = new SendMessageRequest(TestNsId, TestEntityName, "ordered", SessionId: "session-key-1");

        await sender.SendAsync(req);

        var msg = store.GetEntity(TestNsId, TestEntityName)!.PeekMessages(1)[0];
        // GCP sets OrderingKey from SessionId
        msg.SessionId.Should().Be("session-key-1");
    }

    [Fact]
    public async Task GcpSender_SendBatchAsync_NullRequests_Throws()
    {
        var sender = new SimulatedGcpSender(BuildStoreWithEntity(provider: CloudProviderType.Gcp));
        var act = async () => await sender.SendBatchAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GcpSender_SendBatchAsync_EmptyList_ReturnsSuccess()
    {
        var sender = new SimulatedGcpSender(BuildStoreWithEntity(provider: CloudProviderType.Gcp));
        var result = await sender.SendBatchAsync(Array.Empty<SendMessageRequest>());
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GcpSender_SendBatchAsync_MultipleMessages_AllEnqueued()
    {
        var store = BuildStoreWithEntity(provider: CloudProviderType.Gcp);
        var sender = new SimulatedGcpSender(store);

        var result = await sender.SendBatchAsync(new[]
        {
            new SendMessageRequest(TestNsId, TestEntityName, "g1"),
            new SendMessageRequest(TestNsId, TestEntityName, "g2"),
            new SendMessageRequest(TestNsId, TestEntityName, "g3"),
        });

        result.IsSuccess.Should().BeTrue();
        store.GetEntity(TestNsId, TestEntityName)!.PeekMessages(10).Should().HaveCount(3);
    }

    [Fact]
    public async Task GcpSender_SendBatchAsync_StopsOnFirstFailure()
    {
        var store = new InMemorySimulatorStore();
        var sender = new SimulatedGcpSender(store);

        var result = await sender.SendBatchAsync(new[]
        {
            new SendMessageRequest(TestNsId, "no-topic", "x"),
            new SendMessageRequest(TestNsId, "no-topic", "y"),
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Simulator.EntityNotFound");
    }

    #endregion
}
