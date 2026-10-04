using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Providers.Aws.DlqObserver;

/// <summary>
/// Whether a replayed message came back, answered by listing the whole dead-letter queue once the watch window has ended
/// (ADR-0018). Nothing has to be deployed in the customer's account, and nothing runs on a timer: the queue is scanned only
/// when a replay's window closes.
/// </summary>
public sealed class SqsWholeQueueCheck : IDeadLetterReturnCheck, IDeadLetterWholeViewProbe
{
    /// <summary>The longest time-to-dead-letter this check will wait out. A queue set up to take longer cannot be answered.</summary>
    internal static readonly TimeSpan LongestFloor = TimeSpan.FromHours(24);

    private readonly IAwsClientFactory _clients;
    private readonly SqsWholeQueueScanner _scanner;
    private readonly ILogger<SqsWholeQueueCheck> _logger;
    private readonly TimeProvider _time;

    /// <summary>Creates the check.</summary>
    public SqsWholeQueueCheck(IAwsClientFactory clients, SqsWholeQueueScanner scanner, ILogger<SqsWholeQueueCheck> logger, TimeProvider? time = null)
    {
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public CloudProviderType Provider => CloudProviderType.Aws;

    /// <inheritdoc />
    public bool NeedsObserverReference => false;

    /// <inheritdoc />
    public string? ValidateObserverReference(string? observerReference) => null;

    /// <inheritdoc />
    public async Task<DeadLetterViewHealth> CheckHealthAsync(Namespace ns, DlqObserverAttestation attestation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ns);
        try
        {
            // Reading only: can ServiceHub still talk to this account's queues at all? No queue is scanned here.
            await _clients.GetSqsClient(ns).ListQueuesAsync(new ListQueuesRequest { MaxResults = 1 }, cancellationToken).ConfigureAwait(false);
            return new DeadLetterViewHealth(true);
        }
        catch (AmazonSQSException ex)
        {
            _logger.LogWarning(ex, "Could not list queues for namespace {NamespaceId}", ns.Id);
            return new DeadLetterViewHealth(false, "CANNOT_LIST_QUEUES");
        }
    }

    /// <inheritdoc />
    public async Task<DeadLetterReturnVerdict> CheckAsync(
        Namespace ns, DlqObserverAttestation attestation, RecoveryLedgerEntry entry, DateTimeOffset? liveSince, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ns);
        ArgumentNullException.ThrowIfNull(entry);

        var marker = entry.MarkerApplied ? entry.RecoveryMarker : null;
        var replayedId = entry.ReplayedProviderMessageId;
        if (string.IsNullOrEmpty(marker) && string.IsNullOrEmpty(replayedId))
        {
            return DeadLetterReturnVerdict.CannotTell("NO_WAY_TO_RECOGNISE_IT"); // nothing to look for: never "not found, so fine"
        }

        var queueName = QueueNameOf(entry);
        if (queueName is null)
        {
            return DeadLetterReturnVerdict.CannotTell("QUEUE_UNKNOWN");
        }

        var sqs = _clients.GetSqsClient(ns);
        try
        {
            var sourceUrl = (await sqs.GetQueueUrlAsync(new GetQueueUrlRequest { QueueName = queueName }, cancellationToken).ConfigureAwait(false)).QueueUrl;
            var source = await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest { QueueUrl = sourceUrl, AttributeNames = ["VisibilityTimeout", "RedrivePolicy"] }, cancellationToken).ConfigureAwait(false);
            var attributes = source.Attributes ?? [];
            if (!attributes.TryGetValue("RedrivePolicy", out var redrive) || !TryReadRedrive(redrive, out var dlqName, out var maxReceives))
            {
                return DeadLetterReturnVerdict.CannotTell("NO_DEAD_LETTER_QUEUE");
            }

            // "Not in the dead-letter queue" only means "fixed" once the message has had time to fail and be moved there.
            var visibility = attributes.TryGetValue("VisibilityTimeout", out var vt) && int.TryParse(vt, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var seconds) ? seconds : 30;
            var floor = TimeSpan.FromSeconds(((long)visibility * maxReceives) + 60);
            if (floor > LongestFloor)
            {
                return DeadLetterReturnVerdict.CannotTell("WINDOW_TOO_SHORT");
            }

