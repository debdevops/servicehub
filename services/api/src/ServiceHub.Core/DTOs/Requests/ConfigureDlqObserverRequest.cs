namespace ServiceHub.Core.DTOs.Requests;

/// <summary>Turns a cloud's DLQ observer on or off (unit 4.2). Only set it up after the observer module has been applied in that cloud.</summary>
/// <param name="Enabled">Whether to turn it on.</param>
/// <param name="ObserverReference">The DynamoDB table (AWS) or Firestore collection (Google Cloud) the observer writes to. Required to turn it on.</param>
/// <param name="DlqEntityName">The dead-letter queue (AWS) or topic (Google Cloud) the test message goes to. Required to turn it on.</param>
/// <param name="StalenessBoundMinutes">How old the last confirmation may be before the observer counts as not live. Default 30.</param>
public sealed record ConfigureDlqObserverRequest(
    bool Enabled,
    string? ObserverReference = null,
    string? DlqEntityName = null,
    int? StalenessBoundMinutes = null);
