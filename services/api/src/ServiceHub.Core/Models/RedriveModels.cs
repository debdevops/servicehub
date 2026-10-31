namespace ServiceHub.Core.Models;

/// <summary>Whether a person may ask the cloud to redrive one failed run now, and if not, why not (plan PP5).</summary>
/// <param name="CanRedrive">True when the gate would allow it.</param>
/// <param name="ReasonCode">The gate's stable code when it would not.</param>
/// <param name="Message">One plain sentence for the dialog.</param>
/// <param name="Verb">What the cloud calls the action: <c>redrive</c> or <c>resubmit</c>.</param>
/// <param name="PreviousRequestAtUtc">When the cloud last accepted this same run's request, or null when it never did. A warning, not a block.</param>
public sealed record RedriveCheck(bool CanRedrive, string? ReasonCode, string Message, string Verb = "redrive", DateTimeOffset? PreviousRequestAtUtc = null);

/// <summary>What a redrive request ended as. It says what the cloud accepted and nothing more: the run's result is never known.</summary>
/// <param name="EntryId">The ledger entry.</param>
/// <param name="OperationId">The ledger operation.</param>
/// <param name="Result"><c>accepted</c>, <c>rejected</c> or <c>unknown</c>.</param>
/// <param name="State">The entry's state: <c>Unverified</c> when accepted.</param>
/// <param name="Message">One plain sentence.</param>
/// <param name="ErrorCode">The error code when it was not accepted, otherwise null.</param>
public sealed record RedriveOutcome(Guid EntryId, Guid OperationId, string Result, string State, string Message, string? ErrorCode);
