using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.DlqObserver;

namespace ServiceHub.Infrastructure.Agents;

/// <summary>
/// Keeps each switched-on "whole view of the dead-letter queue" confirmed (ADR-0018): on every cycle it asks the cloud's own
/// check whether the view is usable, and records that it is. A view that stops answering simply goes stale, and
/// everything that relied on it fails closed by itself.
/// </summary>
/// <remarks>
/// It <b>observes</b>. It never replays, never sends a test message into anyone's queue, and never decides that a cloud
/// "can prove" anything — it only records that a check, made just now, succeeded. It never names a cloud (R4).
/// </remarks>
public sealed class DeadLetterViewAgent : IAgent
{
    private const int DefaultIntervalSeconds = 60;

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<DeadLetterViewAgent> _logger;

    /// <summary>Creates the agent. Cadence comes from <c>DlqObserver:CheckIntervalSeconds</c>.</summary>
    public DeadLetterViewAgent(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<DeadLetterViewAgent> logger)
    {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration);

        var interval = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("DlqObserver:CheckIntervalSeconds", DefaultIntervalSeconds), 10, 3600));
        Descriptor = new AgentDescriptor(
            Id: "dead-letter-view",
            Name: "Fix Confirmer",
            Purpose: "Where a cloud cannot prove a fix on its own, keeps checking that ServiceHub can still see that cloud's whole dead-letter queue — which is what lets a replay there be confirmed.",
            Kind: AgentKind.Watch,
            Authority: AgentAuthority.Observes,
            Cadence: interval,
            Notes: "Only for clouds where you switched this on in Connections. If it stops being able to see, replays there go back to 'verification required'.",
            May: ["Check that ServiceHub can still see a cloud's whole dead-letter queue", "Record replayed messages that come back"],
            MayNot: ["Replay, purge or change any message", "Send a test message into your queues", "Say a cloud can confirm a fix when it could not see everything"],
            LedgerActor: "System:DeadLetterViewAgent",
            Needs: AgentNeeds.ObserverCloud);
    }

    /// <inheritdoc />
    public AgentDescriptor Descriptor { get; }

    /// <inheritdoc />
    public async Task<AgentCycleResult> ExecuteCycleAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var attestations = scope.ServiceProvider.GetRequiredService<IDlqObserverAttestationService>();
        var namespaces = scope.ServiceProvider.GetRequiredService<INamespaceRepository>();
        var checks = scope.ServiceProvider.GetServices<IDeadLetterReturnCheck>().ToList();
        var tracker = scope.ServiceProvider.GetRequiredService<DeadLetterViewTracker>();
        var time = scope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;

        var enabled = await attestations.GetAllEnabledAsync(ct).ConfigureAwait(false);
        if (enabled.Count == 0)
        {
            return AgentCycleResult.Idle("no cloud has this switched on");
        }

        int confirmed = 0, unusable = 0;
        foreach (var attestation in enabled)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var found = await namespaces.GetByIdAsync(attestation.NamespaceId, ct).ConfigureAwait(false);
                var check = found.IsSuccess ? checks.FirstOrDefault(c => c.Provider == found.Value.Provider) : null;
                if (found.IsFailure || found.Value.OwnerId != attestation.OwnerId || check is null)
                {
                    tracker.Lost(attestation.OwnerId, attestation.NamespaceId);
                    unusable++;
                    continue;
                }

                var health = await check.CheckHealthAsync(found.Value, attestation, ct).ConfigureAwait(false);
                if (!health.Healthy)
                {
                    if (health.Lost)
                    {
                        tracker.Lost(attestation.OwnerId, attestation.NamespaceId);
                    }

                    unusable++;
                    _logger.LogInformation("Dead-letter view for namespace {NamespaceId} is not usable: {Reason}", attestation.NamespaceId, health.Reason);
                    continue;
                }

                var now = time.GetUtcNow();
                // A view that had gone stale saw nothing for a while: what it knew before cannot be relied on, so its clock restarts.
                if (!attestation.IsLiveAt(now))
                {
                    tracker.Lost(attestation.OwnerId, attestation.NamespaceId);
                }

                var saved = await attestations.RecordCanaryConfirmedAsync(attestation.OwnerId, attestation.NamespaceId, ct).ConfigureAwait(false);
                if (saved.IsSuccess)
                {
                    tracker.Confirmed(attestation.OwnerId, attestation.NamespaceId, now);
                    confirmed++;
                }
                else
                {
                    unusable++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // One namespace's failure must never stop the others being checked.
            catch (Exception ex)
            {
                unusable++;
                _logger.LogWarning(ex, "Dead-letter view check failed for namespace {NamespaceId}", attestation.NamespaceId);
            }
#pragma warning restore CA1031
        }

        return new AgentCycleResult(enabled.Count, 0, $"{confirmed} cloud(s) can be seen in full, {unusable} cannot right now");
    }
}
