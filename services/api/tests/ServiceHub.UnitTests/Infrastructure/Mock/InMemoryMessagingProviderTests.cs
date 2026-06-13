using FluentAssertions;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Mock;

namespace ServiceHub.UnitTests.Infrastructure.MockProvider;

/// <summary>
/// Unit tests for <see cref="InMemoryMessagingProvider"/> and <see cref="MockMessageStore"/>.
/// </summary>
public sealed class InMemoryMessagingProviderTests
{
    private readonly MockMessageStore _store = new();
    private readonly InMemoryMessagingProvider _sut;

    public InMemoryMessagingProviderTests()
    {
        _sut = new InMemoryMessagingProvider(_store);
    }

    // ── ProviderType ─────────────────────────────────────────────────────────

    [Fact]
    public void ProviderType_ReturnsAzure()
    {
        _sut.ProviderType.Should().Be(CloudProviderType.Azure);
    }

    // ── ValidateConnectionAsync ───────────────────────────────────────────────

    [Fact]
    public async Task ValidateConnectionAsync_AlwaysSucceeds()
    {
        var ns = BuildNamespace(MockNamespaces.MockNamespaceId, "mock://demo");
        var result = await _sut.ValidateConnectionAsync(ns, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    // ── ListEntitiesAsync — Azure namespace ───────────────────────────────────

    [Fact]
    public async Task ListEntitiesAsync_AzureNamespace_ReturnsFourEntities()
    {
        var result = await _sut.ListEntitiesAsync(MockNamespaces.MockNamespaceId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(4);
        result.Value.Should().AllSatisfy(e => e.Provider.Should().Be(CloudProviderType.Azure));
    }

    [Fact]
    public async Task ListEntitiesAsync_AzureNamespace_HasOrdersQueue()
    {
        var result = await _sut.ListEntitiesAsync(MockNamespaces.MockNamespaceId, CancellationToken.None);

        var orders = result.Value.Single(e => e.Name == "orders");
        orders.EntityType.Should().Be("Queue");
        orders.ActiveMessageCount.Should().Be(40);
        orders.DeadLetterCount.Should().Be(8);
    }

    // ── ListEntitiesAsync — AWS namespace ─────────────────────────────────────

    [Fact]
    public async Task ListEntitiesAsync_AwsNamespace_ReturnsFourEntities()
    {
        var result = await _sut.ListEntitiesAsync(MockNamespaces.AwsMockNamespaceId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(4);
        result.Value.Should().AllSatisfy(e => e.Provider.Should().Be(CloudProviderType.Aws));
    }

    // ── ListEntitiesAsync — GCP namespace ─────────────────────────────────────

    [Fact]
    public async Task ListEntitiesAsync_GcpNamespace_ReturnsFourEntities()
    {
        var result = await _sut.ListEntitiesAsync(MockNamespaces.GcpMockNamespaceId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(4);
        result.Value.Should().AllSatisfy(e => e.Provider.Should().Be(CloudProviderType.Gcp));
    }

    // ── ListEntitiesAsync — unknown namespace ─────────────────────────────────

    [Fact]
    public async Task ListEntitiesAsync_UnknownNamespace_ReturnsEmptyList()
    {
        var result = await _sut.ListEntitiesAsync(Guid.NewGuid(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    // ── GetMessageReceiver / GetMessageSender ─────────────────────────────────

    [Fact]
    public void GetMessageReceiver_ReturnsNonNull()
        => _sut.GetMessageReceiver().Should().NotBeNull();

    [Fact]
    public void GetMessageSender_ReturnsNonNull()
        => _sut.GetMessageSender().Should().NotBeNull();

    // ── MockMessageStore — seeded data ────────────────────────────────────────

    [Fact]
    public void Store_AzureOrders_HasFortyMessages()
    {
        var messages = _store.GetMessages(MockNamespaces.MockNamespaceId, "orders", dlq: false).ToList();
        messages.Should().HaveCount(40);
    }

    [Fact]
    public void Store_AzureOrdersDlq_HasEightMessages()
    {
        var messages = _store.GetMessages(MockNamespaces.MockNamespaceId, "orders", dlq: true).ToList();
        messages.Should().HaveCount(8);
    }

    [Fact]
    public void Store_AwsOrderProcessing_HasThirtyFiveMessages()
    {
        var messages = _store.GetMessages(MockNamespaces.AwsMockNamespaceId, "order-processing", dlq: false).ToList();
        messages.Should().HaveCount(35);
    }

    [Fact]
    public void Store_GcpPatientIntake_HasThirtyMessages()
    {
        var messages = _store.GetMessages(MockNamespaces.GcpMockNamespaceId, "patient-intake", dlq: false).ToList();
        messages.Should().HaveCount(30);
    }

    [Fact]
    public void Store_AllMessages_HaveValidMessageIds()
    {
        var messages = _store.GetMessages(MockNamespaces.MockNamespaceId, "orders", dlq: false).ToList();
        messages.Should().AllSatisfy(m => m.MessageId.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void Store_AllMessages_HaveValidBodies()
    {
        var messages = _store.GetMessages(MockNamespaces.MockNamespaceId, "payments", dlq: false).ToList();
        messages.Should().AllSatisfy(m => m.Body.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void Store_DlqMessages_HaveDeadLetterReason()
    {
        var messages = _store.GetMessages(MockNamespaces.MockNamespaceId, "orders", dlq: true).ToList();
        messages.Should().AllSatisfy(m => m.DeadLetterReason.Should().NotBeNullOrWhiteSpace());
    }

    // ── MockMessageReceiver ───────────────────────────────────────────────────

    [Fact]
    public async Task PeekMessagesAsync_ReturnsRequestedCount()
    {
        var receiver = _sut.GetMessageReceiver();
        var request = new GetMessagesRequest(
            MockNamespaces.MockNamespaceId,
            "orders",
            null,
            false,
            10);

        var result = await receiver.PeekMessagesAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(10);
    }

    [Fact]
    public async Task PeekDeadLetterMessagesAsync_ReturnsDlqMessages()
    {
        var receiver = _sut.GetMessageReceiver();
        var request = new GetMessagesRequest(
            MockNamespaces.MockNamespaceId,
            "orders",
            null,
            true,
            100);

        var result = await receiver.PeekDeadLetterMessagesAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(8);
    }

    // ── MockMessageSender ─────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_Succeeds()
    {
        var sender = _sut.GetMessageSender();
        var request = new SendMessageRequest(
            MockNamespaces.MockNamespaceId,
            "orders",
            "{\"test\":true}",
            "application/json");

        var result = await sender.SendAsync(request);

        result.IsSuccess.Should().BeTrue();
        _store.GetSentMessages().Should().HaveCount(1);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private const string ValidConnectionString =
        "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc123==";

    private static Namespace BuildNamespace(Guid id, string connectionString)
        => Namespace.Create("mock-ns", ValidConnectionString, ownerId: TestConstants.TestOwnerId).Value;
}
