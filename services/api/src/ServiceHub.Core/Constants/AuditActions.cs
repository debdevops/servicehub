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

    /// <summary>A dead letter was purged (unit 6.15).</summary>
    public const string PurgeMessage = "Purge.Message";

    /// <summary>One new message was sent (unit 6.14).</summary>
    public const string MessageSend = "Message.Send";

    /// <summary>A person set up, changed or turned off a cloud's DLQ observer (unit 4.2).</summary>
    public const string DlqObserverConfigure = "DlqObserver.Configure";

    /// <summary>A backup was taken.</summary>
    public const string BackupCreate = "Backup.Create";

    /// <summary>A backup was staged to be restored at the next start, or unstaged.</summary>
    public const string BackupRestore = "Backup.Restore";

    /// <summary>An Auto Replay rule was made. <c>ResourceName</c> is the rule's id and name.</summary>
    public const string RuleCreate = "Rule.Create";

    /// <summary>An Auto Replay rule's name or pace was changed.</summary>
    public const string RuleUpdate = "Rule.Update";

    /// <summary>An Auto Replay rule was switched on or off by a person.</summary>
    public const string RuleToggle = "Rule.Toggle";

    /// <summary>An Auto Replay rule was deleted.</summary>
    public const string RuleDelete = "Rule.Delete";

    /// <summary>Auto Replay rules were generated for the commonest failures.</summary>
    public const string RuleGenerate = "Rule.Generate";

    /// <summary>The action was attempted and failed.</summary>
    public const string Failure = "Failure";
}
