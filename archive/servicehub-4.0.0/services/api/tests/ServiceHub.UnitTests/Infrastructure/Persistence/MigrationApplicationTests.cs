using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure.Persistence;

/// <summary>
/// Establishes the "apply migration, verify shape" / "apply, then Down, verify prior shape"
/// pattern the M1-M4 persistence wave's testing strategy requires — no test of this shape existed
/// against any of the 12 pre-wave migrations before M1's <c>AddAutoReplayRuleNamespaceId</c>.
/// </summary>
public sealed class MigrationApplicationTests : IDisposable
{
    private const string PriorMigration = "20260818183029_AddAutoReplayRuleDisabledReason";
    private const string M1Migration = "20260829061051_AddAutoReplayRuleNamespaceId";
    private const string M2Migration = "20260829065237_AddNamespacesTable";
    private const string M3Migration = "20260829101538_AddGovernanceGrants";

    private readonly SqliteConnection _connection;
    private readonly DlqDbContext _dbContext;

    public MigrationApplicationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite(_connection)
            .Options;
        _dbContext = new DlqDbContext(options);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    private List<string> GetColumnNames(string table)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var reader = cmd.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }
        return columns;
    }

    private List<string> GetIndexNames(string table)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"PRAGMA index_list({table})";
        using var reader = cmd.ExecuteReader();
        var indexes = new List<string>();
        while (reader.Read())
        {
            indexes.Add(reader.GetString(1));
        }
        return indexes;
    }

    [Fact]
    public void Migrate_ToLatest_AddsAutoReplayRuleNamespaceIdColumnAndIndex()
    {
        _dbContext.Database.Migrate();

        GetColumnNames("AutoReplayRules").Should().Contain("NamespaceId");
        GetIndexNames("AutoReplayRules").Should().Contain("IX_AutoReplayRules_OwnerId_NamespaceId");
    }

    [Fact]
    public void Migrate_UpThenDown_RemovesNamespaceIdColumnAndIndex_RestoringPriorShape()
    {
        _dbContext.Database.Migrate();
        GetColumnNames("AutoReplayRules").Should().Contain("NamespaceId");

        var migrator = _dbContext.Database.GetService<IMigrator>();
        migrator.Migrate(PriorMigration);

        GetColumnNames("AutoReplayRules").Should().NotContain("NamespaceId");
        GetIndexNames("AutoReplayRules").Should().NotContain("IX_AutoReplayRules_OwnerId_NamespaceId");
    }

    [Fact]
    public void Migrate_ToLatest_CreatesNamespacesAndNamespaceSharedOwnersTables()
    {
        _dbContext.Database.Migrate();

        var namespaceColumns = GetColumnNames("Namespaces");
        namespaceColumns.Should().Contain([
            "Id", "Name", "DisplayName", "Description", "ConnectionStringEncrypted",
            "AuthType", "IsActive", "Environment", "Provider", "OwnerId", "ConnectionStringHash",
        ]);
        // Never mapped as a real column — hydrated at the repository layer from NamespaceSharedOwners.
        namespaceColumns.Should().NotContain("SharedWithOwnerIds");

        GetIndexNames("Namespaces").Should().Contain([
            "IX_Namespaces_OwnerId_Name", "IX_Namespaces_OwnerId", "IX_Namespaces_IsActive",
        ]);

        GetColumnNames("NamespaceSharedOwners").Should().Contain(["NamespaceId", "OwnerId"]);
        GetIndexNames("NamespaceSharedOwners").Should().Contain("IX_NamespaceSharedOwners_OwnerId");
    }

    [Fact]
    public void Migrate_UpThenDown_RemovesNamespaceTables_RestoringPriorShape()
    {
        _dbContext.Database.Migrate();
        GetColumnNames("Namespaces").Should().NotBeEmpty();

        var migrator = _dbContext.Database.GetService<IMigrator>();
        migrator.Migrate(M1Migration);

        GetColumnNames("Namespaces").Should().BeEmpty();
        GetColumnNames("NamespaceSharedOwners").Should().BeEmpty();
    }

    [Fact]
    public void Migrate_ToLatest_CreatesGovernanceGrantsTable()
    {
        _dbContext.Database.Migrate();

        var columns = GetColumnNames("GovernanceGrants");
        columns.Should().Contain([
            "Id", "OwnerId", "GranteeIdentity", "GranteeKind", "Role", "NamespaceId", "PillarKind",
            "GrantedAt", "GrantedByIdentity", "RevokedAt", "RevokedByIdentity",
        ]);

        GetIndexNames("GovernanceGrants").Should().Contain([
            "IX_GovernanceGrants_OwnerId_GranteeIdentity",
            "IX_GovernanceGrants_OwnerId_NamespaceId",
            "IX_GovernanceGrants_ActiveScope_Unique",
        ]);
    }

    [Fact]
    public void Migrate_UpThenDown_RemovesGovernanceGrantsTable_RestoringPriorShape()
    {
        _dbContext.Database.Migrate();
        GetColumnNames("GovernanceGrants").Should().NotBeEmpty();

        var migrator = _dbContext.Database.GetService<IMigrator>();
        migrator.Migrate(M2Migration);

        GetColumnNames("GovernanceGrants").Should().BeEmpty();
    }

    [Fact]
    public void Migrate_ToLatest_CreatesPlaybookEntriesAndPlaybookEventsTables()
    {
        _dbContext.Database.Migrate();

        var entryColumns = GetColumnNames("PlaybookEntries");
        entryColumns.Should().Contain([
            "Id", "OwnerId", "PillarKind", "ProposalKind", "EvidenceRefJson", "ProposalJson",
            "ProposedAt", "ProposerIdentity", "ProposerKind", "ExpiresAt", "State", "Disposition",
            "LastEventSeq", "ClosedAt",
        ]);

        GetIndexNames("PlaybookEntries").Should().Contain([
            "IX_PlaybookEntries_OwnerId_State",
            "IX_PlaybookEntries_OwnerId_PillarKind",
            "IX_PlaybookEntries_OwnerId_NamespaceId",
        ]);

        var eventColumns = GetColumnNames("PlaybookEvents");
        eventColumns.Should().Contain([
            "Id", "OwnerId", "Seq", "EntryId", "EventType", "OccurredAt", "ActorIdentity",
            "ActorKind", "DetailJson", "PrevHash", "EntryHash", "SchemaVersion",
        ]);

        GetIndexNames("PlaybookEvents").Should().Contain([
            "IX_PlaybookEvents_OwnerId_Seq", "IX_PlaybookEvents_EntryId_Seq",
        ]);
    }

    [Fact]
    public void Migrate_UpThenDown_RemovesPlaybookLedgerTables_RestoringPriorShape()
    {
        _dbContext.Database.Migrate();
        GetColumnNames("PlaybookEntries").Should().NotBeEmpty();

        var migrator = _dbContext.Database.GetService<IMigrator>();
        migrator.Migrate(M3Migration);

        GetColumnNames("PlaybookEntries").Should().BeEmpty();
        GetColumnNames("PlaybookEvents").Should().BeEmpty();
    }
}
