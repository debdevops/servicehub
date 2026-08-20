using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
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

    // Bounds how many extra Peek round-trips one PollNextAsync call spends catching up past
    // a pre-existing backlog (Azure only — see _supportsSequentialCatchUp). A backlog larger
    // than PollBatchSize * MaxCatchUpBatchesPerPoll just takes a few extra 3s poll cycles to
    // skip past instead of one long call; it's never emitted either way (see _firstPoll).
    private const int MaxCatchUpBatchesPerPoll = 8;

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

    // Azure's SequenceNumber is a stable, entity-wide monotonic counter, so it can be used as
    // a "resume from here" cursor across the stateless receiver Peek creates and disposes on
    // every call (Service Bus's own client-side "next peek position" tracking lives on that
    // short-lived receiver and is lost with it). AWS never reaches this class (Live Tail is
    // gated off entirely for providers without SupportsRepeatablePeek). GCP's SequenceNumber
    // rotates per redelivery, so a cursor built from it would be meaningless — GCP keeps
    // relying on its existing MessageId-based dedup below, unchanged from before this fix.
    private readonly bool _supportsSequentialCatchUp;

    private readonly HashSet<string> _seenKeys = new(StringComparer.Ordinal);
    private readonly Queue<string> _seenOrder = new();
    private long? _nextSequenceNumber;
    private bool _firstPoll = true;

    public LiveTailSession(
        IMessageOperationsService messageOperationsService,
        Guid namespaceId,
        string entityName,
        string? subscriptionName,
        bool fromDeadLetter,
        CloudProviderType provider)
    {
        _messageOperationsService = messageOperationsService ?? throw new ArgumentNullException(nameof(messageOperationsService));
        ArgumentException.ThrowIfNullOrEmpty(entityName);
        _namespaceId = namespaceId;
        _entityName = entityName;
        _subscriptionName = subscriptionName;
        _fromDeadLetter = fromDeadLetter;
        _supportsSequentialCatchUp = provider == CloudProviderType.Azure;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Message>>> PollNextAsync(CancellationToken cancellationToken = default)
    {
        var newMessages = new List<Message>();
        var maxIterations = _supportsSequentialCatchUp ? MaxCatchUpBatchesPerPoll : 1;

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var request = new GetMessagesRequest(
                NamespaceId: _namespaceId,
                EntityName: _entityName,
                SubscriptionName: _subscriptionName,
                FromDeadLetter: _fromDeadLetter,
                MaxMessages: PollBatchSize,
                FromSequenceNumber: _supportsSequentialCatchUp ? _nextSequenceNumber : null);

            var result = _fromDeadLetter
                ? await _messageOperationsService.PeekDeadLetterMessagesAsync(request, cancellationToken)
                : await _messageOperationsService.PeekMessagesAsync(request, cancellationToken);

            if (result.IsFailure)
            {
                return Result<IReadOnlyList<Message>>.Failure(result.Error);
            }

            foreach (var message in result.Value)
            {
                if (_supportsSequentialCatchUp && message.SequenceNumber >= (_nextSequenceNumber ?? 0))
                {
                    _nextSequenceNumber = message.SequenceNumber + 1;
                }

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

            // Caught up to the current tail (a short/empty batch), or this provider has no
            // usable cursor and a single snapshot is all one poll can offer — either way,
            // another iteration this cycle can't make further progress.
            if (result.Value.Count < PollBatchSize || !_supportsSequentialCatchUp)
            {
                _firstPoll = false;
                break;
            }
        }

        return Result<IReadOnlyList<Message>>.Success(newMessages);
    }
}
