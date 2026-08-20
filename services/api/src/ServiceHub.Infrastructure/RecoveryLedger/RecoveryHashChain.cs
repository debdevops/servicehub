using System.Security.Cryptography;
using System.Text;
using ServiceHub.Core.Enums;

namespace ServiceHub.Infrastructure.RecoveryLedger;

/// <summary>
/// The canonical hashing rule for the Recovery Evidence Ledger's per-owner hash chain (roadmap
/// §5.5): <c>EntryHash = SHA256(canonical(fields excluding EntryHash) || PrevHash)</c>. Shared by
/// <see cref="RecoveryLedgerService"/> (computing a new event's hash on append) and
/// <see cref="RecoveryChainVerifier"/> (recomputing to verify), so both always agree on the exact
/// same canonical form.
/// </summary>
public static class RecoveryHashChain
{
    /// <summary>The <see cref="ServiceHub.Core.Entities.RecoveryEvent.PrevHash"/> value for the
    /// first event in an owner's chain — 64 zeros, the same length as a SHA-256 hex digest.</summary>
    public static readonly string GenesisHash = new('0', 64);

    /// <summary>
    /// Computes the SHA-256 hex digest for one event: a fixed-order, delimiter-joined
    /// concatenation of every hash-chain field except the hash itself, followed by
    /// <paramref name="prevHash"/>.
    /// </summary>
    public static string ComputeEntryHash(
        Guid id,
        string ownerId,
        long seq,
        Guid? entryId,
        Guid operationId,
        RecoveryEventType eventType,
        DateTimeOffset occurredAt,
        string actorIdentity,
        RecoveryActorKind actorKind,
        string? detailJson,
        int schemaVersion,
        string prevHash)
    {
        var canonical = string.Join(
            '|',
            id.ToString("D"),
            ownerId,
            seq.ToString(System.Globalization.CultureInfo.InvariantCulture),
            entryId?.ToString("D") ?? string.Empty,
            operationId.ToString("D"),
            eventType.ToString(),
            occurredAt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            actorIdentity,
            actorKind.ToString(),
            detailJson ?? string.Empty,
            schemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            prevHash);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(bytes);
    }
}
