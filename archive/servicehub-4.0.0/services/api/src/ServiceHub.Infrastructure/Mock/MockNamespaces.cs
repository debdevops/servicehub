namespace ServiceHub.Infrastructure.Mock;

/// <summary>
/// Well-known mock namespace IDs used throughout the mock provider infrastructure.
/// These GUIDs are stable so that seeded data can be looked up by entity name.
/// </summary>
public static class MockNamespaces
{
    /// <summary>Azure Service Bus — Contoso Commerce demo namespace.</summary>
    public static readonly Guid MockNamespaceId = new("00000000-0000-0000-0000-000000000001");

    /// <summary>AWS SQS — AcmeRetail e-commerce demo namespace.</summary>
    public static readonly Guid AwsMockNamespaceId = new("00000000-0000-0000-0000-000000000002");

    /// <summary>GCP Pub/Sub — MedStream Healthcare demo namespace.</summary>
    public static readonly Guid GcpMockNamespaceId = new("00000000-0000-0000-0000-000000000003");

    /// <summary>Mock connection string prefix that triggers the mock provider.</summary>
    public const string MockConnectionStringPrefix = "mock://";

    /// <summary>Environment variable that enables the mock provider globally.</summary>
    public const string MockProviderEnvVar = "SERVICEHUB_MOCK_PROVIDER";
}
