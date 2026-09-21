using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Infrastructure.Persistence.InMemory;

namespace ServiceHub.Infrastructure.Persistence;

/// <summary>
/// One-shot, forward-only cutover of the namespace store from <c>servicehub-namespaces.json</c> to
/// the SQLite <c>Namespaces</c>/<c>NamespaceSharedOwners</c> tables (M2 of the persistence wave —
/// see <c>PERSISTENCE-EVOLUTION-DESIGN-2026-08-29.md</c> §5/§6/§9/§10). Invoked once from
/// <c>Program.cs</c>, immediately after <c>Database.MigrateAsync()</c> succeeds. No dual-write, no
/// read-through fallback: once this succeeds, the JSON file is renamed to <c>.migrated</c> (never
/// deleted) and <see cref="SqliteNamespaceRepository"/> is the namespace store for the rest of this
/// and every future process lifetime.
/// </summary>
public static class NamespaceStoreImporter
{
    /// <summary>
    /// Idempotent: skips entirely if <c>Namespaces</c> is already non-empty, and skips entirely
    /// (fresh install) if no JSON file exists. Throws on any failure — the row-count parity gate
    /// and any per-entry rehydration failure are both hard, fail-closed errors, matching the design
    /// doc's §10 instruction for the one step in this wave that moves real data. A caller in
    /// production (<c>Program.cs</c>) lets this propagate so the app fails to start rather than run
    /// against a partially-imported store.
    /// </summary>
    public static async Task ImportIfPresentAsync(DlqDbContext dbContext, IConfiguration configuration, ILogger logger)
    {
        if (await dbContext.Namespaces.AnyAsync())
        {
            // Already imported (or a fresh SQLite-native install with namespaces created directly)
            // — never re-import, per the design's forward-only, run-once contract.
            return;
        }

        var storagePath = ResolveStoragePath(configuration);
        if (!File.Exists(storagePath))
        {
            // Fresh install — nothing to import. Namespaces simply starts empty.
            return;
        }

        logger.LogInformation("Importing namespace store from {Path} into SQLite (one-shot, M2)", storagePath);

        var json = await File.ReadAllTextAsync(storagePath);
        var snapshots = JsonSerializer.Deserialize<List<NamespaceJsonSnapshot.Entry>>(json, NamespaceJsonSnapshot.JsonOptions) ?? [];

        var rehydrated = new List<Namespace>(snapshots.Count);
        foreach (var snapshot in snapshots)
        {
            var ns = NamespaceJsonSnapshot.Rehydrate(snapshot, logger);
            if (ns is null)
            {
                // Fail-closed: this is the one gate in the wave with real data at stake (§10). A
                // snapshot that no longer validates must stop the whole import, not silently drop
                // one namespace — the source JSON stays untouched (not renamed) and the app does
                // not start, so an operator can inspect and fix the file by hand before retrying.
                throw new InvalidOperationException(
                    $"Namespace store import aborted: entry '{snapshot.Name}' failed to validate while rehydrating. " +
                    "The source file was left in place. Inspect and fix servicehub-namespaces.json, then restart.");
            }

            rehydrated.Add(ns);
        }

        var expectedShareRows = snapshots.Sum(s => s.SharedWithOwnerIds?.Count ?? 0);

        dbContext.Namespaces.AddRange(rehydrated);
        var shareRows = rehydrated
            .SelectMany(ns => ns.SharedWithOwnerIds.Select(ownerId => new NamespaceSharedOwner { NamespaceId = ns.Id, OwnerId = ownerId }))
            .ToList();
        dbContext.NamespaceSharedOwners.AddRange(shareRows);

        // Row-count parity gate (§10) — a hard failure, checked before anything is committed or
        // the source file is touched. Structurally guaranteed to hold given the construction
        // above, but asserted explicitly anyway: this is the one step in the wave that moves real
        // data, and the design's own instruction is that this gate must be fail-closed, not
        // assumed correct.
        if (rehydrated.Count != snapshots.Count || shareRows.Count != expectedShareRows)
        {
            throw new InvalidOperationException(
                $"Namespace store import aborted: row-count parity check failed " +
                $"(namespaces {rehydrated.Count}/{snapshots.Count}, shared-owner rows {shareRows.Count}/{expectedShareRows}). " +
                "The source file was left in place.");
        }

        await dbContext.SaveChangesAsync();

        // Only after a verified-successful commit: rename the source, never delete, so a bad
        // import is always recoverable by hand from the untouched original.
        File.Move(storagePath, storagePath + ".migrated");

        logger.LogInformation(
            "Namespace store import complete: {NamespaceCount} namespace(s), {ShareCount} shared-owner row(s). " +
            "Source file renamed to {MigratedPath}",
            rehydrated.Count, shareRows.Count, storagePath + ".migrated");
    }

    /// <summary>
    /// Mirrors <see cref="InMemoryNamespaceRepository"/>'s own <c>NamespaceRepository:DataDirectory</c>
    /// resolution (including its path-traversal guard) exactly, so this finds the same file that
    /// repository has been reading/writing all along.
    /// </summary>
    private static string ResolveStoragePath(IConfiguration configuration)
    {
        var rawDataDir = configuration["NamespaceRepository:DataDirectory"]
            ?? Path.Combine(AppContext.BaseDirectory, "data");
        var resolvedDataDir = Path.GetFullPath(rawDataDir);
        var appBaseDir = Path.GetFullPath(AppContext.BaseDirectory);
        if (!resolvedDataDir.StartsWith(appBaseDir, StringComparison.OrdinalIgnoreCase)
            && !resolvedDataDir.StartsWith("/home", StringComparison.OrdinalIgnoreCase)
            && !resolvedDataDir.StartsWith("/var", StringComparison.OrdinalIgnoreCase)
            && !resolvedDataDir.StartsWith("/opt", StringComparison.OrdinalIgnoreCase)
            && !resolvedDataDir.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
        {
            resolvedDataDir = Path.Combine(appBaseDir, "data");
        }

        return Path.Combine(resolvedDataDir, "servicehub-namespaces.json");
    }
}
