namespace ServiceHub.Core.Constants;

/// <summary>
/// Stable, machine-readable error codes. Every <c>ProblemDetails</c> ServiceHub returns carries one
/// of these plus a human sentence (ARCHITECTURE §5.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>A code is a promise.</b> Once a version has shipped with one, it keeps its meaning: clients,
/// runbooks and the escalation messages that quote a gate's reason code all depend on it. Add
/// codes; do not repurpose them.
/// </para>
/// <para>
/// 4.0.0's catalogue had 447 lines covering thirty-three controllers. This one grows with the
/// screens that raise the failures (rule R12), so a code here means something in this product.
/// </para>
/// </remarks>
public static class ErrorCodes
{
    /// <summary>An unhandled failure. The detail is deliberately generic; the log has the rest.</summary>
    public const string UnexpectedFailure = "unexpected_failure";

    /// <summary>The request was malformed or failed validation.</summary>
    public const string ValidationFailed = "validation_failed";

    /// <summary>The thing being asked about does not exist, or is not visible to this caller.</summary>
    public const string NotFound = "not_found";

    /// <summary>
    /// The caller is known but not permitted. The response says what is missing and who can grant
    /// it — never a bare 403 (IA §7).
    /// </summary>
    public const string PermissionDenied = "permission_denied";

    /// <summary>
    /// The provider backing this namespace genuinely cannot do what was asked. This is a capability
    /// fact, not a fault — and it always travels with the remedy, where one exists (rule R4).
    /// </summary>
    public const string CapabilityUnavailable = "capability_unavailable";
}
