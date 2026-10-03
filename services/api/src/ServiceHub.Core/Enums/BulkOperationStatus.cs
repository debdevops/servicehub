namespace ServiceHub.Core.Enums;

/// <summary>Where a bulk replay job is. A job is born <see cref="Previewed"/>: nothing runs until it is started from that preview.</summary>
public enum BulkOperationStatus
{
    /// <summary>The preview is stored; nothing has been sent.</summary>
    Previewed = 0,

    /// <summary>Started; the agent is working through it.</summary>
    Running = 1,

    /// <summary>Every message that could be replayed has been handled.</summary>
    Completed = 2,

    /// <summary>A person stopped it; unsent messages were left exactly as they were.</summary>
    Cancelled = 3,

    /// <summary>It stopped itself because too many messages in a row were not accepted.</summary>
    Stopped = 4,

    /// <summary>A preview nobody started before it expired.</summary>
    Expired = 5,
}

/// <summary>What happened to one message of a bulk job.</summary>
public enum BulkItemState
{
    /// <summary>The gate would not allow it; it is not part of the run. Its reason and remedy are kept.</summary>
    HeldBack = 0,

    /// <summary>Will be replayed.</summary>
    Queued = 1,

    /// <summary>The call to the cloud is in flight. Found in this state after a crash, it becomes <see cref="Unknown"/>.</summary>
    Sending = 2,

    /// <summary>The cloud accepted it.</summary>
    Sent = 3,

    /// <summary>The cloud refused it, or the gate refused it at the moment of sending.</summary>
    Failed = 4,

    /// <summary>ServiceHub lost contact with the cloud before it could tell. Check before trying again.</summary>
    Unknown = 5,

    /// <summary>Never attempted: the job was stopped first.</summary>
    Skipped = 6,
}
