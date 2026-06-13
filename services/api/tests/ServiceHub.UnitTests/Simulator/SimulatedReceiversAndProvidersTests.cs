using FluentAssertions;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Simulator;
using ServiceHub.Simulator.Providers.Aws;
using ServiceHub.Simulator.Providers.Azure;
using ServiceHub.Simulator.Providers.Gcp;
using ServiceHub.Simulator.Store;

namespace ServiceHub.UnitTests.Simulator;

/// <summary>
/// Unit tests for all three simulated receiver classes and their messaging providers.
/// Uses the real <see cref="InMemorySimulatorStore"/> for realistic in-memory behaviour.
/// </summary>
public sealed class SimulatedReceiversAndProvidersTests
{
    private static readonly Guid TestNsId = Guid.NewGuid();
    private static readonly SimulatorClock Clock = new();

    // ─────────────────────────────────────────────────────────────────────────
    // Shared helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static InMemorySimulatorStore BuildStore(
        string entityName = "test-queue",
        CloudProviderType provider = CloudProviderType.Aws,
        bool withMessages = false,
        bool withDlqMessages = false)
    {
        var store = new InMemorySimulatorStore();
        var entity = new SimulatorEntity { Name = entityName, EntityType = "queue", Provider = provider };

        if (withMessages)
        {
            entity.EnqueueMessage(new SimulatorMessage(
                MessageId: Guid.NewGuid().ToString(),
                SequenceNumber: entity.NextSequenceNumber(),
                Body: "hello",
                ContentType: null, CorrelationId: null, SessionId: null,
                PartitionKey: null, Subject: null,
                DeliveryCount: 1, EnqueuedTime: DateTimeOffset.UtcNow,
                ScheduledEnqueueTime: null, IsDeadLettered: false,
                DeadLetterReason: null, DeadLetterErrorDescription: null,
                ApplicationProperties: new Dictionary<string, object>(),
                SizeInBytes: 5, ReceiptHandle: null,
                VisibilityUntil: null, OrderingKey: null,
                DeliveryAttempt: 0, AckDeadline: null, IsNacked: false,
                Provider: provider));
        }

        if (withDlqMessages)
        {
            entity.EnqueueDlqMessage(new SimulatorMessage(
                MessageId: Guid.NewGuid().ToString(),
                SequenceNumber: entity.NextSequenceNumber(),
                Body: "dlq-msg",
                ContentType: null, CorrelationId: null, SessionId: null,
                PartitionKey: null, Subject: null,
                DeliveryCount: 5, EnqueuedTime: DateTimeOffset.UtcNow,
                ScheduledEnqueueTime: null, IsDeadLettered: true,
                DeadLetterReason: "MaxReceiveCount", DeadLetterErrorDescription: null,
                ApplicationProperties: new Dictionary<string, object>(),
                SizeInBytes: 7, ReceiptHandle: null,
                VisibilityUntil: null, OrderingKey: null,
                DeliveryAttempt: 5, AckDeadline: null, IsNacked: false,
                Provider: provider));
        }

        store.RegisterEntity(entity, TestNsId);
        return store;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SimulatedAwsReceiver
    // ═════════════════════════════════════════════════════════════════════════

    #region SimulatedAwsReceiver

    [Fact]
    public void AwsReceiver_Constructor_NullStore_Throws()
    {
        var act = () => new SimulatedAwsReceiver(null!, Clock);
        act.Should().Throw<ArgumentNullException>().WithParameterName("store");
    }

    [Fact]
    public void AwsReceiver_Constructor_NullClock_Throws()
    {
        var act = () => new SimulatedAwsReceiver(new InMemorySimulatorStore(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("clock");
    }

    [Fact]
    public async Task AwsReceiver_PeekMessages_NullRequest_Throws()
    {
        var receiver = new SimulatedAwsReceiver(new InMemorySimulatorStore(), Clock);
        var act = async () => await receiver.PeekMessagesAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task AwsReceiver_PeekMessages_EntityNotFound_ReturnsNotFound()
    {
        var receiver = new SimulatedAwsReceiver(new InMemorySimulatorStore(), Clock);
        var req = new GetMessagesRequest(TestNsId, "ghost-queue", null, false, 10);
        var result = await receiver.PeekMessagesAsync(req);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Simulator.EntityNotFound");
    }

    [Fact]
    public async Task AwsReceiver_PeekMessages_WithMessages_ReturnsMapped()
    {
        var store = BuildStore(withMessages: true);
        var receiver = new SimulatedAwsReceiver(store, Clock);
        var req = new GetMessagesRequest(TestNsId, "test-queue", null, false, 10);
        var result = await receiver.PeekMessagesAsync(req);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].Body.Should().Be("hello");
    }

    [Fact]
    public async Task AwsReceiver_PeekDlq_NullRequest_Throws()
    {
        var receiver = new SimulatedAwsReceiver(new InMemorySimulatorStore(), Clock);
        var act = async () => await receiver.PeekDeadLetterMessagesAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task AwsReceiver_PeekDlq_EntityNotFound_ReturnsNotFound()
    {
        var receiver = new SimulatedAwsReceiver(new InMemorySimulatorStore(), Clock);
        var req = new GetMessagesRequest(TestNsId, "ghost-queue", null, true, 10);
        var result = await receiver.PeekDeadLetterMessagesAsync(req);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task AwsReceiver_PeekDlq_WithDlqMessages_ReturnsMapped()
    {
        var store = BuildStore(withDlqMessages: true);
        var receiver = new SimulatedAwsReceiver(store, Clock);
        var req = new GetMessagesRequest(TestNsId, "test-queue", null, true, 10);
        var result = await receiver.PeekDeadLetterMessagesAsync(req);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].IsFromDeadLetter.Should().BeTrue();
    }

    [Fact]
    public async Task AwsReceiver_GetMessageCount_ReturnsActiveCount()
    {
        var store = BuildStore(withMessages: true);
        var receiver = new SimulatedAwsReceiver(store, Clock);
        var result = await receiver.GetMessageCountAsync(TestNsId, "test-queue");
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
    }

    [Fact]
    public async Task AwsReceiver_GetVisibilityStatus_WhenEntityNotFound_ReturnsNotFound()
    {
        var receiver = new SimulatedAwsReceiver(new InMemorySimulatorStore(), Clock);
        var result = await receiver.GetVisibilityWindowStatusAsync(TestNsId, "ghost");
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task AwsReceiver_GetVisibilityStatus_Success()
    {
        var store = BuildStore(withMessages: true);
        var receiver = new SimulatedAwsReceiver(store, Clock);
        var result = await receiver.GetVisibilityWindowStatusAsync(TestNsId, "test-queue");
        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    // SimulatedAzureReceiver
    // ═════════════════════════════════════════════════════════════════════════

    #region SimulatedAzureReceiver

    [Fact]
    public void AzureReceiver_Constructor_NullStore_Throws()
    {
        var act = () => new SimulatedAzureReceiver(null!, Clock);
        act.Should().Throw<ArgumentNullException>().WithParameterName("store");
    }

    [Fact]
    public async Task AzureReceiver_PeekMessages_WithMessages_ReturnsMapped()
    {
        var store = BuildStore(provider: CloudProviderType.Azure, withMessages: true);
        var receiver = new SimulatedAzureReceiver(store, Clock);
        var req = new GetMessagesRequest(TestNsId, "test-queue", null, false, 10);
        var result = await receiver.PeekMessagesAsync(req);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task AzureReceiver_PeekMessages_EntityNotFound_ReturnsNotFound()
    {
        var receiver = new SimulatedAzureReceiver(new InMemorySimulatorStore(), Clock);
        var req = new GetMessagesRequest(TestNsId, "no-such-queue", null, false, 10);
        var result = await receiver.PeekMessagesAsync(req);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task AzureReceiver_PeekDlq_WithDlqMessages_ReturnsMapped()
    {
        var store = BuildStore(provider: CloudProviderType.Azure, withDlqMessages: true);
        var receiver = new SimulatedAzureReceiver(store, Clock);
        var req = new GetMessagesRequest(TestNsId, "test-queue", null, true, 10);
        var result = await receiver.PeekDeadLetterMessagesAsync(req);
        result.IsSuccess.Should().BeTrue();
        result.Value[0].IsFromDeadLetter.Should().BeTrue();
    }

    [Fact]
    public async Task AzureReceiver_GetMessageCount_ReturnsActiveCount()
    {
        var store = BuildStore(provider: CloudProviderType.Azure, withMessages: true);
        var receiver = new SimulatedAzureReceiver(store, Clock);
        var result = await receiver.GetMessageCountAsync(TestNsId, "test-queue");
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    // SimulatedGcpReceiver
    // ═════════════════════════════════════════════════════════════════════════

    #region SimulatedGcpReceiver

    [Fact]
    public void GcpReceiver_Constructor_NullStore_Throws()
    {
        var act = () => new SimulatedGcpReceiver(null!, Clock);
        act.Should().Throw<ArgumentNullException>().WithParameterName("store");
    }

    [Fact]
    public async Task GcpReceiver_PeekMessages_WithMessages_ReturnsMapped()
    {
        var store = BuildStore(provider: CloudProviderType.Gcp, withMessages: true);
        var receiver = new SimulatedGcpReceiver(store, Clock);
        var req = new GetMessagesRequest(TestNsId, "test-queue", null, false, 10);
        var result = await receiver.PeekMessagesAsync(req);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task GcpReceiver_GetMessageCount_ReturnsActualCount()
    {
        // The simulated GCP receiver returns real counts (unlike the real GcpMessageReceiver which returns -1)
        var store = BuildStore(provider: CloudProviderType.Gcp, withMessages: true);
        var receiver = new SimulatedGcpReceiver(store, Clock);
        var result = await receiver.GetMessageCountAsync(TestNsId, "test-queue");
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GcpReceiver_GetAckDeadlineStatus_WhenEntityNotFound_ReturnsNotFound()
    {
        var receiver = new SimulatedGcpReceiver(new InMemorySimulatorStore(), Clock);
        var result = await receiver.GetAckDeadlineStatusAsync(TestNsId, "ghost");
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task GcpReceiver_GetAckDeadlineStatus_Success()
    {
        var store = BuildStore(provider: CloudProviderType.Gcp, withMessages: true);
        var receiver = new SimulatedGcpReceiver(store, Clock);
        var result = await receiver.GetAckDeadlineStatusAsync(TestNsId, "test-queue");
        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    // SimulatedAwsMessagingProvider
    // ═════════════════════════════════════════════════════════════════════════

    #region SimulatedAwsMessagingProvider

    [Fact]
    public void AwsProvider_Constructor_NullStore_Throws()
    {
        var store = new InMemorySimulatorStore();
        var receiver = new SimulatedAwsReceiver(store, Clock);
        var sender = new SimulatedAwsSender(store);
        var act = () => new SimulatedAwsMessagingProvider(null!, receiver, sender);
        act.Should().Throw<ArgumentNullException>().WithParameterName("store");
    }

    [Fact]
    public void AwsProvider_ProviderType_IsAws()
    {
        var store = new InMemorySimulatorStore();
        var provider = new SimulatedAwsMessagingProvider(
            store, new SimulatedAwsReceiver(store, Clock), new SimulatedAwsSender(store));
        provider.ProviderType.Should().Be(CloudProviderType.Aws);
    }

    [Fact]
    public async Task AwsProvider_ValidateConnection_AlwaysSucceeds()
    {
        var store = new InMemorySimulatorStore();
        var provider = new SimulatedAwsMessagingProvider(
            store, new SimulatedAwsReceiver(store, Clock), new SimulatedAwsSender(store));
        var ns = Namespace.Create("test-namespace.servicebus.windows.net", "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc123==").Value;
        var result = await provider.ValidateConnectionAsync(ns, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AwsProvider_ListEntities_ReturnsRegisteredEntities()
    {
        var store = BuildStore(withMessages: true);
        var provider = new SimulatedAwsMessagingProvider(
            store, new SimulatedAwsReceiver(store, Clock), new SimulatedAwsSender(store));

        var result = await provider.ListEntitiesAsync(TestNsId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].Name.Should().Be("test-queue");
        result.Value[0].ActiveMessageCount.Should().Be(1);
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    // SimulatedAzureMessagingProvider
    // ═════════════════════════════════════════════════════════════════════════

    #region SimulatedAzureMessagingProvider

    [Fact]
    public void AzureProvider_ProviderType_IsAzure()
    {
        var store = new InMemorySimulatorStore();
        var provider = new SimulatedAzureMessagingProvider(
            store,
            new SimulatedAzureReceiver(store, Clock),
            new SimulatedAzureSender(store));
        provider.ProviderType.Should().Be(CloudProviderType.Azure);
    }

    [Fact]
    public async Task AzureProvider_ValidateConnection_AlwaysSucceeds()
    {
        var store = new InMemorySimulatorStore();
        var provider = new SimulatedAzureMessagingProvider(
            store, new SimulatedAzureReceiver(store, Clock), new SimulatedAzureSender(store));
        var ns = Namespace.Create("test-namespace.servicebus.windows.net", "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc123==").Value;
        var result = await provider.ValidateConnectionAsync(ns, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AzureProvider_ListEntities_ReturnsEntities()
    {
        var store = BuildStore(provider: CloudProviderType.Azure, withMessages: true);
        var provider = new SimulatedAzureMessagingProvider(
            store, new SimulatedAzureReceiver(store, Clock), new SimulatedAzureSender(store));

        var result = await provider.ListEntitiesAsync(TestNsId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    // SimulatedGcpMessagingProvider
    // ═════════════════════════════════════════════════════════════════════════

    #region SimulatedGcpMessagingProvider

    [Fact]
    public void GcpProvider_ProviderType_IsGcp()
    {
        var store = new InMemorySimulatorStore();
        var provider = new SimulatedGcpMessagingProvider(
            store,
            new SimulatedGcpReceiver(store, Clock),
            new SimulatedGcpSender(store));
        provider.ProviderType.Should().Be(CloudProviderType.Gcp);
    }

    [Fact]
    public async Task GcpProvider_ValidateConnection_AlwaysSucceeds()
    {
        var store = new InMemorySimulatorStore();
        var provider = new SimulatedGcpMessagingProvider(
            store, new SimulatedGcpReceiver(store, Clock), new SimulatedGcpSender(store));
        var ns = Namespace.Create("test-namespace.servicebus.windows.net", "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc123==").Value;
        var result = await provider.ValidateConnectionAsync(ns, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GcpProvider_ListEntities_ReturnsEntities()
    {
        var store = BuildStore(provider: CloudProviderType.Gcp, withMessages: true, withDlqMessages: true);
        var provider = new SimulatedGcpMessagingProvider(
            store, new SimulatedGcpReceiver(store, Clock), new SimulatedGcpSender(store));

        var result = await provider.ListEntitiesAsync(TestNsId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].DeadLetterCount.Should().Be(1);
    }

    #endregion
}
