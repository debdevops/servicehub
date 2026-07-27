using ServiceHub.Core.Enums;

namespace ServiceHub.Infrastructure.AI;

/// <summary>
/// Structured, versioned feature record extracted from a peeked DLQ message.
/// Consumed by clustering/baselining models; extend by bumping
/// <see cref="FeatureVersion"/> rather than repurposing an existing field.
/// </summary>
public sealed record MessageFeatures(
    // Numeric
    int DeliveryCount,
    long BodySizeBytes,
    double TimeToDeadletterSeconds,
    double SecondsSinceEnqueued,
    int HourOfDay,
    int DayOfWeek,
    int PropertyCount,

    // Categorical
    CloudProviderType Provider,
    string EntityName,
    string DeadletterReason,
    string ExceptionType,
    string ContentType,
    string PayloadShape,

    // Derived
    string ErrorTextNormalised,
    string SchemaFingerprint,
    int FeatureVersion)
{
    /// <summary>Current version of the feature schema. Bump when fields are added or redefined.</summary>
    internal const int CurrentFeatureVersion = 1;

    internal const string ShapeJsonObject = "json_object";
    internal const string ShapeJsonArray = "json_array";
    internal const string ShapeXml = "xml";
    internal const string ShapeText = "text";
    internal const string ShapeBinary = "binary";
}
