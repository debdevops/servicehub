using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.Agents;

/// <summary>
/// Takes a backup on a schedule (unit 6.13) — 4.0.0's <c>BackupWorker</c> as an agent, so it is listed, pausable and watched
/// like the others. Registered only when <c>Backup:ScheduledBackupIntervalHours</c> is above 0; off by default.
/// </summary>
public sealed class BackupAgent : IAgent
{
    private readonly IServiceScopeFactory _scopes;

    /// <summary>Creates the agent.</summary>
    public BackupAgent(IServiceScopeFactory scopes, IOptions<BackupOptions> options)
    {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        var hours = Math.Clamp((options ?? throw new ArgumentNullException(nameof(options))).Value.ScheduledBackupIntervalHours, 1, 24 * 30);
        Descriptor = new AgentDescriptor(
            Id: "backup",
            Name: "Backup Keeper",
            Purpose: "Takes a checked backup of ServiceHub's database on a schedule, and keeps the newest ones.",
            Kind: AgentKind.Maintain,
            Authority: AgentAuthority.Observes,
            Cadence: TimeSpan.FromHours(hours),
            Notes: $"Every {hours} h, keeping the newest {options.Value.RetentionCount}. The encryption key is never in a backup — keep it somewhere else.",
            May: ["Write a backup into the data directory", "Delete backups older than the newest ones it keeps"],
            MayNot: ["Restore anything — only a person can, from Settings", "Touch any cloud or message"]);
    }

    /// <inheritdoc />
    public AgentDescriptor Descriptor { get; }

    /// <inheritdoc />
    public async Task<AgentCycleResult> ExecuteCycleAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IBackupService>().CreateBackupAsync(ct).ConfigureAwait(false);
        return result.IsFailure
            ? throw new InvalidOperationException("The scheduled backup did not complete.")
            : new AgentCycleResult(1, 1, $"backup {result.Value.BackupId} taken");
    }
}
