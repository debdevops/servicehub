using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>How the replays of one signature ended, from the ledger. Recovered and returned are never merged with "unverified".</summary>
public sealed record SignatureReplays(int Replayed, int StayedFixed, int Returned, int Unverified);

/// <summary>A namespace a signature was seen in, with its environment and how many of the signature's messages are there.</summary>
public sealed record SignatureNamespace(Guid Id, string Name, string? DisplayName, EnvironmentType Environment, int Messages);

/// <summary>One failure signature: the same failure, however many messages carry it.</summary>
public sealed record SignatureSummary(
    string SignatureHash,
    CloudProviderType Provider,
    string Reason,
    string? ExampleError,
    IReadOnlyList<string> Entities,
    int Messages,
    int ActiveNow,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    IReadOnlyList<int> Daily,
    bool Growing,
    SignatureReplays Replays,
    string ReplayVerdict,
    IReadOnlyList<SignatureNamespace> Namespaces);

/// <summary>A page of signatures with the tab counts, so a tab never shows a number its list cannot back.</summary>
public sealed record SignaturePage(
    IReadOnlyList<SignatureSummary> Items, int Total, int Page, int PageSize, int All, int Growing, int ReplayHelps, int ReplayDoesNotHelp);

/// <summary>
/// What a failure signature has earned (unit 4.1): whether ServiceHub may replay it on its own, and — in plain numbers —
/// what it still needs. Every figure is counted from recorded outcomes; nothing here is a guess.
/// </summary>
/// <param name="Level"><c>approve</c> (a person approves each replay — the floor), <c>standing</c> (L4) or <c>unattended</c> (L5).</param>
/// <param name="SampleSize">Verified outcomes counted: stayed fixed + came back + failed. "Can't confirm" is never counted.</param>
/// <param name="VerifiedSuccessRate">Stayed fixed ÷ sample; null when there is nothing to divide.</param>
/// <param name="Recovered">Stayed fixed.</param>
/// <param name="Returned">Came back.</param>
/// <param name="Failed">The cloud refused the replay.</param>
/// <param name="Unverified">Replayed, but the cloud cannot confirm the outcome.</param>
/// <param name="NextLevel">The next level up, or null at the top or where it cannot climb.</param>
/// <param name="MoreVerifiedNeeded">How many more verified outcomes the next level needs, at least; null when none.</param>
/// <param name="RateNeeded">The success rate the next level needs; null when there is no next level.</param>
/// <param name="CloudCanConfirm">Whether this cloud can prove a replayed message stayed out of the dead-letter queue — without it, nothing climbs past approve.</param>
/// <param name="ProductionCeiling">True when the signature is in Production, where nothing ever climbs past approve.</param>
/// <param name="Reasons">The scorer's own sentences.</param>
public sealed record SignatureTrust(
    string Level, int SampleSize, double? VerifiedSuccessRate, int Recovered, int Returned, int Failed, int Unverified,
    string? NextLevel, int? MoreVerifiedNeeded, double? RateNeeded, bool CloudCanConfirm, bool ProductionCeiling, IReadOnlyList<string> Reasons);
