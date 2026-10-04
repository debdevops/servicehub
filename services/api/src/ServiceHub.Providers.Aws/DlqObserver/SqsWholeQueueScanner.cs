using System.Collections.Concurrent;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace ServiceHub.Providers.Aws.DlqObserver;

/// <summary>One message seen in a whole-queue scan: enough to recognise a replayed message, and nothing of its contents.</summary>
/// <param name="MessageId">The message's own id — unchanged when SQS moves a message to a dead-letter queue.</param>
/// <param name="RecoveryMarker">The <c>x-servicehub-recovery-id</c> attribute, when the message carries one.</param>
public sealed record ScannedMessage(string MessageId, string? RecoveryMarker);

/// <summary>What a whole-queue scan found.</summary>
/// <param name="Complete">True only when the scan proved it saw every message in the queue.</param>
/// <param name="Reason">Why it is not complete — a short upper-case code.</param>
/// <param name="Messages">What was seen. Meaningful as "everything" only when <paramref name="Complete"/> is true.</param>
public sealed record WholeQueueScan(bool Complete, string? Reason, IReadOnlyList<ScannedMessage> Messages)
{
    /// <summary>A scan that could not prove it saw everything.</summary>
    public static WholeQueueScan Incomplete(string reason) => new(false, reason, []);
}

/// <summary>
/// Lists a dead-letter queue to the END, and proves it got there (ADR-0018): take every message under one lock, check that
/// what was taken is everything, and give them all back. Nothing is deleted and nothing is changed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only ever for a dead-letter queue.</b> Receiving a message raises its receive count. On a working queue that count
/// leads to dead-lettering; on a dead-letter queue with no redrive policy of its own it leads to nothing — which is checked
/// before anything is received.
/// </para>
/// <para>
/// <b>"Complete" is a fact or it is not claimed.</b> A scan is complete only if several long polls in a row came back
/// empty AND the queue's own counts then agree: nothing visible, and nothing in flight except what this scan holds. A
/// FIFO queue, a second reader, too many messages or too little time all make the scan <i>incomplete</i> — never a guess.
/// SQS has been seen to leave a present message out of a receive (see <c>AwsMessageReceiver</c>), which is why the
/// counts are part of the proof and not a hint.
/// </para>
/// </remarks>
public sealed class SqsWholeQueueScanner
{
    /// <summary>The same ceiling the Azure scan has. A queue holding more is reported incomplete.</summary>
    public const int MaxMessages = 5000;

    internal const int LockSeconds = 300;
    internal const int WaitSeconds = 2;
    internal const int EmptyPollsToStop = 3;
    internal const int CountChecks = 3;
    internal static readonly TimeSpan Budget = TimeSpan.FromSeconds(240);
    internal static readonly TimeSpan CountCheckGap = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

    private const int BatchSize = 10;
    private const string MarkerAttribute = "x-servicehub-recovery-id";

    // One scan of a queue at a time in this process: two would hide messages from each other and both report incomplete.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, WholeQueueScan Scan)> _recent = new();
    private readonly TimeProvider _time;
    private readonly Func<TimeSpan, CancellationToken, Task> _pause;

    /// <summary>Creates the scanner.</summary>
    public SqsWholeQueueScanner(TimeProvider? time = null, Func<TimeSpan, CancellationToken, Task>? pause = null)
    {
        _time = time ?? TimeProvider.System;
        _pause = pause ?? Task.Delay;
    }

    /// <summary>Scans the dead-letter queue at <paramref name="dlqUrl"/>. A complete result is reused for a minute, so many replays on one queue share one scan.</summary>
    public async Task<WholeQueueScan> ScanAsync(IAmazonSQS sqs, string dlqUrl, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sqs);
        ArgumentException.ThrowIfNullOrWhiteSpace(dlqUrl);

        var gate = Gates.GetOrAdd(dlqUrl, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_recent.TryGetValue(dlqUrl, out var cached) && _time.GetUtcNow() - cached.At < CacheFor)
            {
                return cached.Scan;
            }

            var scan = await ScanOnceAsync(sqs, dlqUrl, ct).ConfigureAwait(false);
            if (scan.Complete)
            {
                _recent[dlqUrl] = (_time.GetUtcNow(), scan); // an incomplete scan is never remembered: the next one may succeed
            }

