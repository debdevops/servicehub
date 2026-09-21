using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure;

/// <summary>
/// Proves the roadmap next-chapter M5.4 configuration-as-code round trip end to end, and — just
/// as important — proves what it deliberately never touches: a namespace's connection string
/// never appears in an export, and importing never creates or modifies a namespace.
/// </summary>
public sealed class ConfigurationExportServiceTests : IDisposable
{
    private const string OwnerA = "config-owner-a";
    private const string OwnerB = "config-owner-b";

    private readonly DlqDbContext _dbContext;
    private readonly SqliteNamespaceRepository _namespaceRepository;
    private readonly GovernanceGrantService _governanceGrantService;
    private readonly ConfigurationExportService _sut;

    public ConfigurationExportServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _namespaceRepository = new SqliteNamespaceRepository(_dbContext, NullLogger<SqliteNamespaceRepository>.Instance);
        _governanceGrantService = new GovernanceGrantService(
            _dbContext, Mock.Of<IAuditService>(), NullLogger<GovernanceGrantService>.Instance);
        _sut = new ConfigurationExportService(_dbContext, _namespaceRepository, _governanceGrantService);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private static RecoveryActor Actor() => new("test-admin", RecoveryActorKind.User);

    private async Task<Namespace> AddNamespaceAsync(string ownerId, string name)
    {
        var ns = Namespace.Create(
            name,
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            displayName: name,
            ownerId: ownerId).Value;
        await _namespaceRepository.AddAsync(ns);
        return ns;
    }

    private async Task<AutoReplayRule> AddRuleAsync(string ownerId, string name, Guid? namespaceId = null)
    {
        var rule = new AutoReplayRule
        {
            Name = name,
            OwnerId = ownerId,
            Description = "seed rule",
            NamespaceId = namespaceId,
            Enabled = true,
            ConditionsJson = """[{"field":"failureCategory","operator":"equals","value":"Timeout"}]""",
            ActionsJson = """{"type":"Replay"}""",
            CreatedAt = DateTimeOffset.UtcNow,
            MaxReplaysPerHour = 50,
        };
        _dbContext.AutoReplayRules.Add(rule);
        await _dbContext.SaveChangesAsync();
        return rule;
    }

    private static ConfigurationBundle StripRules(ConfigurationBundle bundle, params AutoReplayRuleConfig[] rules) =>
        bundle with { AutoReplayRules = rules };

    private static ConfigurationBundle StripGrants(ConfigurationBundle bundle, params GovernanceGrantConfig[] grants) =>
        bundle with { GovernanceGrants = grants };

    [Fact]
    public async Task ExportAsync_NoDataForOwner_ReturnsEmptyBundle()
    {
        var bundle = await _sut.ExportAsync(OwnerA);

        bundle.Namespaces.Should().BeEmpty();
        bundle.AutoReplayRules.Should().BeEmpty();
        bundle.GovernanceGrants.Should().BeEmpty();
    }

    [Fact]
    public async Task ExportAsync_NeverIncludesConnectionStringOrCredentialFields()
    {
        await AddNamespaceAsync(OwnerA, "orders-ns");

        var bundle = await _sut.ExportAsync(OwnerA);
        var json = JsonSerializer.Serialize(bundle);

        bundle.Namespaces.Should().ContainSingle();
        json.Should().NotContain("ConnectionString", "a namespace's connection string is a credential, never configuration-as-code");
        json.Should().NotContain("SharedAccessKey");
    }

    [Fact]
    public async Task ExportAsync_ScopesEverythingToOwner()
    {
        await AddNamespaceAsync(OwnerA, "owner-a-ns");
        await AddNamespaceAsync(OwnerB, "owner-b-ns");
        await AddRuleAsync(OwnerA, "owner-a-rule");
        await AddRuleAsync(OwnerB, "owner-b-rule");
        await _governanceGrantService.GrantAsync(new GrantRoleRequest(
            OwnerA, "alex@contoso.com", GranteeKind.User, GovernanceRole.Operator, null, null, "admin@contoso.com"));
        await _governanceGrantService.GrantAsync(new GrantRoleRequest(
            OwnerB, "sam@contoso.com", GranteeKind.User, GovernanceRole.Operator, null, null, "admin@contoso.com"));

        var bundle = await _sut.ExportAsync(OwnerA);

        bundle.Namespaces.Should().ContainSingle().Which.Name.Should().Be("owner-a-ns");
        bundle.AutoReplayRules.Should().ContainSingle().Which.Name.Should().Be("owner-a-rule");
        bundle.GovernanceGrants.Should().ContainSingle().Which.GranteeIdentity.Should().Be("alex@contoso.com");
    }

    [Fact]
    public async Task ImportAsync_OwnerMismatch_Fails()
    {
        var bundle = new ConfigurationBundle(OwnerB, DateTimeOffset.UtcNow, [], [], []);

        var result = await _sut.ImportAsync(OwnerA, bundle, Actor());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ConfigurationImport.OwnerMismatch");
    }

    [Fact]
    public async Task ImportAsync_NewRule_IsCreated()
    {
        var bundle = new ConfigurationBundle(OwnerA, DateTimeOffset.UtcNow, [], [
            new AutoReplayRuleConfig(
                "new-rule", "desc", null, true, 25,
                JsonSerializer.SerializeToElement(new[] { new { field = "x", operatorName = "equals", value = "y" } }),
                JsonSerializer.SerializeToElement(new { type = "Replay" })),
        ], []);

        var result = await _sut.ImportAsync(OwnerA, bundle, Actor());

        result.IsSuccess.Should().BeTrue();
        result.Value.RulesCreated.Should().Be(1);
        result.Value.RulesUpdated.Should().Be(0);
        (await _dbContext.AutoReplayRules.CountAsync(r => r.OwnerId == OwnerA && r.Name == "new-rule")).Should().Be(1);
    }

