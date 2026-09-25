using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>How the replays of one signature ended, from the ledger. Recovered and returned are never merged with "unverified".</summary>
public sealed record SignatureReplays(int Replayed, int StayedFixed, int Returned, int Unverified);

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
    string ReplayVerdict);

/// <summary>A page of signatures with the tab counts, so a tab never shows a number its list cannot back.</summary>
public sealed record SignaturePage(
    IReadOnlyList<SignatureSummary> Items, int Total, int Page, int PageSize, int All, int Growing, int ReplayHelps, int ReplayDoesNotHelp);
