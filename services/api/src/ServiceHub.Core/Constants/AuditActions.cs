namespace ServiceHub.Core.Constants;

/// <summary>
/// The stable names recorded in the audit trail. Like error codes, an action name is a promise:
/// filters, exports and runbooks quote it, so add names — never repurpose one.
/// </summary>
public static class AuditActions
{
    /// <summary>A namespace was connected.</summary>
    public const string NamespaceConnect = "Namespace.Connect";

    /// <summary>A namespace was removed.</summary>
    public const string NamespaceRemove = "Namespace.Remove";

    /// <summary>A dead letter was replayed (or a replay was refused).</summary>
    public const string ReplayMessage = "Replay.Message";

    /// <summary>A person asked ServiceHub to look at a namespace's dead letters now.</summary>
    public const string DeadLettersLook = "DeadLetters.Look";

    /// <summary>A person paused an agent: it will not act (its loop keeps running). <c>ResourceName</c> is the agent id.</summary>
    public const string AgentPause = "Agent.Pause";

    /// <summary>A person resumed a paused agent. <c>ResourceName</c> is the agent id.</summary>
    public const string AgentResume = "Agent.Resume";

    /// <summary>The action completed.</summary>
    public const string Success = "Success";

    /// <summary>The action was attempted and failed.</summary>
    public const string Failure = "Failure";
}