    [Fact]
    public async Task ImportAsync_ExistingRuleUnchanged_CountsAsUnchanged()
    {
        await AddRuleAsync(OwnerA, "stable-rule");
        var exported = await _sut.ExportAsync(OwnerA);

        var result = await _sut.ImportAsync(OwnerA, exported, Actor());

        result.IsSuccess.Should().BeTrue();
        result.Value.RulesUnchanged.Should().Be(1);
        result.Value.RulesCreated.Should().Be(0);
        result.Value.RulesUpdated.Should().Be(0);
    }

    [Fact]
    public async Task ImportAsync_ExistingRuleEdited_UpdatesInPlaceRatherThanDuplicating()
    {
        await AddRuleAsync(OwnerA, "editable-rule");
        var exported = await _sut.ExportAsync(OwnerA);
        var edited = StripRules(exported, exported.AutoReplayRules[0] with { Enabled = false, Description = "edited via git" });

        var result = await _sut.ImportAsync(OwnerA, edited, Actor());

        result.IsSuccess.Should().BeTrue();
        result.Value.RulesUpdated.Should().Be(1);
        result.Value.RulesCreated.Should().Be(0);

        var rows = await _dbContext.AutoReplayRules.Where(r => r.OwnerId == OwnerA).ToListAsync();
        rows.Should().ContainSingle("editing must never duplicate — it must match by name and update in place");
        rows[0].Enabled.Should().BeFalse();
        rows[0].Description.Should().Be("edited via git");
    }

    [Fact]
    public async Task ImportAsync_RuleReferencingUnknownNamespace_IsSkippedWithWarning()
    {
        var bundle = new ConfigurationBundle(OwnerA, DateTimeOffset.UtcNow, [], [
            new AutoReplayRuleConfig(
                "dangling-rule", null, Guid.NewGuid(), true, 10,
                JsonSerializer.SerializeToElement(Array.Empty<object>()),
                JsonSerializer.SerializeToElement(new { type = "Replay" })),
        ], []);

        var result = await _sut.ImportAsync(OwnerA, bundle, Actor());

        result.IsSuccess.Should().BeTrue();
        result.Value.RulesCreated.Should().Be(0);
        result.Value.Warnings.Should().ContainSingle(w => w.Contains("dangling-rule") && w.Contains("does not exist"));
        (await _dbContext.AutoReplayRules.CountAsync(r => r.OwnerId == OwnerA)).Should().Be(0);
    }

    [Fact]
    public async Task ImportAsync_NewGrant_IsCreated()
    {
        var bundle = new ConfigurationBundle(OwnerA, DateTimeOffset.UtcNow, [], [], [
            new GovernanceGrantConfig("alex@contoso.com", "User", "Operator", null, null),
        ]);

        var result = await _sut.ImportAsync(OwnerA, bundle, Actor());

        result.IsSuccess.Should().BeTrue();
        result.Value.GrantsCreated.Should().Be(1);
        var grants = await _governanceGrantService.GetActiveGrantsAsync(OwnerA);
        grants.Value.Should().ContainSingle().Which.GranteeIdentity.Should().Be("alex@contoso.com");
    }

    [Fact]
    public async Task ImportAsync_AlreadyActiveGrant_IsIdempotentNotAnError()
    {
        await _governanceGrantService.GrantAsync(new GrantRoleRequest(
            OwnerA, "alex@contoso.com", GranteeKind.User, GovernanceRole.Operator, null, null, "admin@contoso.com"));
        var exported = await _sut.ExportAsync(OwnerA);

        var result = await _sut.ImportAsync(OwnerA, exported, Actor());

        result.IsSuccess.Should().BeTrue();
        result.Value.GrantsAlreadyActive.Should().Be(1);
        result.Value.GrantsCreated.Should().Be(0);
        (await _governanceGrantService.GetActiveGrantsAsync(OwnerA)).Value.Should().ContainSingle();
    }

    [Fact]
    public async Task ImportAsync_GrantReferencingUnknownNamespace_IsSkippedWithWarning()
    {
        var bundle = StripGrants(
            new ConfigurationBundle(OwnerA, DateTimeOffset.UtcNow, [], [], []),
            new GovernanceGrantConfig("alex@contoso.com", "User", "Operator", Guid.NewGuid(), null));

        var result = await _sut.ImportAsync(OwnerA, bundle, Actor());

        result.IsSuccess.Should().BeTrue();
        result.Value.GrantsCreated.Should().Be(0);
        result.Value.Warnings.Should().ContainSingle(w => w.Contains("alex@contoso.com") && w.Contains("does not exist"));
    }

    [Fact]
    public async Task ExportThenImport_RoundTripIsIdempotent()
    {
        await AddNamespaceAsync(OwnerA, "orders-ns");
        await AddRuleAsync(OwnerA, "rule-one");
        await _governanceGrantService.GrantAsync(new GrantRoleRequest(
            OwnerA, "alex@contoso.com", GranteeKind.User, GovernanceRole.Operator, null, null, "admin@contoso.com"));

        var exported = await _sut.ExportAsync(OwnerA);
        var result = await _sut.ImportAsync(OwnerA, exported, Actor());

        result.IsSuccess.Should().BeTrue();
        result.Value.RulesCreated.Should().Be(0);
        result.Value.RulesUnchanged.Should().Be(1);
        result.Value.GrantsCreated.Should().Be(0);
        result.Value.GrantsAlreadyActive.Should().Be(1);
        result.Value.Warnings.Should().BeEmpty();
    }
}
