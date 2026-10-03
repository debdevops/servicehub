using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Entities;

/// <summary>
/// Structured, versioned feature snapshot captured for a DLQ message at scan time.
/// One row per <see cref="DlqMessage"/> — the dataset clustering, baselining, and any
/// future supervised model train on.
/// </summary>
public sealed class MessageFeatureRecord
{
    /// <summary>Primary key.</summary>
    public long Id { get; private set; }

    /// <summary>
    /// Foreign key to the parent DLQ message. Not <c>required</c> on purpose: when a
    /// <see cref="MessageFeatureRecord"/> is added alongside a brand-new <see cref="DlqMessage"/>
    /// in the same unit of work, EF Core's relationship fixup assigns this from
    /// <see cref="DlqMessage"/>'s generated key at <c>SaveChanges</c> time — set
    /// <see cref="DlqMessage"/> instead of this property in that case.
    /// </summary>
    public long DlqMessageId { get; init; }

    /// <summary>Namespace identifier (matches the registered namespace ID).</summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>Owner ID for multi-user isolation. Every query against this table is owner-scoped.</summary>
    public required string OwnerId { get; init; }

    /// <summary>When this feature snapshot was captured (matches the parent message's detection time).</summary>
    public required DateTimeOffset CapturedAt { get; init; }

    // Numeric
    public int DeliveryCount { get; init; }
    public long BodySizeBytes { get; init; }
    public double TimeToDeadletterSeconds { get; init; }
    public double SecondsSinceEnqueued { get; init; }
    public int HourOfDay { get; init; }
    public int DayOfWeek { get; init; }
    public int PropertyCount { get; init; }

    // Categorical
    public required CloudProviderType Provider { get; init; }
    public required string EntityName { get; init; }
    public required string DeadletterReason { get; init; }
    public required string ExceptionType { get; init; }
    public required string ContentType { get; init; }
    public required string PayloadShape { get; init; }

    // Derived
    public required string ErrorTextNormalised { get; init; }
    public required string SchemaFingerprint { get; init; }
    public required int FeatureVersion { get; init; }

    /// <summary>Navigation property: the parent DLQ message.</summary>
    public DlqMessage? DlqMessage { get; init; }
}
