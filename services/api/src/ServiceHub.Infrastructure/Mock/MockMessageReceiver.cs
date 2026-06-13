using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.Mock;

/// <summary>
/// In-memory <see cref="IMessageReceiver"/> used when the mock provider is active.
/// Returns seeded data without contacting any cloud messaging service.
/// </summary>
internal sealed class MockMessageReceiver : IMessageReceiver
{
    private readonly MockMessageStore _store;

    public MockMessageReceiver(MockMessageStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<Result<IReadOnlyList<Message>>> PeekMessagesAsync(
        GetMessagesRequest request,
        CancellationToken cancellationToken = default)
    {
        var messages = _store.GetMessages(request.NamespaceId, request.EntityName, dlq: false)
            .Take(request.MaxMessages)
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<Message>>.Success(messages));
    }

    public Task<Result<IReadOnlyList<Message>>> PeekDeadLetterMessagesAsync(
        GetMessagesRequest request,
        CancellationToken cancellationToken = default)
    {
        var messages = _store.GetMessages(request.NamespaceId, request.EntityName, dlq: true)
            .Take(request.MaxMessages)
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<Message>>.Success(messages));
    }

    public Task<Result<long>> GetMessageCountAsync(
        Guid namespaceId,
        string entityName,
        string? subscriptionName = null,
        CancellationToken cancellationToken = default)
    {
        var count = (long)_store.GetMessages(namespaceId, entityName, dlq: false).Count();
        return Task.FromResult(Result<long>.Success(count));
    }

    public Task<Result<int>> DeadLetterMessagesAsync(
        DeadLetterRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Result<int>.Success(0));

    public Task<Result> ReplayMessageAsync(
        Guid namespaceId,
        string entityName,
        string? subscriptionName,
        long sequenceNumber,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Success());

    public Task<Result> PurgeMessageAsync(
        Guid namespaceId,
        string entityName,
        string? subscriptionName,
        long sequenceNumber,
        bool fromDeadLetter,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Success());

    public Task<Result<IReadOnlyList<Message>>> GetScheduledMessagesAsync(
        Guid namespaceId,
        string entityName,
        string? subscriptionName,
        int maxMessages,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Message> empty = [];
        return Task.FromResult(Result<IReadOnlyList<Message>>.Success(empty));
    }
}
