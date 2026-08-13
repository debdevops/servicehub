namespace ServiceHub.Core.Enums;

/// <summary>
/// The kind of provider mutation a <see cref="Entities.RecoveryOperation"/> represents.
/// </summary>
public enum RecoveryOperationKind
{
    /// <summary>The operation replayed one or more messages back to a live entity.</summary>
    Replay = 0,

    /// <summary>The operation permanently deleted one or more messages from the DLQ.</summary>
    Purge = 1
}
