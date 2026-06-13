using ServiceHub.Core.Enums;

namespace ServiceHub.Simulator.Store;

/// <summary>
/// Thread-safe in-memory entity that owns a list of active and dead-lettered messages.
/// Operations are protected by a single <see cref="Lock"/> so concurrent HTTP requests
/// produce consistent results without data races.
/// </summary>
public sealed class SimulatorEntity
{
    private readonly Lock _lock = new();
    private readonly List<SimulatorMessage> _messages = [];
    private readonly List<SimulatorMessage> _dlq = [];
    private long _sequenceCounter = 1000;

    /// <summary>Gets the entity name (queue, topic, or subscription name).</summary>
    public required string Name { get; init; }

    /// <summary>Gets the entity type descriptor (e.g., <c>queue</c>, <c>topic</c>, <c>subscription</c>, <c>snsTopic</c>, <c>pubsubTopic</c>).</summary>
    public required string EntityType { get; init; }

    /// <summary>Gets the cloud provider that owns this entity.</summary>
    public required CloudProviderType Provider { get; init; }

    /// <summary>Gets or sets the dead-letter topic name (GCP only).</summary>
    public string? DeadLetterTopicName { get; set; }

    /// <summary>Gets or sets the maximum delivery attempts before a message is dead-lettered (GCP only).</summary>
    public int MaxDeliveryAttempts { get; set; } = 5;

    /// <summary>Gets or sets the visibility timeout in seconds (AWS only).</summary>
    public int VisibilityTimeoutSeconds { get; set; } = 30;

    /// <summary>Gets or sets the ack deadline in seconds (GCP only).</summary>
    public int AckDeadlineSeconds { get; set; } = 10;

    /// <summary>Gets or sets whether message ordering is enabled (GCP only).</summary>
    public bool MessageOrderingEnabled { get; set; }

    // ── Read ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns up to <paramref name="max"/> active messages without removing them.
    /// </summary>
    public IReadOnlyList<SimulatorMessage> PeekMessages(int max)
    {
        lock (_lock)
            return _messages.Take(max).ToList();
    }

    /// <summary>
    /// Returns up to <paramref name="max"/> dead-lettered messages without removing them.
    /// </summary>
    public IReadOnlyList<SimulatorMessage> PeekDlq(int max)
    {
        lock (_lock)
            return _dlq.Take(max).ToList();
    }

    /// <summary>Returns the current active message count.</summary>
    public long GetMessageCount()
    {
        lock (_lock) return _messages.Count;
    }

    /// <summary>Returns the current dead-letter message count.</summary>
    public long GetDlqCount()
    {
        lock (_lock) return _dlq.Count;
    }

    /// <summary>Atomically increments and returns the next sequence number.</summary>
    public long NextSequenceNumber() => Interlocked.Increment(ref _sequenceCounter);

    // ── Write ─────────────────────────────────────────────────────────────────

    /// <summary>Adds a message to the active queue.</summary>
    public void EnqueueMessage(SimulatorMessage msg)
    {
        ArgumentNullException.ThrowIfNull(msg);
        lock (_lock) _messages.Add(msg);
    }

    /// <summary>
    /// Moves the message identified by <paramref name="sequenceNumber"/> from active to dead-letter.
    /// </summary>
    /// <param name="sequenceNumber">Sequence number of the message to dead-letter.</param>
    /// <param name="reason">The dead-letter reason string.</param>
    /// <param name="description">Optional detailed error description.</param>
    public void MoveToDeadLetter(long sequenceNumber, string reason, string description)
    {
        lock (_lock)
        {
            var idx = _messages.FindIndex(m => m.SequenceNumber == sequenceNumber);
            if (idx < 0) return;

            var original = _messages[idx];
            _messages.RemoveAt(idx);

            var dlqMsg = original with
            {
                IsDeadLettered = true,
                DeadLetterReason = reason,
                DeadLetterErrorDescription = description
            };
            _dlq.Add(dlqMsg);
        }
    }

    /// <summary>
    /// Moves the message identified by <paramref name="sequenceNumber"/> from DLQ back to active.
    /// </summary>
    /// <returns><see langword="true"/> if the message was found and replayed; otherwise <see langword="false"/>.</returns>
    public bool ReplayFromDlq(long sequenceNumber)
    {
        lock (_lock)
        {
            var idx = _dlq.FindIndex(m => m.SequenceNumber == sequenceNumber);
            if (idx < 0) return false;

            var original = _dlq[idx];
            _dlq.RemoveAt(idx);

            var replayed = original with
            {
                IsDeadLettered = false,
                DeadLetterReason = null,
                DeadLetterErrorDescription = null,
                DeliveryCount = 0
            };
            _messages.Add(replayed);
            return true;
        }
    }

