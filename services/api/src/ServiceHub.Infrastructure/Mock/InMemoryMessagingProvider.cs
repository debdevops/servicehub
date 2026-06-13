using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.Mock;

/// <summary>
/// <see cref="ICloudMessagingProvider"/> implementation that serves in-memory seeded data.
/// Activated when:
/// <list type="bullet">
///   <item>The environment variable <c>SERVICEHUB_MOCK_PROVIDER=true</c> is set, or</item>
///   <item>The namespace connection string starts with <c>mock://</c>.</item>
/// </list>
/// This allows the full ServiceHub stack to be exercised locally without connecting
/// to any real Azure, AWS, or GCP messaging service.
/// </summary>
public sealed class InMemoryMessagingProvider : ICloudMessagingProvider
{
    private static readonly bool _globalMockEnabled =
        string.Equals(
            Environment.GetEnvironmentVariable(MockNamespaces.MockProviderEnvVar),
            "true",
            StringComparison.OrdinalIgnoreCase);

    private readonly MockMessageStore _store;
    private readonly MockMessageReceiver _receiver;
    private readonly MockMessageSender _sender;

    /// <summary>
    /// Initialises a new instance of <see cref="InMemoryMessagingProvider"/>.
    /// </summary>
    public InMemoryMessagingProvider(MockMessageStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _receiver = new MockMessageReceiver(_store);
        _sender = new MockMessageSender(_store);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns <see cref="CloudProviderType.Azure"/> so that it acts as a transparent in-process
    /// stand-in for the real Azure provider during development and testing.  When AWS and GCP
    /// providers are implemented in Phase 2 this value can be made configurable.
    /// </remarks>
    public CloudProviderType ProviderType => CloudProviderType.Azure;

    /// <summary>
    /// Returns <see langword="true"/> when the mock provider should handle the given namespace.
    /// </summary>
    public static bool ShouldActivate(Namespace ns)
    {
        ArgumentNullException.ThrowIfNull(ns);

        if (_globalMockEnabled)
            return true;

        // Connection string starts with "mock://"
        var conn = ns.ConnectionString ?? string.Empty;
        return conn.StartsWith(MockNamespaces.MockConnectionStringPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public Task<Result> ValidateConnectionAsync(Namespace ns, CancellationToken ct)
        => Task.FromResult(Result.Success());

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<CloudEntity>>> ListEntitiesAsync(Guid namespaceId, CancellationToken ct)
    {
        var entities = new List<CloudEntity>();

        // Azure demo namespace
        if (namespaceId == MockNamespaces.MockNamespaceId)
        {
            entities.AddRange(new[]
            {
                new CloudEntity { Name = "orders", EntityType = "Queue", ActiveMessageCount = 40, DeadLetterCount = 8, Provider = CloudProviderType.Azure },
                new CloudEntity { Name = "payments", EntityType = "Queue", ActiveMessageCount = 30, DeadLetterCount = 5, Provider = CloudProviderType.Azure },
                new CloudEntity { Name = "notifications", EntityType = "Queue", ActiveMessageCount = 20, Provider = CloudProviderType.Azure },
                new CloudEntity { Name = "inventory-sync", EntityType = "Queue", ActiveMessageCount = 25, Provider = CloudProviderType.Azure },
            });
        }
        // AWS demo namespace
        else if (namespaceId == MockNamespaces.AwsMockNamespaceId)
        {
            entities.AddRange(new[]
            {
                new CloudEntity { Name = "order-processing", EntityType = "Queue", ActiveMessageCount = 35, DeadLetterCount = 6, Provider = CloudProviderType.Aws },
                new CloudEntity { Name = "payment-gateway-events", EntityType = "Queue", ActiveMessageCount = 20, Provider = CloudProviderType.Aws },
                new CloudEntity { Name = "notification-service", EntityType = "Queue", ActiveMessageCount = 15, Provider = CloudProviderType.Aws },
                new CloudEntity { Name = "fraud-detection", EntityType = "Queue", ActiveMessageCount = 12, Provider = CloudProviderType.Aws },
            });
        }
        // GCP demo namespace
        else if (namespaceId == MockNamespaces.GcpMockNamespaceId)
        {
            entities.AddRange(new[]
            {
                new CloudEntity { Name = "patient-intake", EntityType = "Topic", ActiveMessageCount = 30, Provider = CloudProviderType.Gcp },
                new CloudEntity { Name = "lab-results", EntityType = "Topic", ActiveMessageCount = 22, Provider = CloudProviderType.Gcp },
                new CloudEntity { Name = "billing-events", EntityType = "Topic", ActiveMessageCount = 18, Provider = CloudProviderType.Gcp },
                new CloudEntity { Name = "clinical-alerts", EntityType = "Topic", ActiveMessageCount = 0, DeadLetterCount = 4, Provider = CloudProviderType.Gcp },
            });
        }

        IReadOnlyList<CloudEntity> result = entities;
        return Task.FromResult(Result<IReadOnlyList<CloudEntity>>.Success(result));
    }

    /// <inheritdoc/>
    public IMessageReceiver GetMessageReceiver() => _receiver;

    /// <inheritdoc/>
    public IMessageSender GetMessageSender() => _sender;
}
