using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Security;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models.Backup;
using ServiceHub.Infrastructure.Backup;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Backup and restore, behind Settings → Backup (unit 6.13). Admin only, instance-wide.
/// </summary>
/// <remarks>
/// <para>Create and list are 4.0.0's, unchanged in meaning. Restore is new: it checks the bundle and <b>stages</b> it for the
/// next start — the running database is never swapped (see <see cref="BackupRestoreService"/>).</para>
/// <para>A backup does not contain the encryption key, only its fingerprint. The key must be kept somewhere else — a backup
/// that carried its own key would hand anyone who got the file every saved connection.</para>
/// </remarks>
[Route("api/v1/admin/backup")]
public sealed class BackupController : ApiControllerBase
{
    private readonly IBackupService _backups;
    private readonly IBackupRestore _restore;
    private readonly BackupRestoreService _bundles;
    private readonly IAuditTrail _audit;

    /// <summary>Creates the controller.</summary>
    public BackupController(IBackupService backups, IBackupRestore restore, BackupRestoreService bundles, IAuditTrail audit)
    {
        _backups = backups ?? throw new ArgumentNullException(nameof(backups));
        _restore = restore ?? throw new ArgumentNullException(nameof(restore));
        _bundles = bundles ?? throw new ArgumentNullException(nameof(bundles));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    /// <summary>Existing backups, newest first, and any restore waiting for the next start.</summary>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (await DeniedUnlessAsync(GovernanceRole.Admin, null, null, "see backups", cancellationToken) is { } denied) return denied;
        var list = await _backups.ListBackupsAsync(cancellationToken);
        return list.IsFailure
            ? Problem(StatusCodes.Status500InternalServerError, list.Error.Code, "ServiceHub couldn't read its backups folder.")
            : Ok(new { backups = list.Value, pending = _restore.Pending() });
    }

    /// <summary>Takes a backup now: a consistent snapshot, checked, with a manifest.</summary>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpPost]
    [ProducesResponseType(typeof(BackupManifest), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        if (!IntentHeaders.Declares(Request, IntentHeaders.CreateBackup))
        {
            return Problem(StatusCodes.Status428PreconditionRequired, ErrorCodes.IntentRequired, IntentHeaders.MissingDetail("take a backup", IntentHeaders.CreateBackup));
        }

        if (await DeniedUnlessAsync(GovernanceRole.Admin, null, null, "take a backup", cancellationToken) is { } denied) return denied;

        var result = await _backups.CreateBackupAsync(cancellationToken);
        await AuditAsync(AuditActions.BackupCreate, result.IsSuccess, result.IsSuccess ? result.Value.BackupId : null, cancellationToken);
        return result.IsFailure
            ? Problem(StatusCodes.Status500InternalServerError, result.Error.Code, "The backup did not complete. Nothing was kept.")
            : Ok(result.Value);
    }

    /// <summary>Downloads a backup's database file, to keep somewhere other than this disk.</summary>
    /// <param name="id">The backup id.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpGet("{id}/download")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(string id, CancellationToken cancellationToken)
    {
        if (await DeniedUnlessAsync(GovernanceRole.Admin, null, null, "download a backup", cancellationToken) is { } denied) return denied;
        var dir = _bundles.BundlePath(id);
        var file = dir is null ? null : Directory.EnumerateFiles(dir, "*.db").FirstOrDefault();
        return file is null
            ? Problem(StatusCodes.Status404NotFound, ErrorCodes.NotFound, $"There is no backup called '{id}'.")
            : PhysicalFile(file, "application/vnd.sqlite3", $"servicehub-backup-{id}.db");
    }

    /// <summary>Checks whether a backup could be restored, and says why not. Changes nothing.</summary>
    /// <param name="id">The backup id.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpGet("{id}/check")]
    [ProducesResponseType(typeof(RestoreCheck), StatusCodes.Status200OK)]
    public async Task<IActionResult> Check(string id, CancellationToken cancellationToken)
    {
        if (await DeniedUnlessAsync(GovernanceRole.Admin, null, null, "check a backup", cancellationToken) is { } denied) return denied;
        return Ok(await _restore.CheckAsync(id, cancellationToken));
    }

    /// <summary>What a restore request carries: the typed word, so it cannot be a slip.</summary>
    /// <param name="Confirm">Must be <c>RESTORE</c>.</param>
    public sealed record RestoreRequest(string? Confirm);

    /// <summary>
    /// Stages a backup to replace the database at the next start. Refused — with the failed checks — unless every check passes.
    /// </summary>
    /// <param name="id">The backup id.</param>
    /// <param name="request">The typed confirmation.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpPost("{id}/restore")]
    [ProducesResponseType(typeof(RestoreCheck), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RestoreCheck), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(string id, [FromBody] RestoreRequest request, CancellationToken cancellationToken)
    {
        if (!IntentHeaders.Declares(Request, IntentHeaders.RestoreBackup))
        {
            return Problem(StatusCodes.Status428PreconditionRequired, ErrorCodes.IntentRequired, IntentHeaders.MissingDetail("restore a backup", IntentHeaders.RestoreBackup));
        }

        if (!string.Equals(request?.Confirm, "RESTORE", StringComparison.Ordinal))
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "Type RESTORE to confirm. Everything recorded since this backup will be replaced at the next start.");
        }

        if (await DeniedUnlessAsync(GovernanceRole.Admin, null, null, "restore a backup", cancellationToken) is { } denied) return denied;

        var check = await _restore.StageAsync(id, cancellationToken);
        await AuditAsync(AuditActions.BackupRestore, check.CanRestore, id, cancellationToken);
        return check.CanRestore ? Ok(check) : Conflict(check);
    }

    /// <summary>Unstages a restore waiting for the next start.</summary>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpDelete("pending")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelPending(CancellationToken cancellationToken)
    {
        if (!IntentHeaders.Declares(Request, IntentHeaders.RestoreBackup))
        {
            return Problem(StatusCodes.Status428PreconditionRequired, ErrorCodes.IntentRequired, IntentHeaders.MissingDetail("cancel the restore", IntentHeaders.RestoreBackup));
        }

        if (await DeniedUnlessAsync(GovernanceRole.Admin, null, null, "cancel a restore", cancellationToken) is { } denied) return denied;
        var pending = _restore.Pending();
        if (!_restore.CancelPending()) return Problem(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "No restore is waiting.");
        await AuditAsync(AuditActions.BackupRestore, true, $"cancelled {pending?.BackupId}", cancellationToken);
        return NoContent();
    }

    private Task AuditAsync(string action, bool ok, string? resource, CancellationToken cancellationToken) =>
        _audit.RecordAsync(new AuditLog
        {
            Id = Guid.NewGuid(), Timestamp = DateTimeOffset.UtcNow, OwnerId = OwnerId, UserIdentity = Actor.Identity,
            Action = action, Outcome = ok ? AuditActions.Success : AuditActions.Failure, ResourceName = resource,
            CorrelationId = HttpContext.TraceIdentifier, HttpMethod = Request.Method, HttpPath = Request.Path.Value,
        }, cancellationToken);
}