            if (_time.GetUtcNow() < entry.BegunAt + floor)
            {
                return DeadLetterReturnVerdict.TooEarly;
            }

            var dlqUrl = (await sqs.GetQueueUrlAsync(new GetQueueUrlRequest { QueueName = dlqName }, cancellationToken).ConfigureAwait(false)).QueueUrl;
            var scan = await _scanner.ScanAsync(sqs, dlqUrl, cancellationToken).ConfigureAwait(false);
            if (!scan.Complete)
            {
                return DeadLetterReturnVerdict.CannotTell(scan.Reason ?? "SCAN_INCOMPLETE");
            }

            var back = scan.Messages.Any(m =>
                (!string.IsNullOrEmpty(marker) && string.Equals(m.RecoveryMarker, marker, StringComparison.Ordinal))
                || (!string.IsNullOrEmpty(replayedId) && string.Equals(m.MessageId, replayedId, StringComparison.Ordinal)));
            return back ? DeadLetterReturnVerdict.Returned : DeadLetterReturnVerdict.NotReturned;
        }
        catch (QueueDoesNotExistException)
        {
            return DeadLetterReturnVerdict.CannotTell("QUEUE_NOT_FOUND");
        }
        catch (AmazonSQSException ex)
        {
            _logger.LogWarning(ex, "The dead-letter queue of {Queue} could not be read for entry {EntryId}", ServiceHub.Core.Security.LogRedactor.SanitiseForLog(queueName), entry.Id);
            return DeadLetterReturnVerdict.CannotTell("QUEUE_UNREADABLE");
        }
    }

    /// <inheritdoc />
    public async Task<DeadLetterViewProbe> ProbeAsync(Namespace ns, string entityName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ns);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        var sqs = _clients.GetSqsClient(ns);
        try
        {
            var sourceUrl = (await sqs.GetQueueUrlAsync(new GetQueueUrlRequest { QueueName = entityName }, cancellationToken).ConfigureAwait(false)).QueueUrl;
            var source = await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest { QueueUrl = sourceUrl, AttributeNames = ["RedrivePolicy"] }, cancellationToken).ConfigureAwait(false);
            if (source.Attributes is null || !source.Attributes.TryGetValue("RedrivePolicy", out var redrive) || !TryReadRedrive(redrive, out var dlqName, out _))
            {
                return new DeadLetterViewProbe(false, "NO_DEAD_LETTER_QUEUE", 0);
            }

            var dlqUrl = (await sqs.GetQueueUrlAsync(new GetQueueUrlRequest { QueueName = dlqName }, cancellationToken).ConfigureAwait(false)).QueueUrl;
            var scan = await _scanner.ScanAsync(sqs, dlqUrl, cancellationToken).ConfigureAwait(false);
            return new DeadLetterViewProbe(scan.Complete, scan.Reason, scan.Messages.Count);
        }
        catch (QueueDoesNotExistException)
        {
            return new DeadLetterViewProbe(false, "QUEUE_NOT_FOUND", 0);
        }
        catch (AmazonSQSException)
        {
            return new DeadLetterViewProbe(false, "QUEUE_UNREADABLE", 0);
        }
    }

    /// <summary>The queue a replay was made on. An SNS subscription is stored by its path; the queue is its last part.</summary>
    internal static string? QueueNameOf(RecoveryLedgerEntry entry)
    {
        var name = entry.EntityNameSnapshot;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var slash = name.LastIndexOf('/');
        return slash >= 0 ? name[(slash + 1)..] : name;
    }

    /// <summary>Reads <c>{"deadLetterTargetArn":"arn:aws:sqs:region:account:name","maxReceiveCount":N}</c>.</summary>
    internal static bool TryReadRedrive(string? json, out string dlqName, out int maxReceives)
    {
        dlqName = string.Empty;
        maxReceives = 0;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("deadLetterTargetArn", out var arn) || arn.GetString() is not { Length: > 0 } text)
            {
                return false;
            }

            dlqName = text[(text.LastIndexOf(':') + 1)..];
            // AWS writes the count as a number in some places and as text in others.
            if (doc.RootElement.TryGetProperty("maxReceiveCount", out var count))
            {
                maxReceives = count.ValueKind == JsonValueKind.Number ? count.GetInt32() : int.TryParse(count.GetString(), out var parsed) ? parsed : 0;
            }

            return dlqName.Length > 0 && maxReceives > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
