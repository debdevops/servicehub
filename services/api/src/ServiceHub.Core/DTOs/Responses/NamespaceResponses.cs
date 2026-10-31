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
/// <param name="CanSend">What the person declared: false is read-only, true is read and send, null is not said (treated as may send).</param>
/// <param name="OwnerChannelId">The one notification channel told about this connection; null means every enabled channel.</param>
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
    ProviderCapabilities? Capabilities,
    bool? CanSend = null,
    Guid? OwnerChannelId = null);

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

/// <summary>One limit or setting of an entity, in the cloud's own words.</summary>
/// <param name="Label">What the cloud calls it.</param>
/// <param name="Value">The value as text, with its unit.</param>
public sealed record EntitySettingResponse(string Label, string Value);

/// <summary>An entity's limits and settings.</summary>
/// <param name="Name">The entity's name.</param>
/// <param name="Kind">queue, topic or subscription.</param>
/// <param name="Settings">The settings read, or null when this cloud does not read them for this kind of entity.</param>
public sealed record EntitySettingsResponse(string Name, string Kind, IReadOnlyList<EntitySettingResponse>? Settings);

/// <summary>A place a non-message component keeps its failures (a state machine, a workflow).</summary>
public sealed record SourcePlaceResponse(string Id, string Name, string Kind, string? ConsoleUrl);

/// <summary>
/// Whether a connection can read a component that is not a message store, and its places. <c>available</c> false carries the reason
/// ("Not enabled: ..."); it is a state, not an error. Nothing here can be sent back to a cloud.
/// </summary>
public sealed record ComponentSourceResponse(
    string ComponentKey, bool Available, string? Reason, bool ReasonInList, IReadOnlyList<SourcePlaceResponse> Places, bool PlacesReachedLimit = false, bool PlacesIncomplete = false);

/// <summary>One failed run or delivery. <c>reasonCode</c> is null when the list did not read it.</summary>
public sealed record FailedItemResponse(
    string Id, string Place, DateTimeOffset? AtUtc, string Status, string? ReasonCode, string Summary, string Copy, string? ConsoleUrl, HeldInResponse? HeldIn = null);

/// <summary>Another component's entity that also holds a failed item, where its own replay can put it back.</summary>
public sealed record HeldInResponse(string ComponentKey, string Entity);

/// <summary>Failed items, newest first. <c>reachedLimit</c> says there are more than were returned; <c>incomplete</c> says part of the read failed or timed out.</summary>
public sealed record FailedItemsResponse(string ComponentKey, IReadOnlyList<FailedItemResponse> Items, bool ReachedLimit, bool Incomplete);

/// <summary>One failed item in full, redacted. <c>item</c> is null when the cloud no longer has it.</summary>
public sealed record FailedItemDetailResponse(
    FailedItemResponse? Item, string? Reason, string? Cause, DateTimeOffset? StartedUtc, DateTimeOffset? StoppedUtc);


/// <summary>One copy-ready block of a setup guide.</summary>
public sealed record SetupSnippetResponse(string Label, string Text);

/// <summary>
/// One entry of the Add a component dialog for a connection. <c>access</c> is <c>sameConnection</c>, <c>ownCredential</c>,
/// <c>notBuildable</c> or <c>included</c>; <c>state</c> is <c>on</c>, <c>off</c>, <c>alwaysOn</c> (watched on every connection, cannot be
/// unticked), <c>included</c> or <c>unavailable</c>. <c>canToggle</c> says whether the person may tick or untick it.
/// </summary>
public sealed record ComponentEntryResponse(
    string ComponentKey, string Name, string StuckPlace, string Status, string StatusWords, string Access, string State, bool CanToggle,
    string? Note, string? What, string? Appears, string? Needs, IReadOnlyList<string> Steps, IReadOnlyList<SetupSnippetResponse> Snippets, IReadOnlyList<string> Permissions,
    string? IncludedIn);

/// <summary><c>GET /api/v1/namespaces/{id}/components</c>: what can be brought in on this connection, and what already is.</summary>
public sealed record ComponentListResponse(Guid NamespaceId, string ConnectionName, string Cloud, IReadOnlyList<ComponentEntryResponse> Components);

/// <summary>The result of Check access. Read-only: <c>ok</c> false carries the reason, which names what is missing.</summary>
public sealed record ComponentCheckResponse(string ComponentKey, bool Ok, string? Reason, IReadOnlyList<string> Permissions);

/// <summary>One ticked component on one connection, for the menu.</summary>
public sealed record EnabledComponentResponse(Guid NamespaceId, string Cloud, string ComponentKey);
