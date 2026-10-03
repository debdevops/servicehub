namespace ServiceHub.Core.Enums;

/// <summary>
/// What kind of work an agent does. The Agents screen groups by this, and it leads with
/// <see cref="Act"/> so the first thing the screen says is how few agents can change anything.
/// </summary>
public enum AgentKind
{
    /// <summary>Observes the world and records what it sees. Changes nothing.</summary>
    Watch = 0,

    /// <summary>Evaluates recorded evidence and reaches a verdict. Changes no cloud resource.</summary>
    Decide = 1,

    /// <summary>Performs an action against a cloud provider. The only kind that can change anything.</summary>
    Act = 2,

    /// <summary>Keeps ServiceHub's own house in order — retention, backups, expiry.</summary>
    Maintain = 3
}
