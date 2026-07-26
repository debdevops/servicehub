namespace ServiceHub.Core.Constants;

/// <summary>
/// Canonical <see cref="Interfaces.ForensicEngineResult.ReplaySafety"/> values. Every forensic
/// engine (base and provider-specific) must return one of these three values so that downstream
/// consumers — <c>BulkOperationService</c>'s unsafe-replay counting, the DLQ history/export API,
/// and the frontend's DLQ History table badge — can rely on a single, closed vocabulary instead
/// of each engine inventing its own strings.
/// </summary>
public static class ReplaySafetyLevels
{
    public const string Safe = "Safe";
    public const string Unsafe = "Unsafe";
    public const string RequiresReview = "RequiresReview";
}
