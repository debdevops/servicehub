namespace ServiceHub.Core.Enums;

/// <summary>
/// What an agent needs from the clouds that are connected before it has anything meaningful to do. An agent nobody needs is
/// not run and not listed — a sleeping loop that can only ever do nothing is noise on the Agents screen and a false
/// "stalled" waiting to happen. Decided by capability, never by a cloud's name (rule R4).
/// </summary>
public enum AgentNeeds
{
    /// <summary>Always meaningful, whatever is connected (replaying, verifying, backing up, Auto Replay).</summary>
    None = 0,

    /// <summary>A connected cloud that ServiceHub can watch on its own (<c>SupportsRepeatablePeek</c>) — Azure today.</summary>
    WatchedCloud = 1,

    /// <summary>A connected cloud that can prove a fix held (<c>CanProveDlqAbsence</c>) — Azure today.</summary>
    VerifiableCloud = 2,

    /// <summary>A connected cloud that cannot prove a fix held on its own (<c>!CanProveDlqAbsence</c>) — AWS and Google Cloud today — and so may have a DLQ observer to check.</summary>
    ObserverCloud = 3,
}
