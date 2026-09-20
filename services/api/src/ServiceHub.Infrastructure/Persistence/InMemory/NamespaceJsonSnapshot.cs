using System.Text.Json;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Security;

namespace ServiceHub.Infrastructure.Persistence.InMemory;

/// <summary>
/// The on-disk shape of <c>servicehub-namespaces.json</c> and the rehydration logic that turns a
/// snapshot back into a real <see cref="Namespace"/> — shared between <see cref="InMemoryNamespaceRepository"/>
/// (which reads it on every startup) and the one-shot JSON→SQLite import step (<c>Program.cs</c>,
/// M2 of the persistence wave), which reads it exactly once, on the first startup after upgrading.
/// Kept in one place so the two readers can never silently drift on what a snapshot means.
/// </summary>
public static class NamespaceJsonSnapshot
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public sealed class Entry
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? DisplayName { get; init; }
        public string? Description { get; init; }
        public string? ConnectionString { get; init; }
        public ConnectionAuthType AuthType { get; init; }
        public bool IsActive { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? ModifiedAt { get; init; }
        public DateTimeOffset? LastConnectionTestAt { get; init; }
        public bool? LastConnectionTestSucceeded { get; init; }
        public bool HasListenPermission { get; init; }
        public bool HasSendPermission { get; init; }
        public bool HasManagePermission { get; init; }
        public EnvironmentType Environment { get; init; }
        /// <summary>
        /// Owner identifier for tenant isolation. Defaults to the SPA owner so that
        /// namespaces written before this field existed remain visible to the instance admin.
        /// </summary>
        public string OwnerId { get; init; } = Namespace.SpaOwnerId;
        /// <summary>
        /// Additional owner IDs this namespace is shared with. Null/absent on files written
        /// before sharing existed — deserialises to null and is normalised to an empty list on
        /// rehydration, so older snapshot files load unaffected.
        /// </summary>
        public List<string>? SharedWithOwnerIds { get; init; }
        /// <summary>SHA-256 hash of the plaintext connection string for fast deduplication.</summary>
        public string? ConnectionStringHash { get; init; }
        /// <summary>Cloud provider (Azure, AWS, GCP). Defaults to Azure for backward compatibility.</summary>
        public CloudProviderType Provider { get; init; } = CloudProviderType.Azure;
        /// <summary>AWS region identifier. Null for non-AWS namespaces.</summary>
        public string? AwsRegion { get; init; }
        /// <summary>GCP project identifier. Null for non-GCP namespaces.</summary>
        public string? GcpProjectId { get; init; }
    }

    public static Entry ToSnapshot(Namespace ns) => new()
    {
        Id = ns.Id,
        Name = ns.Name,
        DisplayName = ns.DisplayName,
        Description = ns.Description,
        ConnectionString = ns.ConnectionString,
        AuthType = ns.AuthType,
        IsActive = ns.IsActive,
        CreatedAt = ns.CreatedAt,
        ModifiedAt = ns.ModifiedAt,
        LastConnectionTestAt = ns.LastConnectionTestAt,
        LastConnectionTestSucceeded = ns.LastConnectionTestSucceeded,
        HasListenPermission = ns.HasListenPermission,
        HasSendPermission = ns.HasSendPermission,
        HasManagePermission = ns.HasManagePermission,
        Environment = ns.Environment,
        OwnerId = ns.OwnerId,
        SharedWithOwnerIds = ns.SharedWithOwnerIds.Count > 0 ? [.. ns.SharedWithOwnerIds] : null,
        ConnectionStringHash = ns.ConnectionStringHash,
        Provider = ns.Provider,
        AwsRegion = ns.AwsRegion,
        GcpProjectId = ns.GcpProjectId,
    };

    /// <summary>
    /// Reconstructs a <see cref="Namespace"/> from a snapshot, or null (logged) if the snapshot no
    /// longer passes <see cref="Namespace"/>'s own validation. Dispatches on stored credentials,
    /// not <c>AuthType</c>: AWS/GCP namespaces persist with AwsAccessKey/GcpServiceAccount auth
    /// types but still carry a connection string, and <c>CreateWithManagedIdentity</c> would
    /// reject (and drop) them.
    /// </summary>
    public static Namespace? Rehydrate(Entry snapshot, ILogger logger)
    {
        try
        {
            var createResult = !string.IsNullOrWhiteSpace(snapshot.ConnectionString)
                ? Namespace.Create(
                    snapshot.Name,
                    snapshot.ConnectionString ?? string.Empty,
                    snapshot.DisplayName,
                    snapshot.Description,
                    snapshot.Environment,
                    provider: snapshot.Provider,
                    ownerId: snapshot.OwnerId,
                    connectionStringHash: snapshot.ConnectionStringHash,
                    awsRegion: snapshot.AwsRegion,
                    gcpProjectId: snapshot.GcpProjectId)
                : Namespace.CreateWithManagedIdentity(
                    snapshot.Name,
                    snapshot.AuthType,
                    snapshot.DisplayName,
                    snapshot.Description,
                    snapshot.Environment,
                    provider: snapshot.Provider,
                    ownerId: snapshot.OwnerId,
                    awsRegion: snapshot.AwsRegion,
                    gcpProjectId: snapshot.GcpProjectId);

            if (createResult.IsFailure)
            {
                logger.LogWarning(
                    "Skipping persisted namespace {Name} due to validation failure while rehydrating",
                    LogRedactor.SanitiseForLog(snapshot.Name));
                return null;
            }

            var ns = createResult.Value;

            SetPrivateProperty(ns, nameof(Namespace.Id), snapshot.Id);
            SetPrivateProperty(ns, nameof(Namespace.CreatedAt), snapshot.CreatedAt);
            SetPrivateProperty(ns, nameof(Namespace.ModifiedAt), snapshot.ModifiedAt);
            SetPrivateProperty(ns, nameof(Namespace.LastConnectionTestAt), snapshot.LastConnectionTestAt);
            SetPrivateProperty(ns, nameof(Namespace.LastConnectionTestSucceeded), snapshot.LastConnectionTestSucceeded);
            SetPrivateProperty(ns, nameof(Namespace.HasListenPermission), snapshot.HasListenPermission);
            SetPrivateProperty(ns, nameof(Namespace.HasSendPermission), snapshot.HasSendPermission);
            SetPrivateProperty(ns, nameof(Namespace.HasManagePermission), snapshot.HasManagePermission);
            SetPrivateProperty(ns, nameof(Namespace.Environment), snapshot.Environment);
            SetPrivateProperty(ns, nameof(Namespace.SharedWithOwnerIds), (IReadOnlyList<string>)(snapshot.SharedWithOwnerIds ?? []));

            if (!snapshot.IsActive)
            {
                ns.Deactivate();
            }

            return ns;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to rehydrate persisted namespace {Name}", LogRedactor.SanitiseForLog(snapshot.Name));
            return null;
        }
    }

    internal static void SetPrivateProperty<T>(Namespace target, string propertyName, T value)
    {
        var property = typeof(Namespace).GetProperty(
            propertyName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

        property?.SetValue(target, value);
    }
}
