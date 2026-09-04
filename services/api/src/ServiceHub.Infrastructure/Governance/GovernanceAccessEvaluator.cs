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
        // undifferentiated," created before any per-identity grant existed. That grant must stop
        // applying to a caller the moment an admin has granted *that specific identity* anything
        // at all: once differentiated, a caller's access is determined solely by their own
        // grants, never topped up by the coarse owner-level grant — otherwise a Viewer-only grant
        // for one credential is silently satisfied by the fleet-wide Admin grant every real
        // deployment seeds for its primary owner (`__spa__`), and per-identity restriction never
        // actually restricts anything for that owner. (Live-verified 2026-09-04: a legacy API key
        // holding only a namespace-scoped Viewer grant successfully replayed a message, because
        // the seed's GranteeIdentity=="__spa__"==ownerId matched via the fallback below and its
        // Admin role beat Viewer under the old "highest of either" rule.) Multiple grants that
        // both name this exact identity remain additive-permissive, per GovernanceGrant's EF
        // configuration — the caller gets the highest role among *those*.
        var ownGrants = activeGrants
            .Where(g => IsSameIdentity(g.GranteeIdentity, granteeIdentity))
            .ToList();

        var candidates = ownGrants.Count > 0
            ? ownGrants
            : activeGrants.Where(g => IsSameIdentity(g.GranteeIdentity, ownerId)).ToList();

        var applicable = candidates
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
