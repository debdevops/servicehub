using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure;

/// <summary>
/// <inheritdoc cref="IConfigurationExportService"/>
/// </summary>
public sealed class ConfigurationExportService : IConfigurationExportService
{
    private readonly DlqDbContext _dbContext;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly IGovernanceGrantService _governanceGrantService;

    /// <summary>Initialises a new instance of <see cref="ConfigurationExportService"/>.</summary>
    public ConfigurationExportService(
        DlqDbContext dbContext,
        INamespaceRepository namespaceRepository,
        IGovernanceGrantService governanceGrantService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _governanceGrantService = governanceGrantService ?? throw new ArgumentNullException(nameof(governanceGrantService));
    }

    /// <inheritdoc />
    public async Task<ConfigurationBundle> ExportAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new ArgumentException("Owner identifier is required.", nameof(ownerId));
        }

        var namespacesResult = await _namespaceRepository.GetByOwnerAsync(ownerId, cancellationToken: cancellationToken);
        var namespaces = namespacesResult.IsSuccess ? namespacesResult.Value : [];

        var rules = (await _dbContext.AutoReplayRules
            .AsNoTracking()
            .Where(r => r.OwnerId == ownerId)
            .ToListAsync(cancellationToken))
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var grantsResult = await _governanceGrantService.GetActiveGrantsAsync(ownerId, cancellationToken);
        var grants = grantsResult.IsSuccess ? grantsResult.Value : [];

        return new ConfigurationBundle(
            OwnerId: ownerId,
            ExportedAtUtc: DateTimeOffset.UtcNow,
            Namespaces: namespaces
                .Select(ns => new NamespaceReference(ns.Id, ns.Name, ns.DisplayName, ns.Environment.ToString(), ns.Provider.ToString()))
                .ToList(),
            AutoReplayRules: rules.Select(MapRule).ToList(),
            GovernanceGrants: grants
                .OrderBy(g => g.GranteeIdentity, StringComparer.OrdinalIgnoreCase)
                .Select(g => new GovernanceGrantConfig(
                    g.GranteeIdentity, g.GranteeKind.ToString(), g.Role.ToString(), g.NamespaceId, g.PillarKind?.ToString()))
                .ToList());
    }

    /// <inheritdoc />
    public async Task<Result<ConfigurationImportResult>> ImportAsync(
        string ownerId, ConfigurationBundle bundle, RecoveryActor actor, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new ArgumentException("Owner identifier is required.", nameof(ownerId));
        }

        if (!string.Equals(bundle.OwnerId, ownerId, StringComparison.Ordinal))
        {
            return Result<ConfigurationImportResult>.Failure(Error.Validation(
                "ConfigurationImport.OwnerMismatch",
                $"This bundle was exported for owner '{bundle.OwnerId}' and cannot be imported for '{ownerId}'."));
        }

        var namespacesResult = await _namespaceRepository.GetByOwnerAsync(ownerId, cancellationToken: cancellationToken);
        var validNamespaceIds = (namespacesResult.IsSuccess ? namespacesResult.Value : [])
            .Select(ns => ns.Id)
            .ToHashSet();

        var warnings = new List<string>();
        int rulesCreated = 0, rulesUpdated = 0, rulesUnchanged = 0;

        foreach (var ruleConfig in bundle.AutoReplayRules)
        {
            if (ruleConfig.NamespaceId is { } namespaceId && !validNamespaceIds.Contains(namespaceId))
            {
                warnings.Add($"Rule '{ruleConfig.Name}': namespace {namespaceId} does not exist for this owner — skipped.");
                continue;
            }

            var existing = await _dbContext.AutoReplayRules
                .FirstOrDefaultAsync(r => r.OwnerId == ownerId && r.Name == ruleConfig.Name, cancellationToken);

            var conditionsJson = ruleConfig.Conditions.GetRawText();
            var actionJson = ruleConfig.Action.GetRawText();

            if (existing is null)
            {
                _dbContext.AutoReplayRules.Add(new AutoReplayRule
                {
                    Name = ruleConfig.Name,
                    OwnerId = ownerId,
                    Description = ruleConfig.Description,
                    NamespaceId = ruleConfig.NamespaceId,
                    Enabled = ruleConfig.Enabled,
                    ConditionsJson = conditionsJson,
                    ActionsJson = actionJson,
                    CreatedAt = DateTimeOffset.UtcNow,
                    MaxReplaysPerHour = ruleConfig.MaxReplaysPerHour,
                });
                rulesCreated++;
                continue;
            }

            var changed = existing.Description != ruleConfig.Description
                || existing.NamespaceId != ruleConfig.NamespaceId
                || existing.Enabled != ruleConfig.Enabled
                || existing.ConditionsJson != conditionsJson
                || existing.ActionsJson != actionJson
                || existing.MaxReplaysPerHour != ruleConfig.MaxReplaysPerHour;

            if (!changed)
            {
                rulesUnchanged++;
                continue;
            }

            existing.Description = ruleConfig.Description;
            existing.NamespaceId = ruleConfig.NamespaceId;
            existing.Enabled = ruleConfig.Enabled;
            existing.ConditionsJson = conditionsJson;
            existing.ActionsJson = actionJson;
            existing.MaxReplaysPerHour = ruleConfig.MaxReplaysPerHour;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            rulesUpdated++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        int grantsCreated = 0, grantsAlreadyActive = 0;

        foreach (var grantConfig in bundle.GovernanceGrants)
        {
            if (grantConfig.NamespaceId is { } namespaceId && !validNamespaceIds.Contains(namespaceId))
            {
                warnings.Add($"Grant for '{grantConfig.GranteeIdentity}': namespace {namespaceId} does not exist for this owner — skipped.");
                continue;
            }

            if (!Enum.TryParse<GranteeKind>(grantConfig.GranteeKind, ignoreCase: true, out var granteeKind))
            {
                warnings.Add($"Grant for '{grantConfig.GranteeIdentity}': unrecognised granteeKind '{grantConfig.GranteeKind}' — skipped.");
                continue;
            }

            if (!Enum.TryParse<GovernanceRole>(grantConfig.Role, ignoreCase: true, out var role))
            {
                warnings.Add($"Grant for '{grantConfig.GranteeIdentity}': unrecognised role '{grantConfig.Role}' — skipped.");
                continue;
            }

            PillarKind? pillarKind = null;
            if (grantConfig.PillarKind is not null)
            {
                if (!Enum.TryParse<PillarKind>(grantConfig.PillarKind, ignoreCase: true, out var parsedPillar))
                {
                    warnings.Add($"Grant for '{grantConfig.GranteeIdentity}': unrecognised pillarKind '{grantConfig.PillarKind}' — skipped.");
                    continue;
                }
                pillarKind = parsedPillar;
            }

            var grantResult = await _governanceGrantService.GrantAsync(new GrantRoleRequest(
                ownerId, grantConfig.GranteeIdentity, granteeKind, role, grantConfig.NamespaceId, pillarKind, actor.Identity),
                cancellationToken);

            if (grantResult.IsSuccess)
            {
                grantsCreated++;
            }
            else if (grantResult.Error.Type == ErrorType.Conflict)
            {
                grantsAlreadyActive++;
            }
            else
            {
                warnings.Add($"Grant for '{grantConfig.GranteeIdentity}': {grantResult.Error.Message}");
            }
        }

        return Result<ConfigurationImportResult>.Success(new ConfigurationImportResult(
            rulesCreated, rulesUpdated, rulesUnchanged, grantsCreated, grantsAlreadyActive, warnings));
    }

    private static AutoReplayRuleConfig MapRule(AutoReplayRule rule) => new(
        Name: rule.Name,
        Description: rule.Description,
        NamespaceId: rule.NamespaceId,
        Enabled: rule.Enabled,
        MaxReplaysPerHour: rule.MaxReplaysPerHour,
        Conditions: JsonSerializer.Deserialize<JsonElement>(rule.ConditionsJson),
        Action: JsonSerializer.Deserialize<JsonElement>(rule.ActionsJson));
}
