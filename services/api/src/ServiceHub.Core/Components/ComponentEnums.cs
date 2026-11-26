namespace ServiceHub.Core.Components;

/// <summary>
/// The cloud a component belongs to. A grouping for the screen only: nothing may branch on it to decide behaviour (R4).
/// It is separate from <see cref="Enums.CloudProviderType"/> because one cloud can have several adapters
/// (Azure: Service Bus today, Storage Queues next), and the stored provider value identifies the adapter, not the cloud.
/// </summary>
public enum CloudKind
{
    Azure = 0,
    Aws = 1,
    Gcp = 2,
}

/// <summary>How ServiceHub finds that something is stuck.</summary>
public enum DetectionKind
{
    /// <summary>Not found at all: the component is listed only to say it is not covered.</summary>
    None = 0,

    /// <summary>Reads a place that holds the failed message (a dead-letter queue, a poison queue, a dead-letter topic).</summary>
    Store = 1,

    /// <summary>Reads a history of failed runs.</summary>
    Runs = 2,

    /// <summary>A metric or a state only. There is nothing to read back.</summary>
    Signal = 3,

    /// <summary>
    /// Reads failed records a service keeps in a place that is not a message queue (dead-letter blobs, undelivered events, failure
    /// records that point at the original). Read on demand through an <see cref="IComponentSource"/>, like <see cref="Runs"/>.
    /// </summary>
    Records = 4,
}

/// <summary>Is the failed work kept anywhere ServiceHub can read?</summary>
public enum CopyKind
{
    /// <summary>Nothing is kept.</summary>
    None = 0,

    /// <summary>Only a reference to the original, which can expire.</summary>
    Pointer = 1,

    /// <summary>The failed message itself is held.</summary>
    Held = 2,
}

/// <summary>The safe way to put the work back.</summary>
public enum RecoveryPathKind
{
    /// <summary>No safe path is known. A person decides.</summary>
    None = 0,

    /// <summary>ServiceHub replays one message to a proven target.</summary>
    Adapter = 1,

    /// <summary>The cloud's own redrive. Approval only.</summary>
    Native = 2,

    /// <summary>Re-run a failed run. Approval only.</summary>
    Resubmit = 3,
}

/// <summary>
/// The highest thing ServiceHub can observe about one replayed message (plan §3a). These are separate observations, not a
/// ladder: <see cref="NotReturned"/> does not prove <see cref="Delivered"/>.
/// </summary>
public enum ProofLevel
{
    None = 0,

    /// <summary>E1: the cloud accepted the send.</summary>
    Accepted = 1,

    /// <summary>E2: seen at the destination. Nothing establishes this today.</summary>
    Delivered = 2,

    /// <summary>E3: did not come back to the dead-letter store within the window, across an uncapped scan.</summary>
    NotReturned = 3,

    /// <summary>E4: the consumer handled it. Nothing establishes this today.</summary>
    Processed = 4,
}

/// <summary>What the coverage table says about a row. A row is <see cref="Supported"/> only after a recorded run on a real cloud.</summary>
public enum SupportStatus
{
    Supported = 0,
    BeingValidated = 1,
    OnRequest = 2,
    NotSupported = 3,
}

/// <summary>
/// The recoverability class, always derived from <see cref="RecoverabilityFacts"/> and never stored or set by hand
/// (PLAN-recoverability-boundary §3).
/// </summary>
public enum RecoverabilityClass
{
    /// <summary>Not yet classified by a recorded live run. Treated as detect-only: no replay, a person decides.</summary>
    R0Unknown = 0,

    /// <summary>Replayable and provable: may earn autonomy.</summary>
    R1ReplayableProvable = 1,

    /// <summary>Replayable, not provable: replay on approval, result Unverified, never earns autonomy.</summary>
    R2ReplayableUnproven = 2,

    /// <summary>The cloud redrives or re-runs. Approval only, always Unverified.</summary>
    R3CloudRedriveOnly = 3,

    /// <summary>Only a pointer to the original is kept; recovery is a race against its expiry.</summary>
    R4PointerOnly = 4,

    /// <summary>Detected by a signal, nothing to put back. Hand to a person.</summary>
    R5DetectOnly = 5,

    /// <summary>Nothing is kept and nothing signals it. Said plainly, handed to a person.</summary>
    R6NotRecoverable = 6,
}
