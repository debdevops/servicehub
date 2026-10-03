namespace ServiceHub.Core.Models;

/// <summary>
/// What a provider's <c>ReplayMessageAsync</c> actually did, beyond plain success/failure.
/// </summary>
/// <remarks>
/// Added for unit `4.2` (DLQ observer attestation): proving a replay "stayed fixed" via an
/// AWS/GCP observer's log requires looking the replay up by the message's <b>new</b>
/// provider-assigned ID — replay always mints a fresh one, on every provider — which nothing
/// captured before this. <see cref="ProviderMessageId"/> is that ID; <see cref="MarkerApplied"/>
/// is the pre-existing "was the recovery marker actually stamped" signal (previously the entire
/// return value, as a bare <c>bool</c>).
/// </remarks>
/// <param name="MarkerApplied">
/// Whether the <c>x-servicehub-recovery-id</c> marker was actually applied to the replayed
/// message. False whenever no marker was requested, stamping was disabled/unsupported, or the
/// provider refused it (e.g. SQS's 10-attribute cap).
/// </param>
/// <param name="ProviderMessageId">
/// The provider-assigned ID of the message as it now exists in the source queue/topic after
/// replay — e.g. SQS's <c>SendMessage</c> response <c>MessageId</c>, or Pub/Sub's published
/// message ID. Null where a provider has nothing meaningful to report here (Azure Service Bus's
/// send has no such response, and none is needed there: <c>CanProveDlqAbsence</c> is already
/// true for Azure via its own uncapped peek, so nothing ever needs to look this ID up).
/// </param>
public readonly record struct ReplayExecutionResult(bool MarkerApplied, string? ProviderMessageId);