    /// <summary>
    /// Permanently removes the message identified by <paramref name="sequenceNumber"/>.
    /// </summary>
    /// <param name="sequenceNumber">Sequence number of the message to purge.</param>
    /// <param name="fromDlq">When <see langword="true"/>, purges from DLQ; otherwise from active queue.</param>
    /// <returns><see langword="true"/> if the message was found and removed; otherwise <see langword="false"/>.</returns>
    public bool Purge(long sequenceNumber, bool fromDlq)
    {
        lock (_lock)
        {
            var list = fromDlq ? _dlq : _messages;
            var idx = list.FindIndex(m => m.SequenceNumber == sequenceNumber);
            if (idx < 0) return false;
            list.RemoveAt(idx);
            return true;
        }
    }

    // ── AWS visibility window ─────────────────────────────────────────────────

    /// <summary>
    /// Sets the visibility window for the specified message (AWS SQS simulation).
    /// </summary>
    /// <param name="sequenceNumber">Sequence number of the target message.</param>
    /// <param name="seconds">Visibility timeout in seconds. 0 = visible immediately.</param>
    public void SetVisibilityWindow(long sequenceNumber, int seconds)
    {
        lock (_lock)
        {
            var idx = _messages.FindIndex(m => m.SequenceNumber == sequenceNumber);
            if (idx < 0) return;

            var until = seconds == 0
                ? (DateTimeOffset?)null
                : DateTimeOffset.UtcNow.AddSeconds(seconds);

            _messages[idx] = _messages[idx] with { VisibilityUntil = until };
        }
    }

    /// <summary>
    /// Returns messages that are currently in the visibility window (AWS SQS in-flight simulation).
    /// </summary>
    public IReadOnlyList<SimulatorMessage> GetInFlightMessages()
    {
        var now = DateTimeOffset.UtcNow;
        lock (_lock)
            return _messages.Where(m => m.VisibilityUntil.HasValue && m.VisibilityUntil.Value > now).ToList();
    }

    /// <summary>
    /// Makes messages whose visibility window has expired visible again.
    /// Called by the background ticker every 5 seconds.
    /// </summary>
    public void ExpireVisibilityWindows()
    {
        var now = DateTimeOffset.UtcNow;
        lock (_lock)
        {
            for (var i = 0; i < _messages.Count; i++)
            {
                var vis = _messages[i].VisibilityUntil;
                if (vis.HasValue && vis.Value <= now)
                    _messages[i] = _messages[i] with { VisibilityUntil = null };
            }
        }
    }

    // ── GCP ack deadline ──────────────────────────────────────────────────────

    /// <summary>
    /// Sets the ack deadline for the specified message (GCP Pub/Sub simulation).
    /// </summary>
    /// <param name="messageId">Message ID of the target message.</param>
    /// <param name="seconds">Ack deadline in seconds from now.</param>
    public void SetAckDeadline(string messageId, int seconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        lock (_lock)
        {
            var idx = _messages.FindIndex(m => m.MessageId == messageId);
            if (idx < 0) return;
            _messages[idx] = _messages[idx] with
            {
                AckDeadline = DateTimeOffset.UtcNow.AddSeconds(seconds)
            };
        }
    }

    /// <summary>
    /// Returns messages whose ack deadline has passed and have not yet been nacked or acknowledged.
    /// </summary>
    public IReadOnlyList<SimulatorMessage> GetExpiredAckDeadlines()
    {
        var now = DateTimeOffset.UtcNow;
        lock (_lock)
            return _messages
                .Where(m => m.AckDeadline.HasValue && m.AckDeadline.Value <= now && !m.IsNacked)
                .ToList();
    }

    /// <summary>
    /// Nacks a message: increments <c>DeliveryAttempt</c> and re-queues it.
    /// If <c>DeliveryAttempt</c> has reached <see cref="MaxDeliveryAttempts"/> and a
    /// <see cref="DeadLetterTopicName"/> is configured, the message is dead-lettered instead.
    /// </summary>
    /// <param name="messageId">Message ID of the message to nack.</param>
    public void NackMessage(string messageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        lock (_lock)
        {
            var idx = _messages.FindIndex(m => m.MessageId == messageId);
            if (idx < 0) return;

            var msg = _messages[idx];
            var newAttempt = msg.DeliveryAttempt + 1;

            if (newAttempt >= MaxDeliveryAttempts && DeadLetterTopicName != null)
            {
                _messages.RemoveAt(idx);
                _dlq.Add(msg with
                {
                    DeliveryAttempt = newAttempt,
                    IsDeadLettered = true,
                    DeadLetterReason = "nack",
                    DeadLetterErrorDescription = $"MaxDeliveryAttempts ({MaxDeliveryAttempts}) exceeded"
                });
            }
            else
            {
                _messages[idx] = msg with
                {
                    DeliveryAttempt = newAttempt,
                    IsNacked = true,
                    AckDeadline = null
                };
            }
        }
    }

    /// <summary>
    /// Enqueues a message directly to the dead-letter queue (used by the seeder and flood injection).
    /// </summary>
    public void EnqueueDlqMessage(SimulatorMessage msg)
    {
        ArgumentNullException.ThrowIfNull(msg);
        lock (_lock) _dlq.Add(msg);
    }

    /// <summary>
    /// Clears all active and DLQ messages (used by the reset operation).
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _messages.Clear();
            _dlq.Clear();
        }
    }
}
