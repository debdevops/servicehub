using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ServiceHub.Core.Entities;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Architecture;

/// <summary>
/// Enforces the Playbook Ledger's "no FK" decision — the same architectural invariant
/// <see cref="RecoveryLedgerNoForeignKeyTests"/> enforces for the Recovery Evidence Ledger, applied
/// independently to <see cref="PlaybookEntry"/>/<see cref="PlaybookEvent"/>. In particular, this
/// ledger must never gain an FK to or from any Recovery ledger table — the two chains stay fully
/// independent by construction.
/// </summary>
public sealed class PlaybookLedgerNoForeignKeyTests
{
    private static IModel BuildModel()
    {
        using var dbContext = new DlqDbContext(
            new DbContextOptionsBuilder<DlqDbContext>().UseSqlite("DataSource=:memory:").Options);
        return dbContext.Model;
    }

    [Fact]
    public void PlaybookEntry_HasNoForeignKeys()
    {
        var model = BuildModel();
        var entityType = model.FindEntityType(typeof(PlaybookEntry));

        entityType.Should().NotBeNull();
        entityType!.GetForeignKeys().Should().BeEmpty(
            "PlaybookEntry.SignatureHashSnapshot/NamespaceId/RelatedRecoveryOperationId are all " +
            "deliberate soft references — a foreign key would let unrelated deletions cascade " +
            "into this reasoning trail");
    }

    [Fact]
    public void PlaybookEvent_HasNoForeignKeys()
    {
        var model = BuildModel();
        var entityType = model.FindEntityType(typeof(PlaybookEvent));

        entityType.Should().NotBeNull();
        entityType!.GetForeignKeys().Should().BeEmpty(
            "PlaybookEvent links to its entry by a plain Guid column, not a navigation FK");
    }

    [Fact]
    public void NoOtherEntityType_HasAForeignKeyIntoThePlaybookLedger()
    {
        var model = BuildModel();
        var playbookClrTypes = new[] { typeof(PlaybookEntry), typeof(PlaybookEvent) };

        var incomingForeignKeys = model.GetEntityTypes()
            .Where(e => !playbookClrTypes.Contains(e.ClrType))
            .SelectMany(e => e.GetForeignKeys())
            .Where(fk => playbookClrTypes.Contains(fk.PrincipalEntityType.ClrType))
            .ToList();

        incomingForeignKeys.Should().BeEmpty(
            "nothing should reference the Playbook Ledger via a foreign key — it must never " +
            "become a cascade target, same as the Recovery Evidence Ledger");
    }

    [Fact]
    public void PlaybookLedger_HasNoForeignKeyToOrFromTheRecoveryLedger()
    {
        var model = BuildModel();
        var playbookClrTypes = new[] { typeof(PlaybookEntry), typeof(PlaybookEvent) };
        var recoveryClrTypes = new[] { typeof(RecoveryOperation), typeof(RecoveryLedgerEntry), typeof(RecoveryEvent) };

        var playbookToRecovery = model.GetEntityTypes()
            .Where(e => playbookClrTypes.Contains(e.ClrType))
            .SelectMany(e => e.GetForeignKeys())
            .Where(fk => recoveryClrTypes.Contains(fk.PrincipalEntityType.ClrType));

        var recoveryToPlaybook = model.GetEntityTypes()
            .Where(e => recoveryClrTypes.Contains(e.ClrType))
            .SelectMany(e => e.GetForeignKeys())
            .Where(fk => playbookClrTypes.Contains(fk.PrincipalEntityType.ClrType));

        playbookToRecovery.Concat(recoveryToPlaybook).Should().BeEmpty(
            "the two ledgers must stay structurally independent in both directions — " +
            "PlaybookEntry.RelatedRecoveryOperationId is a soft reference only, never an FK");
    }
}
