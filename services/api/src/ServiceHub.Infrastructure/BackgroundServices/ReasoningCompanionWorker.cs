using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Agent;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Background worker for the optional reasoning companion (roadmap §7, W5). Disabled by default
/// (<see cref="ReasoningAgentOptions.Enabled"/> is <c>false</c>) — mirrors
/// <see cref="AuditRetentionWorker"/>'s no-op-when-disabled shape rather than being conditionally
/// registered, so enabling/disabling it is a pure configuration change with no restart-topology
/// difference.
/// <para>
/// Per sweep, per owner: takes the same ranked candidates <see cref="IAttentionQueueService"/>
/// already surfaces on Home (roadmap W2.2) — capped at
/// <see cref="ReasoningAgentOptions.MaxSignaturesPerSweep"/> — builds payload-free evidence for
/// each via <see cref="IIncidentReadModelService"/> and <see cref="ReasoningEvidenceMapper"/>,
/// sends it to <see cref="IReasoningAgentClient"/>, and writes every returned proposal into the
/// Playbook Ledger as <see cref="PlaybookActorKind.ReasoningAgent"/> — never anything
/// else. This is the only path in the codebase permitted to construct a
/// <see cref="PlaybookActor"/> with that <see cref="PlaybookActorKind"/> value.
/// </para>
/// </summary>
public sealed class ReasoningCompanionWorker : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ProposalExpiry = TimeSpan.FromDays(7);
    private const string ProposalKind = "ReasoningCompanionObservation";
    private const string ProposerIdentity = "ReasoningAgent:services/agent";

    private readonly IServiceProvider _serviceProvider;
    private readonly ReasoningAgentOptions _options;
    private readonly ILogger<ReasoningCompanionWorker> _logger;
    private readonly TimeSpan _sweepInterval;
    private readonly IWorkerHeartbeatStore? _heartbeatStore;

    /// <summary>Initializes a new instance of the <see cref="ReasoningCompanionWorker"/> class.</summary>
    public ReasoningCompanionWorker(
        IServiceProvider serviceProvider,
        IOptions<ReasoningAgentOptions> options,
        ILogger<ReasoningCompanionWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sweepInterval = TimeSpan.FromMinutes(Math.Max(1, _options.SweepIntervalMinutes));

        _heartbeatStore = serviceProvider.GetService<IWorkerHeartbeatStore>();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "Reasoning Companion Worker starting in disabled mode — no reasoning companion. " +
                "Set ReasoningAgent:Enabled=true to opt in.");

            // No cadence to judge staleness against while disabled by configuration — mirrors
            // AuditRetentionWorker's single-heartbeat-then-idle shape for the same reason.
            _heartbeatStore?.RecordHeartbeat(nameof(ReasoningCompanionWorker), expectedInterval: null);
            return;
        }

        _logger.LogInformation(
            "Reasoning Companion Worker starting: sweeping every {SweepIntervalMinutes}m, " +
            "up to {MaxSignaturesPerSweep} signature(s) per owner",
            _sweepInterval.TotalMinutes,
            _options.MaxSignaturesPerSweep);

        try
        {
            await Task.Delay(InitialDelay, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSweepCycleAsync(stoppingToken).ConfigureAwait(false);
                _heartbeatStore?.RecordHeartbeat(nameof(ReasoningCompanionWorker), _sweepInterval);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during reasoning companion sweep cycle");
            }

            try
            {
                await Task.Delay(_sweepInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Reasoning Companion Worker stopping");
    }

    internal async Task RunSweepCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var reasoningAgentClient = scope.ServiceProvider.GetRequiredService<IReasoningAgentClient>();
        var namespaceRepository = scope.ServiceProvider.GetRequiredService<INamespaceRepository>();
        var attentionQueueService = scope.ServiceProvider.GetRequiredService<IAttentionQueueService>();
        var incidentReadModelService = scope.ServiceProvider.GetRequiredService<IIncidentReadModelService>();
        var playbookLedger = scope.ServiceProvider.GetRequiredService<IPlaybookLedger>();

        var namespacesResult = await namespaceRepository.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        if (namespacesResult.IsFailure)
        {
            _logger.LogWarning(
                "Failed to retrieve active namespaces for reasoning companion sweep: {Error}",
                namespacesResult.Error.Message);
            return;
        }

        var ownerIds = namespacesResult.Value.Select(ns => ns.OwnerId).Distinct(StringComparer.Ordinal).ToList();
        if (ownerIds.Count == 0)
        {
            _logger.LogDebug("No active namespaces configured for reasoning companion sweep");
            return;
        }

        var proposedCount = 0;
        foreach (var ownerId in ownerIds)
        {
            proposedCount += await RunSweepForOwnerAsync(
                ownerId, attentionQueueService, incidentReadModelService, reasoningAgentClient, playbookLedger, cancellationToken)
                .ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Reasoning companion sweep cycle complete: {OwnerCount} owner(s) scanned, {ProposedCount} proposal(s) written",
            ownerIds.Count,
            proposedCount);
    }

    private async Task<int> RunSweepForOwnerAsync(
        string ownerId,
        IAttentionQueueService attentionQueueService,
        IIncidentReadModelService incidentReadModelService,
        IReasoningAgentClient reasoningAgentClient,
        IPlaybookLedger playbookLedger,
        CancellationToken cancellationToken)
    {
        var queueResult = await attentionQueueService.GetAttentionQueueAsync(ownerId, cancellationToken).ConfigureAwait(false);
        if (queueResult.IsFailure || queueResult.Value.IsEmpty)
        {
            return 0;
        }

        var candidates = queueResult.Value.Items.Take(Math.Max(0, _options.MaxSignaturesPerSweep)).ToList();
        if (candidates.Count == 0)
        {
            return 0;
        }

        var evidence = new List<ReasoningEvidenceRecord>(candidates.Count);
        foreach (var item in candidates)
        {
            var incidentResult = await incidentReadModelService
                .GetIncidentAsync(ownerId, item.NamespaceId, item.SignatureHash, cancellationToken)
                .ConfigureAwait(false);

            if (incidentResult.IsFailure)
            {
                continue;
            }

            evidence.Add(ReasoningEvidenceMapper.ToEvidenceRecord(ownerId, item.Severity, item.IsRecurring, incidentResult.Value));
        }

        if (evidence.Count == 0)
        {
            return 0;
        }

        var proposalsResult = await reasoningAgentClient.ProposeAsync(evidence, cancellationToken).ConfigureAwait(false);
        if (proposalsResult.IsFailure || proposalsResult.Value.Count == 0)
        {
            return 0;
        }

        var evidenceByRef = evidence.ToDictionary(e => e.Ref, StringComparer.Ordinal);
        var proposedCount = 0;

        foreach (var proposal in proposalsResult.Value)
        {
            if (!evidenceByRef.TryGetValue(proposal.Ref, out var record))
            {
                // The client already filters to refs it sent, but a per-owner cross-check costs
                // nothing and keeps this loop safe even if that invariant ever weakens.
                continue;
            }

            if (await ProposePlaybookEntryAsync(playbookLedger, ownerId, record, proposal, cancellationToken).ConfigureAwait(false))
            {
                proposedCount++;
            }
        }

        return proposedCount;
    }

    private async Task<bool> ProposePlaybookEntryAsync(
        IPlaybookLedger playbookLedger,
        string ownerId,
        ReasoningEvidenceRecord record,
        ReasoningProposal proposal,
        CancellationToken cancellationToken)
    {
        var proposalJson = JsonSerializer.Serialize(new
        {
            proposal.Summary,
            proposal.Considerations,
        });
        var evidenceRefJson = JsonSerializer.Serialize(new
        {
            record.SignatureHash,
            record.NamespaceId,
            record.LifecycleStatus,
            record.OccurrenceCount,
        });

        var result = await playbookLedger.ProposeAsync(new ProposePlaybookEntryRequest
        {
            OwnerId = ownerId,
            PillarKind = PillarKind.Investigate,
            ProposalKind = ProposalKind,
            EvidenceRefJson = evidenceRefJson,
            ProposalJson = proposalJson,
            Proposer = new PlaybookActor(ProposerIdentity, PlaybookActorKind.ReasoningAgent),
            SignatureHashSnapshot = record.SignatureHash,
            NamespaceId = record.NamespaceId,
            ProviderSnapshot = null,
            ExpiresAfter = ProposalExpiry,
        }, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Failed to propose Playbook Ledger entry for reasoning companion observation on signature {SignatureHash}: {Error}",
                record.SignatureHash,
                result.Error.Message);
            return false;
        }

        return true;
    }
}
