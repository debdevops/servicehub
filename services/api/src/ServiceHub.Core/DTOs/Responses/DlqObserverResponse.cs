namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// One cloud's DLQ observer, as a person sees it (unit 4.2). <see cref="Needed"/> is a capability fact — Azure can confirm a
/// replay stayed fixed without one — and <see cref="Live"/> is true only while the observer's own log has shown a recent test message.
/// </summary>
/// <param name="Needed">Whether this cloud needs an observer to prove a fix held. False for a cloud that can prove it alone.</param>
/// <param name="Enabled">Whether a person turned the observer on for this cloud.</param>
/// <param name="Live">Enabled and confirmed within <paramref name="StalenessBoundMinutes"/>. Never assumed.</param>
/// <param name="ObserverReference">The DynamoDB table (AWS) or Firestore collection (Google Cloud) the observer writes to.</param>
/// <param name="DlqEntityName">The dead-letter queue (AWS) or topic (Google Cloud) the test message is sent to.</param>
/// <param name="StalenessBoundMinutes">How old the last confirmation may be before the observer counts as not live.</param>
/// <param name="LastCanarySentAt">When the last test message was sent.</param>
/// <param name="LastConfirmedAt">When the observer's log last showed a test message.</param>
/// <param name="Status">One plain sentence saying where this stands.</param>
public sealed record DlqObserverResponse(
    bool Needed,
    bool Enabled,
    bool Live,
    string? ObserverReference,
    string? DlqEntityName,
    int StalenessBoundMinutes,
    DateTimeOffset? LastCanarySentAt,
    DateTimeOffset? LastConfirmedAt,
    string Status);
