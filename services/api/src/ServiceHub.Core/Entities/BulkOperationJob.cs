using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Entities;

/// <summary>
/// A bulk replay: many dead letters, one stored preview (unit 3.2). The job is created by the preview and the run can
/// only start from it — there is no way to execute a selection that was not previewed, because the selection is the
/// preview's items and nothing else.
/// </summary>
public sealed class BulkOperationJob
{
    /// <summary>The preview id, and the job id from then on.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Owner scope.</summary>
    public required string OwnerId { get; init; }

    /// <summary>Who asked. The same actor is on every ledger entry the run writes.</summary>
    public required string ActorIdentity { get; init; }

    /// <summary>The actor's kind.</summary>
    public required RecoveryActorKind ActorKind { get; init; }

    /// <summary>Where it is.</summary>
    public BulkOperationStatus Status { get; set; } = BulkOperationStatus.Previewed;

    /// <summary>When the preview was made. A preview expires, because the world it described moves.</summary>
    public required DateTimeOffset PreviewedAt { get; init; }

    /// <summary>When the run started.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>When it ended.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Messages sent per second, fixed at preview so what was shown is what runs.</summary>
    public required double PerSecond { get; init; }

    /// <summary>The run stops itself after this many messages in a row are not accepted.</summary>
    public required int StopAfterConsecutiveFailures { get; init; }

    /// <summary>True when only the first message is to be sent ("replay a sample of 1 first").</summary>
    public bool SampleOnly { get; set; }

    /// <summary>A person asked to stop. The agent honours it before the next message.</summary>
    public bool CancelRequested { get; set; }

    /// <summary>Why it ended, in words, when it did not simply finish.</summary>
    public string? EndedReason { get; set; }

    /// <summary>The messages, held back or queued.</summary>
    public List<BulkOperationItem> Items { get; init; } = [];

    /// <summary>What the run does to each message: replay (the default) or purge (unit 6.15).</summary>
    public RecoveryOperationKind Kind { get; init; } = RecoveryOperationKind.Replay;

    /// <summary>Why — required for a purge, kept on every message's ledger operation.</summary>
    public string? Reason { get; init; }
}

/// <summary>One dead letter inside a bulk job.</summary>
public sealed class BulkOperationItem
{
    /// <summary>Primary key.</summary>
    public long Id { get; private set; }

    /// <summary>The job.</summary>
    public Guid JobId { get; init; }

    /// <summary>The dead letter (<c>DlqMessages.Id</c>).</summary>
    public required long DlqMessageId { get; init; }

    /// <summary>Its namespace.</summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>The queue it sits in, for the preview's grouping.</summary>
    public required string EntityName { get; init; }

    /// <summary>The reason the cloud recorded — the group the preview shows it under.</summary>
    public string? DeadLetterReason { get; init; }

    /// <summary>State.</summary>
    public BulkItemState State { get; set; }

    /// <summary>The gate's or the cloud's reason code (held back, failed).</summary>
    public string? ReasonCode { get; set; }

    /// <summary>What to do about it, in words, for a held-back message.</summary>
    public string? Remedy { get; set; }

    /// <summary>The ledger entry the replay wrote — one per message actually attempted.</summary>
    public Guid? RecoveryEntryId { get; set; }

    /// <summary>Order within the run.</summary>
    public int Position { get; init; }
}
