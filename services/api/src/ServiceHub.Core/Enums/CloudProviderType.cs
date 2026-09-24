// Ported from ServiceHub 4.0.0
//   source: archive/servicehub-4.0.0/services/api/src/ServiceHub.Core/Enums/CloudProviderType.cs
//   copied: 2026-09-21 for unit 0.5
//   changes: none

namespace ServiceHub.Core.Enums;

/// <summary>
/// Identifies which cloud messaging platform backs a namespace connection.
/// Phase 1 ships Azure support only; AWS and GCP providers are registered in later phases.
/// </summary>
public enum CloudProviderType
{
    /// <summary>
    /// Microsoft Azure Service Bus — the original and default provider.
    /// </summary>
    Azure = 0,

    /// <summary>
    /// Amazon Web Services — Amazon SQS and SNS messaging.
    /// </summary>
    Aws = 1,

    /// <summary>
    /// Google Cloud Platform — Google Cloud Pub/Sub messaging.
    /// </summary>
    Gcp = 2
}
