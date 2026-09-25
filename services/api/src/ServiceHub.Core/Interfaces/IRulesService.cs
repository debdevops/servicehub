using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>Auto Replay rules (unit 3.6): list, create from a failure, turn on/off, and test against the last 7 days.</summary>
public interface IRulesService
{
    /// <summary>The owner's rules for one cloud, with what happened under each.</summary>
    Task<IReadOnlyList<RuleView>> ListAsync(string ownerId, CloudProviderType provider, CancellationToken ct);

    /// <summary>Failures seen recently that a rule could be made from (one row per signature).</summary>
    Task<IReadOnlyList<RuleSource>> SourcesAsync(string ownerId, CloudProviderType provider, CancellationToken ct);

    /// <summary>Makes a rule. It starts on.</summary>
    Task<Result<RuleView>> CreateAsync(string ownerId, CloudProviderType provider, string name, string? reason, string? entity, string? signatureHash, int maxPerHour, int waitSeconds, bool backOff, CancellationToken ct);

    /// <summary>Turns a rule on or off. A rule the circuit breaker stopped can be turned back on only by a person, on purpose.</summary>
    Task<Result<RuleView>> SetEnabledAsync(string ownerId, long id, bool enabled, CancellationToken ct);

    /// <summary>What the rule would have done over the last <paramref name="days"/> days, judged by today's checks. Sends nothing.</summary>
    Task<RuleTest> TestAsync(string ownerId, IReadOnlySet<Guid>? allowed, CloudProviderType provider, string? reason, string? entity, string? signatureHash, int days, CancellationToken ct);
}
