using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.LiveTail;

/// <summary>
/// Default <see cref="ILiveTailSession"/> implementation. Polls via the same
/// provider-neutral <see cref="IMessageOperationsService.PeekMessagesAsync"/> /
/// <see cref="IMessageOperationsService.PeekDeadLetterMessagesAsync"/> facade every other
/// read-only message view already uses — no provider-specific code here.
/// </summary>
public sealed class LiveTailSession : ILiveTailSession
{
    private const int PollBatchSize = 25;

    // Bounds memory for a long-running session against a busy entity — once the tracked
    // set is full, the oldest-seen key is evicted to make room. A false "new" re-emit of a
    // very old message after eviction is an acceptable trade-off for a live, best-effort
    // debugging feed (not a durable record).
    private const int MaxTrackedKeys = 1_000;

    private readonly IMessageOperationsService _messageOperationsService;
    private readonly Guid _namespaceId;
    private readonly string _entityName;
    private readonly string? _subscriptionName;
    private readonly bool _fromDeadLetter;

    private readonly HashSet<string> _seenKeys = new(StringComparer.Ordinal);
    private readonly Queue<string> _seenOrder = new();
    private bool _firstPoll = true;

    public LiveTailSession(
        IMessageOperationsService messageOperationsService,
        Guid namespaceId,
        string entityName,
        string? subscriptionName,
        bool fromDeadLetter)
    {
        _messageOperationsService = messageOperationsService ?? throw new ArgumentNullException(nameof(messageOperationsService));
        ArgumentException.ThrowIfNullOrEmpty(entityName);
        _namespaceId = namespaceId;
        _entityName = entityName;
        _subscriptionName = subscriptionName;
        _fromDeadLetter = fromDeadLetter;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Message>>> PollNextAsync(CancellationToken cancellationToken = default)
    {
        var request = new GetMessagesRequest(
            NamespaceId: _namespaceId,
            EntityName: _entityName,
            SubscriptionName: _subscriptionName,
            FromDeadLetter: _fromDeadLetter,
            MaxMessages: PollBatchSize);

        var result = _fromDeadLetter
            ? await _messageOperationsService.PeekDeadLetterMessagesAsync(request, cancellationToken)
            : await _messageOperationsService.PeekMessagesAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            return Result<IReadOnlyList<Message>>.Failure(result.Error);
        }

        var newMessages = new List<Message>();

        foreach (var message in result.Value)
        {
            // MessageId is the one identity all three providers report consistently on peek;
            // sequence numbers are stable on Azure but rotate per-delivery on AWS/GCP receipt
            // handles, so MessageId is the safe universal dedup key here.
            var key = string.IsNullOrEmpty(message.MessageId)
                ? $"seq-{message.SequenceNumber}"
                : message.MessageId;

            if (!_seenKeys.Add(key))
            {
                continue;
            }

            _seenOrder.Enqueue(key);
            if (_seenOrder.Count > MaxTrackedKeys)
            {
                _seenKeys.Remove(_seenOrder.Dequeue());
            }

            // Don't emit the entity's existing backlog on the very first poll — a session
            // starts watching from "now", like `tail -f`, not by dumping what's already there.
            if (!_firstPoll)
            {
                newMessages.Add(message);
            }
        }

        _firstPoll = false;

        return Result<IReadOnlyList<Message>>.Success(newMessages);
    }
}
