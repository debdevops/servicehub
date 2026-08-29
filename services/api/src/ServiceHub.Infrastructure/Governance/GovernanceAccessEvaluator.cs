using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.Governance;

/// <summary>
/// See <see cref="IGovernanceAccessEvaluator"/> for the full contract. Reads only from
/// <see cref="IGovernanceGrantService"/> — no new persistence, no new table, no schema change,
/// exactly the "middleware/policy handlers reading the durable record" scope the persistence
/// design's §14 left for this roadmap item.
/// </summary>
public sealed class GovernanceAccessEvaluator : IGovernanceAccessEvaluator
{
    private readonly IGovernanceGrantService _governanceGrantService;
    private readonly ILogger<GovernanceAccessEvaluator> _logger;

    public GovernanceAccessEvaluator(IGovernanceGrantService governanceGrantService, ILogger<GovernanceAccessEvaluator> logger)
    {
        _governanceGrantService = governanceGrantService ?? throw new ArgumentNullException(nameof(governanceGrantService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<Result> EvaluateAsync(
        string ownerId,
        string granteeIdentity,
        GovernanceRole requiredRole,
        Guid? namespaceId,
        PillarKind? pillarKind,
        CancellationToken cancellationToken = default)
    {
        var resolution = await ResolveAsync(ownerId, granteeIdentity, namespaceId, pillarKind, cancellationToken);
        if (resolution.IsFailure)
        {
            // Fail closed on an infrastructure error reading the grant store — never silently
            // allow when the durable record itself couldn't be read.
            return Result.Failure(resolution.Error!);
        }

        if (resolution.GovernanceInactiveForOwner)
        {
            return Result.Success();
        }

        if (resolution.MaxApplicableRole is { } maxRole && maxRole >= requiredRole)
        {
            return Result.Success();
        }

        _logger.LogWarning(
            "Governance access denied: {Grantee} has role {ActualRole} (namespace={NamespaceId}, pillar={PillarKind}), required {RequiredRole}",
            LogRedactor.SanitiseForLog(granteeIdentity),
            resolution.MaxApplicableRole?.ToString() ?? "none",
            namespaceId,
            pillarKind,
            requiredRole);

        return Result.Failure(Error.Forbidden(
            ErrorCodes.Governance.InsufficientRole,
            resolution.MaxApplicableRole is { } actualRole
                ? $"'{granteeIdentity}' has Governance role '{actualRole}', which does not meet the required role '{requiredRole}'."
                : $"'{granteeIdentity}' has no Governance grant covering the required role '{requiredRole}'."));
    }

    /// <inheritdoc/>
    public async Task<GovernanceRole?> GetEffectiveRoleAsync(
        string ownerId,
        string granteeIdentity,
        Guid? namespaceId,
        PillarKind? pillarKind,
        CancellationToken cancellationToken = default)
    {
        var resolution = await ResolveAsync(ownerId, granteeIdentity, namespaceId, pillarKind, cancellationToken);
        if (resolution.IsFailure)
        {
            return null;
        }

        return resolution.GovernanceInactiveForOwner ? GovernanceRole.Admin : resolution.MaxApplicableRole;
    }

    private async Task<Resolution> ResolveAsync(
        string ownerId,
        string granteeIdentity,
        Guid? namespaceId,
        PillarKind? pillarKind,
        CancellationToken cancellationToken)
    {
        var activeGrantsResult = await _governanceGrantService.GetActiveGrantsAsync(ownerId, cancellationToken);
        if (activeGrantsResult.IsFailure)
        {
            return Resolution.Failed(activeGrantsResult.Error);
        }

        var activeGrants = activeGrantsResult.Value;
        if (activeGrants.Count == 0)
        {
            // Bootstrap safety: this owner has never had a Governance grant created — no seed has
            // run yet, or this is a genuinely fresh owner. Governance is not activated for this
            // tenant, so behave exactly as before M3 shipped: unrestricted.
            return Resolution.Inactive();
        }

        // A grant's GranteeIdentity may name this exact resolved actor, or may equal OwnerId
        // itself — GovernanceGrantSeeder's grandfathering convention for "this whole tenant,
        // undifferentiated," used because the HTTP actor-identity precedence
        // (ApiKey:{name}/claims name) that differentiates individual callers within one owner
        // partition does not, and should not, match the coarse grant the seeder created before
        // any per-identity grant existed. Both count as applicable to this caller; per the
        // "additive-permissive, never restrictive" convention already documented on
        // GovernanceGrant's EF configuration, the caller gets the highest role either grants.
        var applicable = activeGrants
            .Where(g => IsSameIdentity(g.GranteeIdentity, granteeIdentity) || IsSameIdentity(g.GranteeIdentity, ownerId))
            .Where(g => g.NamespaceId is null || g.NamespaceId == namespaceId)
            .Where(g => g.PillarKind is null || g.PillarKind == pillarKind)
            .ToList();

        return applicable.Count == 0
            ? Resolution.NoMatch()
            : Resolution.Matched(applicable.Max(g => g.Role));
    }

    private static bool IsSameIdentity(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);

    private readonly struct Resolution
    {
        public bool IsFailure { get; private init; }
        public Error? Error { get; private init; }
        public bool GovernanceInactiveForOwner { get; private init; }
        public GovernanceRole? MaxApplicableRole { get; private init; }

        public static Resolution Failed(Error error) => new() { IsFailure = true, Error = error };
        public static Resolution Inactive() => new() { GovernanceInactiveForOwner = true };
        public static Resolution NoMatch() => new();
        public static Resolution Matched(GovernanceRole role) => new() { MaxApplicableRole = role };
    }
}
