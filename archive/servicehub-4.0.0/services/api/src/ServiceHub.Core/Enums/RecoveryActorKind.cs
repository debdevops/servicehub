namespace ServiceHub.Core.Enums;

/// <summary>
/// The category of actor behind a recovery action, as resolved server-side — never
/// caller-supplied. See <see cref="Models.RecoveryActor"/>.
/// </summary>
public enum RecoveryActorKind
{
    /// <summary>A human operator authenticated via the SPA/claims principal.</summary>
    User = 0,

    /// <summary>A scoped API key.</summary>
    ApiKey = 1,

    /// <summary>An unattended automation path (AutoReplay rule, bulk/signature job worker).</summary>
    Automation = 2,

    /// <summary>ServiceHub itself, acting without an operator (e.g. startup reconciliation).</summary>
    System = 3
}
