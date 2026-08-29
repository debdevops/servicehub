using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure;

/// <summary>
/// Governance/RBAC grant management (M3 of the persistence wave) — see <see cref="GovernanceGrant"/>
/// and <see cref="IGovernanceGrantService"/> for the full design rationale. No authorization
/// enforcement lives here; this only manages the grants a future enforcement layer will read.
/// </summary>
public sealed class GovernanceGrantService : IGovernanceGrantService
{
    private readonly DlqDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly ILogger<GovernanceGrantService> _logger;

    public GovernanceGrantService(DlqDbContext dbContext, IAuditService auditService, ILogger<GovernanceGrantService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<Result<GovernanceGrant>> GrantAsync(GrantRoleRequest request, CancellationToken cancellationToken = default)
    {
        // The database's own filtered unique index catches most duplicate-scope cases, but SQLite
        // (like standard SQL) treats NULL as distinct from NULL for uniqueness purposes, so a
        // fleet-wide/all-pillar (NamespaceId=null, PillarKind=null) duplicate would slip past it.
        // Checking in code, where null == null compares correctly, closes that gap.
        var activeForGrantee = await _dbContext.GovernanceGrants
            .AsNoTracking()
            .Where(g => g.OwnerId == request.OwnerId && g.GranteeIdentity == request.GranteeIdentity && g.RevokedAt == null)
            .ToListAsync(cancellationToken);

        if (activeForGrantee.Any(g => g.NamespaceId == request.NamespaceId && g.PillarKind == request.PillarKind))
        {
            return Result.Failure<GovernanceGrant>(Error.Conflict(
                ErrorCodes.Governance.AlreadyExists,
                $"An active grant already exists for grantee '{request.GranteeIdentity}' at this namespace/pillar scope."));
        }

        var grant = new GovernanceGrant
        {
            OwnerId = request.OwnerId,
            GranteeIdentity = request.GranteeIdentity,
            GranteeKind = request.GranteeKind,
            Role = request.Role,
            NamespaceId = request.NamespaceId,
            PillarKind = request.PillarKind,
            GrantedAt = DateTimeOffset.UtcNow,
            GrantedByIdentity = request.GrantedByIdentity,
        };

        _dbContext.GovernanceGrants.Add(grant);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _auditService.Enqueue(new AuditLog
        {
            Id = Guid.NewGuid(),
            Timestamp = grant.GrantedAt,
            OwnerId = request.OwnerId,
            UserIdentity = request.GrantedByIdentity,
            Action = "Governance.Grant",
            Outcome = "Success",
            NamespaceId = request.NamespaceId,
            ResourceName = request.GranteeIdentity,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                role = request.Role.ToString(),
                pillarKind = request.PillarKind?.ToString(),
                granteeKind = request.GranteeKind.ToString(),
            }),
        });

        _logger.LogInformation(
            "Granted {Role} to {Grantee} (namespace={NamespaceId}, pillar={PillarKind})",
            grant.Role, LogRedactor.SanitiseForLog(grant.GranteeIdentity), grant.NamespaceId, grant.PillarKind);

        return Result.Success(grant);
    }

    /// <inheritdoc/>
    public async Task<Result> RevokeAsync(Guid grantId, string ownerId, string revokedByIdentity, CancellationToken cancellationToken = default)
    {
        var grant = await _dbContext.GovernanceGrants
            .FirstOrDefaultAsync(g => g.Id == grantId && g.OwnerId == ownerId, cancellationToken);

        if (grant is null)
        {
            return Result.Failure(Error.NotFound(
                ErrorCodes.Governance.NotFound, $"Grant with ID '{grantId}' was not found."));
        }

        if (grant.RevokedAt is not null)
        {
            // Idempotent — revoking an already-revoked grant is a no-op success, matching
            // Namespace.RevokeShare's convention elsewhere in this codebase.
            return Result.Success();
        }

        grant.RevokedAt = DateTimeOffset.UtcNow;
        grant.RevokedByIdentity = revokedByIdentity;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _auditService.Enqueue(new AuditLog
        {
            Id = Guid.NewGuid(),
            Timestamp = grant.RevokedAt.Value,
            OwnerId = ownerId,
            UserIdentity = revokedByIdentity,
            Action = "Governance.Revoke",
            Outcome = "Success",
            NamespaceId = grant.NamespaceId,
            ResourceName = grant.GranteeIdentity,
        });

        _logger.LogInformation(
            "Revoked grant {GrantId} for {Grantee}", grantId, LogRedactor.SanitiseForLog(grant.GranteeIdentity));

        return Result.Success();
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<GovernanceGrant>>> GetActiveGrantsAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        var grants = await _dbContext.GovernanceGrants
            .AsNoTracking()
            .Where(g => g.OwnerId == ownerId && g.RevokedAt == null)
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<GovernanceGrant>>(grants);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<GovernanceGrant>>> GetGrantsForGranteeAsync(
        string ownerId, string granteeIdentity, CancellationToken cancellationToken = default)
    {
        var grants = await _dbContext.GovernanceGrants
            .AsNoTracking()
            .Where(g => g.OwnerId == ownerId && g.GranteeIdentity == granteeIdentity && g.RevokedAt == null)
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<GovernanceGrant>>(grants);
    }
}
