using System.Collections.Concurrent;
using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SimpleNotificationService;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.Aws;

/// <summary>
/// Constructs and caches AWS SDK clients from <see cref="Namespace"/> credentials.
/// <para>
/// Connection string conventions:
/// <list type="bullet">
///   <item><term>AwsAccessKey</term><description>Stored as <c>AKID:SecretKey</c> (AES-GCM encrypted at rest).</description></item>
///   <item><term>AwsIamRole</term><description>Role ARN stored as the connection string (no secret needed).</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class AwsClientFactory : IAwsClientFactory
{
    private readonly IConnectionStringProtector _protector;
    private readonly ILogger<AwsClientFactory> _logger;

    // Keyed by namespace ID for O(1) lookup without re-creating SDK clients per request.
    private readonly ConcurrentDictionary<Guid, IAmazonSQS> _sqsCache = new();
    private readonly ConcurrentDictionary<Guid, IAmazonSimpleNotificationService> _snsCache = new();

    /// <summary>
    /// Initialises a new instance of <see cref="AwsClientFactory"/>.
    /// </summary>
    /// <param name="protector">Service that decrypts stored connection strings.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public AwsClientFactory(
        IConnectionStringProtector protector,
        ILogger<AwsClientFactory> logger)
    {
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public IAmazonSQS GetSqsClient(Namespace ns)
    {
        ArgumentNullException.ThrowIfNull(ns);
        return _sqsCache.GetOrAdd(ns.Id, _ => CreateSqsClient(ns));
    }

    /// <inheritdoc/>
    public IAmazonSimpleNotificationService GetSnsClient(Namespace ns)
    {
        ArgumentNullException.ThrowIfNull(ns);
        return _snsCache.GetOrAdd(ns.Id, _ => CreateSnsClient(ns));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private IAmazonSQS CreateSqsClient(Namespace ns)
    {
        var region = ResolveRegion(ns);
        var credentials = ResolveCredentials(ns);
        _logger.LogDebug("Creating SQS client for namespace {NamespaceId} in region {Region}", ns.Id, region.SystemName);
        return new AmazonSQSClient(credentials, region);
    }

    private IAmazonSimpleNotificationService CreateSnsClient(Namespace ns)
    {
        var region = ResolveRegion(ns);
        var credentials = ResolveCredentials(ns);
        _logger.LogDebug("Creating SNS client for namespace {NamespaceId} in region {Region}", ns.Id, region.SystemName);
        return new AmazonSimpleNotificationServiceClient(credentials, region);
    }

    private RegionEndpoint ResolveRegion(Namespace ns)
    {
        var regionName = ns.AwsRegion;
        if (string.IsNullOrWhiteSpace(regionName))
        {
            _logger.LogWarning("No AWS region specified for namespace {NamespaceId} — falling back to us-east-1", ns.Id);
            regionName = "us-east-1";
        }
        return RegionEndpoint.GetBySystemName(regionName);
    }

    private AWSCredentials ResolveCredentials(Namespace ns)
    {
        switch (ns.AuthType)
        {
            case ConnectionAuthType.AwsAccessKey:
            {
                var connStr = DecryptConnectionString(ns);
                var parts = connStr.Split(':', 2);
                if (parts.Length != 2)
                    throw new InvalidOperationException(
                        $"AwsAccessKey connection string for namespace {ns.Id} must be in format 'AKID:SecretKey'.");
                return new BasicAWSCredentials(parts[0].Trim(), parts[1].Trim());
            }

            case ConnectionAuthType.AwsIamRole:
            {
                // RoleArn is stored in the connection string (plaintext — no secret value).
                var roleArn = ns.ConnectionString ?? throw new InvalidOperationException(
                    $"AwsIamRole namespace {ns.Id} has no RoleArn in ConnectionString.");

                // AssumeRoleAWSCredentials takes source credentials + the role ARN to assume.
                // Use the default credential chain as the source (instance profile, env vars, etc.).
                var sourceCredentials = FallbackCredentialsFactory.GetCredentials();
                return new AssumeRoleAWSCredentials(sourceCredentials, roleArn, $"ServiceHub-{ns.Id:N}");
            }

            case ConnectionAuthType.AwsOidc:
                // OIDC uses the DefaultInstanceProfileAWSCredentials chain (EKS, GitHub Actions, etc.)
                return FallbackCredentialsFactory.GetCredentials();

            default:
                _logger.LogWarning(
                    "Namespace {NamespaceId} has auth type {AuthType} which is not an AWS auth type. Using anonymous credentials.",
                    ns.Id, ns.AuthType);
                return new AnonymousAWSCredentials();
        }
    }

    private string DecryptConnectionString(Namespace ns)
    {
        if (string.IsNullOrWhiteSpace(ns.ConnectionString))
            throw new InvalidOperationException($"Namespace {ns.Id} has no connection string.");

        var result = _protector.Unprotect(ns.ConnectionString);
        if (result.IsFailure)
            throw new InvalidOperationException(
                $"Failed to decrypt connection string for namespace {ns.Id}: {result.Error.Message}");

        return result.Value;
    }
}
