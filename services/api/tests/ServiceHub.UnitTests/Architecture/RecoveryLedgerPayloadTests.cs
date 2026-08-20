using System.Reflection;
using FluentAssertions;
using ServiceHub.Core.Entities;

namespace ServiceHub.UnitTests.Architecture;

/// <summary>
/// Enforces the Recovery Evidence Ledger's payload rule (roadmap §5.6): no message body, body
/// preview, connection string, or credential may ever enter a ledger table. Reflects over the
/// three ledger entity types' public properties and fails the build if a future property lands
/// with a name suggesting it carries one of those — the same allow-list-by-substring style as
/// <c>ScopeConformanceTests</c>.
/// </summary>
public sealed class RecoveryLedgerPayloadTests
{
    private static readonly string[] ForbiddenSubstrings = ["Body", "Payload", "Preview", "Connection", "Secret", "Credential", "Password"];

    // BodyHash is a SHA-256 digest, not a body — explicitly allowed despite containing "Body".
    private static readonly HashSet<string> AllowedExceptions = new()
    {
        nameof(RecoveryLedgerEntry.BodyHash),
    };

    public static IEnumerable<object[]> LedgerEntityTypes()
    {
        yield return [typeof(RecoveryOperation)];
        yield return [typeof(RecoveryLedgerEntry)];
        yield return [typeof(RecoveryEvent)];
    }

    [Theory]
    [MemberData(nameof(LedgerEntityTypes))]
    public void LedgerEntity_HasNoPropertyNameSuggestingPayloadOrSecretContent(Type entityType)
    {
        var offending = entityType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => !AllowedExceptions.Contains(p.Name))
            .Where(p => ForbiddenSubstrings.Any(s => p.Name.Contains(s, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name)
            .ToList();

        offending.Should().BeEmpty(
            $"the Recovery Evidence Ledger must never store a message body, preview, connection " +
            $"string, or credential — {entityType.Name} has a property whose name suggests it does");
    }
}
