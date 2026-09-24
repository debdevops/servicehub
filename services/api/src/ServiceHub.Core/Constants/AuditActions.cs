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

    /// <summary>The action completed.</summary>
    public const string Success = "Success";

    /// <summary>The action was attempted and failed.</summary>
    public const string Failure = "Failure";
}
