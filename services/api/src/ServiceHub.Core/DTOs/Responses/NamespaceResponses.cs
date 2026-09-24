using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;

namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// A connected namespace as the UI sees it. <b>There is no connection string here, and never will
/// be</b> — not masked, not hashed, not present. A credential leaves the process only inside the
/// provider client that needs it.
/// </summary>
/// <param name="Id">The namespace's identifier.</param>
/// <param name="Name">The fully qualified namespace name.</param>
/// <param name="DisplayName">A friendlier name, when one was given.</param>
/// <param name="Description">Free text, when one was given.</param>
/// <param name="Provider">The cloud that hosts it.</param>
/// <param name="Environment">Dev, Uat or Prod.</param>
/// <param name="AuthType">How ServiceHub authenticates to it.</param>
/// <param name="AwsRegion">The AWS region; null for other clouds.</param>
/// <param name="GcpProjectId">The GCP project; null for other clouds.</param>
/// <param name="IsActive">Whether ServiceHub is watching it.</param>
/// <param name="CreatedAt">When it was connected.</param>
/// <param name="LastConnectionTestAt">When it was last tested; null if never.</param>
/// <param name="LastConnectionTestSucceeded">The last test's outcome; null if never tested.</param>
/// <param name="Capabilities">
/// What this namespace's provider genuinely supports. Carried on the response so the UI asks it
/// "can this do X?" instead of deriving the answer from the provider's name (rule R4). Null only
/// when no adapter for this provider is registered in this build.
/// </param>
public sealed record NamespaceResponse(
    Guid Id,
    string Name,
    string? DisplayName,
    string? Description,
    CloudProviderType Provider,
    EnvironmentType Environment,
    ConnectionAuthType AuthType,
    string? AwsRegion,
    string? GcpProjectId,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastConnectionTestAt,
    bool? LastConnectionTestSucceeded,
    ProviderCapabilities? Capabilities);

/// <summary>The outcome of testing a namespace's connection.</summary>
/// <param name="IsConnected">Whether a live probe succeeded.</param>
/// <param name="Message">One sentence saying what happened, in words a person can act on.</param>
/// <param name="TestedAt">When the probe ran.</param>
public sealed record ConnectionTestResponse(bool IsConnected, string Message, DateTimeOffset TestedAt);

/// <summary>One kind of entity and how many of it a namespace has.</summary>
/// <param name="Kind">queue, topic or subscription.</param>
/// <param name="Count">How many were found.</param>
public sealed record EntityKindCount(string Kind, int Count);

/// <summary>
/// A namespace's shape at a glance. Message totals are <b>nullable on purpose</b>: a provider that
/// cannot count messages reports <see langword="null"/>, never <c>0</c>, because "empty" and
/// "cannot know" are different answers (rule R5).
/// </summary>
/// <param name="NamespaceId">The namespace these figures describe.</param>
/// <param name="Entities">How many entities of each kind exist.</param>
/// <param name="ActiveMessages">Total active messages, or null when the provider cannot count.</param>
/// <param name="DeadLetterMessages">Total dead-lettered messages, or null when the provider cannot count.</param>
/// <param name="MessageCountsSupported">Whether this provider can count messages at all.</param>
/// <param name="ObservedAt">When the figures were read from the provider.</param>
public sealed record NamespaceStatsResponse(
    Guid NamespaceId,
    IReadOnlyList<EntityKindCount> Entities,
    long? ActiveMessages,
    long? DeadLetterMessages,
    bool MessageCountsSupported,
    DateTimeOffset ObservedAt);

/// <summary>One messaging entity in a namespace.</summary>
/// <param name="Name">The entity's name (a subscription's includes its topic).</param>
/// <param name="Kind">queue, topic or subscription.</param>
/// <param name="ActiveMessages">Active messages, or null when the provider cannot count.</param>
/// <param name="DeadLetterMessages">Dead-lettered messages, or null when the provider cannot count.</param>
/// <param name="DeadLetterTargetName">Where a separate dead-letter queue redrives, when the provider has one.</param>
public sealed record EntityResponse(
    string Name,
    string Kind,
    long? ActiveMessages,
    long? DeadLetterMessages,
    string? DeadLetterTargetName);

/// <summary>The entities of one namespace, filtered to one kind when asked.</summary>
/// <param name="NamespaceId">The namespace listed.</param>
/// <param name="Entities">The entities found.</param>
public sealed record EntityListResponse(Guid NamespaceId, IReadOnlyList<EntityResponse> Entities);