            return scan;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<WholeQueueScan> ScanOnceAsync(IAmazonSQS sqs, string dlqUrl, CancellationToken ct)
    {
        var before = await CountsAsync(sqs, dlqUrl, withShape: true, ct).ConfigureAwait(false);
        if (before.Fifo)
        {
            return WholeQueueScan.Incomplete("FIFO_QUEUE"); // a FIFO queue hands out one group at a time: it cannot be listed to the end
        }

        if (before.HasRedrivePolicy)
        {
            return WholeQueueScan.Incomplete("DLQ_HAS_REDRIVE"); // here a receive count is real, so this queue is never scanned
        }

        if (before.Visible + before.NotVisible > MaxMessages)
        {
            return WholeQueueScan.Incomplete("TOO_MANY");
        }

        if (before.NotVisible > 0)
        {
            return WholeQueueScan.Incomplete("OTHER_READER"); // someone else is holding messages this scan could not see
        }

        var held = new Dictionary<string, Message>(StringComparer.Ordinal);
        var started = _time.GetUtcNow();
        var reason = (string?)null;
        try
        {
            var empty = 0;
            while (empty < EmptyPollsToStop)
            {
                if (_time.GetUtcNow() - started > Budget)
                {
                    reason = "TIME_BUDGET";
                    break;
                }

                var response = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = dlqUrl,
                    MaxNumberOfMessages = BatchSize,
                    VisibilityTimeout = LockSeconds,
                    WaitTimeSeconds = WaitSeconds, // a long poll asks every server, not a sample of them
                    MessageAttributeNames = [MarkerAttribute],
                }, ct).ConfigureAwait(false);

                var messages = response.Messages ?? [];
                if (messages.Count == 0)
                {
                    empty++;
                    continue;
                }

                empty = 0;
                foreach (var message in messages)
                {
                    held[message.MessageId] = message; // at-least-once delivery can repeat one: the latest handle is the valid one
                }

                if (held.Count > MaxMessages)
                {
                    reason = "TOO_MANY";
                    break;
                }
            }

            if (reason is null && !await CountsAgreeAsync(sqs, dlqUrl, held.Count, ct).ConfigureAwait(false))
            {
                reason = "COUNT_MISMATCH";
            }
        }
        finally
        {
            // Nothing is consumed: everything taken is made visible again, even if the caller gave up. If this fails the
            // locks run out by themselves.
            foreach (var chunk in held.Values.Chunk(BatchSize))
            {
                try
                {
                    await sqs.ChangeMessageVisibilityBatchAsync(new ChangeMessageVisibilityBatchRequest
                    {
                        QueueUrl = dlqUrl,
                        Entries = chunk.Select((m, i) => new ChangeMessageVisibilityBatchRequestEntry { Id = i.ToString(System.Globalization.CultureInfo.InvariantCulture), ReceiptHandle = m.ReceiptHandle, VisibilityTimeout = 0 }).ToList(),
                    }, CancellationToken.None).ConfigureAwait(false);
                }
                catch (AmazonSQSException)
                {
                    // They reappear after LockSeconds.
                }
            }
        }

        if (reason is not null)
        {
            return WholeQueueScan.Incomplete(reason);
        }

        return new WholeQueueScan(true, null, held.Values.Select(m => new ScannedMessage(
            m.MessageId,
            m.MessageAttributes is not null && m.MessageAttributes.TryGetValue(MarkerAttribute, out var marker) ? marker.StringValue : null)).ToList());
    }

    /// <summary>The queue's own counts must say: nothing left visible, and nothing in flight but what this scan holds.</summary>
    private async Task<bool> CountsAgreeAsync(IAmazonSQS sqs, string dlqUrl, int holding, CancellationToken ct)
    {
        for (var attempt = 0; attempt < CountChecks; attempt++)
        {
            if (attempt > 0)
            {
                await _pause(CountCheckGap, ct).ConfigureAwait(false); // the counts are approximate and can lag a moment
            }

            var now = await CountsAsync(sqs, dlqUrl, withShape: false, ct).ConfigureAwait(false);
            if (now.Visible == 0 && now.NotVisible == holding)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<(int Visible, int NotVisible, bool Fifo, bool HasRedrivePolicy)> CountsAsync(IAmazonSQS sqs, string dlqUrl, bool withShape, CancellationToken ct)
    {
        var names = new List<string> { "ApproximateNumberOfMessages", "ApproximateNumberOfMessagesNotVisible" };
        if (withShape)
        {
            names.AddRange(["FifoQueue", "RedrivePolicy"]);
        }

        var response = await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest { QueueUrl = dlqUrl, AttributeNames = names }, ct).ConfigureAwait(false);
        var attributes = response.Attributes ?? [];
        int Number(string name) => attributes.TryGetValue(name, out var text) && int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
        return (
            Number("ApproximateNumberOfMessages"),
            Number("ApproximateNumberOfMessagesNotVisible"),
            attributes.TryGetValue("FifoQueue", out var fifo) && string.Equals(fifo, "true", StringComparison.OrdinalIgnoreCase),
            attributes.TryGetValue("RedrivePolicy", out var redrive) && !string.IsNullOrWhiteSpace(redrive));
    }
}
