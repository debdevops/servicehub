using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ServiceHub.Core.Enums;

namespace ServiceHub.Infrastructure.PlaybookLedger;

/// <summary>
/// Computes the Playbook Ledger's hash chain — structurally identical algorithm to
/// <c>RecoveryHashChain</c> (SHA-256 over a fixed-order canonical join, ending in <c>PrevHash</c>),
/// implemented as a fully independent static class so the two chains stay cryptographically and
/// structurally separate: no shared code path, no shared <c>Seq</c> space, no possibility of one
/// chain's verification accidentally validating the other's data.
/// </summary>
public static class PlaybookHashChain
{
    public static readonly string GenesisHash = new('0', 64);

    public static string ComputeEntryHash(
        Guid id,
        string ownerId,
        long seq,
        Guid entryId,
        PlaybookEventType eventType,
        DateTimeOffset occurredAt,
        string actorIdentity,
        PlaybookActorKind actorKind,
        string? detailJson,
        int schemaVersion,
        string prevHash)
    {
        var canonical = string.Join(
            '|',
            id.ToString("D"),
            ownerId,
            seq.ToString(CultureInfo.InvariantCulture),
            entryId.ToString("D"),
            eventType.ToString(),
            occurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            actorIdentity,
            actorKind.ToString(),
            detailJson ?? string.Empty,
            schemaVersion.ToString(CultureInfo.InvariantCulture),
            prevHash);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(bytes);
    }
}
