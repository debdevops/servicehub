using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Signatures;

namespace ServiceHub.UnitTests.Infrastructure.Signatures;

/// <summary>Signs a dead letter the way 4.1.0 did (fingerprint version 1, with its tally row), so tests can start from the database an upgrade finds.</summary>
internal static class LegacySigner
{
    public static async Task<string> AssignAsync(ServiceHubDbContext db, DlqMessage message, CancellationToken ct)
    {
        var features = (await new FailureFeatureExtractor().ExtractAsync(message, ct)).Value;
        var fingerprint = (await new FailureFingerprintBuilder().ComputeAsync(features, ct)).Value;
        var hash = fingerprint.Hash;
        var row = db.NamespaceSignatures.Local.FirstOrDefault(s => s.OwnerId == message.OwnerId && s.NamespaceId == message.NamespaceId && s.SignatureHash == hash)
            ?? await db.NamespaceSignatures.FirstOrDefaultAsync(s => s.OwnerId == message.OwnerId && s.NamespaceId == message.NamespaceId && s.SignatureHash == hash, ct);
        if (row is null)
        {
            db.NamespaceSignatures.Add(new NamespaceSignature
            {
                NamespaceId = message.NamespaceId, OwnerId = message.OwnerId, SignatureHash = hash,
                FirstSeenAt = message.DetectedAtUtc, LastSeenAt = message.DetectedAtUtc, OccurrenceCount = 1,
                DominantDeadletterReason = message.DeadLetterReason ?? "Unknown", EntityName = message.EntityName,
                ExampleError = message.DeadLetterErrorDescription, TopTermsJson = JsonSerializer.Serialize(fingerprint.TopTerms),
            });
        }
        else
        {
            row.OccurrenceCount++;
        }

        return hash;
    }
}
