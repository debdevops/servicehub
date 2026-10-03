using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Api.Security;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models.Backup;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Admin-only controller for on-demand backup creation and listing (roadmap F2). Restore is
/// deliberately not exposed here — restore is a manual, operator-driven procedure documented in
/// docs/BACKUP-RESTORE.md, not an automated API call.
/// </summary>
[Route(ApiRoutes.Backup.Base)]
[Tags("Backup")]
public sealed class BackupController : ApiControllerBase
{
    private readonly IBackupService _backupService;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<BackupController> _logger;

    /// <summary>Initializes a new instance of the <see cref="BackupController"/> class.</summary>
    public BackupController(
        IBackupService backupService,
        ILogger<BackupController> logger,
        IAuditLogger? auditLogger = null)
    {
        _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
        _auditLogger = auditLogger ?? NoOpAuditLogger.Instance;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ─── POST /api/v1/admin/backup ────────────────────────────────────────────

    /// <summary>
    /// Creates an on-demand backup bundle: a consistent SQLite snapshot, an integrity check of
    /// that snapshot, an independent copy of the namespace JSON store, and a manifest. Instance-
    /// wide, like the scheduled backup worker — not tenant-scoped.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Backup completed; returns the manifest.</response>
    [HttpPost]
    [RequireScope(ApiKeyScopes.Admin)]
    [ProducesResponseType(typeof(BackupManifest), StatusCodes.Status200OK)]
    public async Task<ActionResult<BackupManifest>> CreateBackup(CancellationToken cancellationToken = default)
    {
        var result = await _backupService.CreateBackupAsync(cancellationToken);

        if (result.IsFailure)
        {
            _auditLogger.LogCriticalAction(
                HttpContext, OwnerId, action: "backup:create", outcome: "Failed",
                detail: result.Error.Message);
            return ToActionResult<BackupManifest>(result.Error);
        }

        _auditLogger.LogCriticalAction(
            HttpContext, OwnerId, action: "backup:create", outcome: "Succeeded",
            detail: $"Created backup {result.Value.BackupId}");

        _logger.LogInformation("On-demand backup {BackupId} created", result.Value.BackupId);

        return Ok(result.Value);
    }

    // ─── GET /api/v1/admin/backup ─────────────────────────────────────────────

    /// <summary>
    /// Lists existing backup bundles, newest first — for DR verification and operator visibility
    /// into scheduled/on-demand backup history.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">List of backup bundles.</response>
    [HttpGet]
    [RequireScope(ApiKeyScopes.Admin)]
    [ProducesResponseType(typeof(IReadOnlyList<BackupSummary>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BackupSummary>>> ListBackups(CancellationToken cancellationToken = default)
    {
        var result = await _backupService.ListBackupsAsync(cancellationToken);
        return ToActionResult<IReadOnlyList<BackupSummary>>(result);
    }
}
