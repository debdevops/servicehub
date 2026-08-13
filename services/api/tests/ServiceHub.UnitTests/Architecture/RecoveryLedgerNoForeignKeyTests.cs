using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ServiceHub.Core.Entities;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Architecture;

/// <summary>
/// Enforces the Recovery Evidence Ledger's "no FK" decision (roadmap §6.4/P10): a foreign key
/// from <see cref="RecoveryLedgerEntry"/> to <c>DlqMessages</c> would let a future
/// <c>DlqMessage</c> deletion cascade into evidence loss. <see cref="RecoveryLedgerEntry.DlqMessageId"/>
/// must stay a soft reference forever — this test fails the build if a future migration adds a
/// relationship there, or anywhere else on any of the three ledger entity types.
/// </summary>
public sealed class RecoveryLedgerNoForeignKeyTests
{
    private static IModel BuildModel()
    {
        using var dbContext = new DlqDbContext(
            new DbContextOptionsBuilder<DlqDbContext>().UseSqlite("DataSource=:memory:").Options);
        return dbContext.Model;
    }

    [Fact]
    public void RecoveryLedgerEntry_HasNoForeignKeys()
    {
        var model = BuildModel();
        var entityType = model.FindEntityType(typeof(RecoveryLedgerEntry));

        entityType.Should().NotBeNull();
        entityType!.GetForeignKeys().Should().BeEmpty(
            "RecoveryLedgerEntry.DlqMessageId is a deliberate soft reference — a foreign key " +
            "would let DlqMessage deletion cascade into the ledger and destroy evidence");
    }

    [Fact]
    public void RecoveryOperation_HasNoForeignKeys()
    {
        var model = BuildModel();
        var entityType = model.FindEntityType(typeof(RecoveryOperation));

        entityType.Should().NotBeNull();
        entityType!.GetForeignKeys().Should().BeEmpty(
            "RecoveryOperation has no FK to a namespace or any other mutable table — it snapshots instead");
    }

    [Fact]
    public void RecoveryEvent_HasNoForeignKeys()
    {
        var model = BuildModel();
        var entityType = model.FindEntityType(typeof(RecoveryEvent));

        entityType.Should().NotBeNull();
        entityType!.GetForeignKeys().Should().BeEmpty(
            "RecoveryEvent links to its entry/operation by plain Guid columns, not navigation FKs");
    }

    [Fact]
    public void NoOtherEntityType_HasAForeignKeyIntoTheRecoveryLedger()
    {
        var model = BuildModel();
        var ledgerClrTypes = new[] { typeof(RecoveryOperation), typeof(RecoveryLedgerEntry), typeof(RecoveryEvent) };

        var incomingForeignKeys = model.GetEntityTypes()
            .Where(e => !ledgerClrTypes.Contains(e.ClrType))
            .SelectMany(e => e.GetForeignKeys())
            .Where(fk => ledgerClrTypes.Contains(fk.PrincipalEntityType.ClrType))
            .ToList();

        incomingForeignKeys.Should().BeEmpty(
            "nothing should reference the Recovery Evidence Ledger via a foreign key — the ledger " +
            "must never become a cascade target");
    }
}
