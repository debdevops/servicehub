using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.Agents;

/// <summary>
/// Proves, namespace by namespace, that the DLQ observer a person set up is still watching (unit 4.2; ADR-004 item 4).
/// It sends one small canary into the dead-letter queue the person named, then looks for that message's id in the
/// observer's own log. Only a canary the log actually shows moves <see cref="DlqObserverAttestation.LastConfirmedAt"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Authority: ActsWithApproval.</b> It puts a message into a cloud queue, so it may only do so for a namespace whose
/// observer a person turned on (<c>PUT /namespaces/{id}/dlq-observer</c>) — that setup is the approval. With nothing turned
/// on it does nothing, and a cloud that can already prove absence by itself (Azure) never needs it.
/// </para>
/// <para>
/// <b>Fails closed.</b> Liveness is never assumed: no id from the cloud, no reader, an unreadable log or an unanswered canary
/// each leave the observer "not live" and say why in the cycle summary. The canary is a real message in a real dead-letter
/// queue, so one shows up beside the dead letters; its body says what it is.
/// </para>
/// </remarks>
public sealed class DlqObserverCanaryAgent : IAgent
{
    /// <summary>The body of every canary — what a person sees in the queue.</summary>
    public const string CanaryBody = "ServiceHub DLQ observer liveness check — safe to ignore or delete.";

    /// <summary>The application property every canary carries.</summary>
    public const string CanaryProperty = "servicehub-canary";

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;
    private readonly ILogger<DlqObserverCanaryAgent> _logger;

    /// <summary>Creates the agent.</summary>
    public DlqObserverCanaryAgent(IServiceScopeFactory scopes, ILogger<DlqObserverCanaryAgent> logger, TimeProvider? time = null)
    {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
        Descriptor = new AgentDescriptor(
            Id: "dlq-observer-canary",
            Name: "DLQ Observer Check",
            Purpose: "For each cloud where you set up a DLQ observer, sends a small test message into its dead-letter queue and checks the observer wrote it down, so \"verified\" is only said while the observer is really watching.",
            Kind: AgentKind.Act,
            Authority: AgentAuthority.ActsWithApproval,
            Cadence: TimeSpan.FromMinutes(1),
            Needs: AgentNeeds.ObserverCloud,
            Notes: "Only AWS and Google Cloud need this — Azure can confirm on its own. It does nothing until you turn the observer on for a cloud. The test message is a real message in that dead-letter queue.",
            May: ["Send one test message into the dead-letter queue you named, for a cloud whose observer you turned on", "Look the test message up in the observer's own log"],
            MayNot: ["Do anything for a cloud whose observer you have not turned on", "Say the observer is working when its log does not show the test message", "Touch any real dead letter"]);
    }

    /// <inheritdoc />
    public AgentDescriptor Descriptor { get; }

    /// <inheritdoc />
    public async Task<AgentCycleResult> ExecuteCycleAsync(CancellationToken ct)
    {
        IReadOnlyList<DlqObserverAttestation> enabled;
        using (var scope = _scopes.CreateScope())
        {
            enabled = await scope.ServiceProvider.GetRequiredService<IDlqObserverAttestationService>().GetAllEnabledAsync(ct).ConfigureAwait(false);
        }

        if (enabled.Count == 0)
        {
            return AgentCycleResult.Idle("no DLQ observer is turned on");
        }

        int confirmed = 0, sent = 0, waiting = 0, problems = 0;
        foreach (var attestation in enabled)
        {
            switch (await CheckOneAsync(attestation, ct).ConfigureAwait(false))
            {
                case Step.Confirmed: confirmed++; break;
                case Step.Sent: sent++; break;
                case Step.Waiting: waiting++; break;
                case Step.Problem: problems++; break;
            }
        }

        var parts = new List<string> { $"checked {enabled.Count} observer(s)" };
        if (confirmed > 0) parts.Add($"{confirmed} confirmed live");
        if (sent > 0) parts.Add($"{sent} test message(s) sent");
        if (waiting > 0) parts.Add($"{waiting} waiting for the observer to log its test message");
        if (problems > 0) parts.Add($"{problems} could not be checked");
        return new AgentCycleResult(enabled.Count, sent, string.Join(", ", parts), Degraded: problems > 0);
    }

    private enum Step { Quiet, Confirmed, Sent, Waiting, Problem }

    private async Task<Step> CheckOneAsync(DlqObserverAttestation attestation, CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var sp = scope.ServiceProvider;
            var service = sp.GetRequiredService<IDlqObserverAttestationService>();
            var nsResult = await sp.GetRequiredService<INamespaceRepository>().GetByIdAsync(attestation.NamespaceId, ct).ConfigureAwait(false);
            if (nsResult.IsFailure)
            {
                _logger.LogWarning("DLQ observer check skipped: namespace {NamespaceId} is no longer connected", attestation.NamespaceId);
                return Step.Problem;
            }

            var ns = nsResult.Value;
            var reader = sp.GetServices<IDlqObserverLogReader>().FirstOrDefault(r => r.Provider == ns.Provider);
            if (reader is null || string.IsNullOrWhiteSpace(attestation.ObserverReference) || string.IsNullOrWhiteSpace(attestation.DlqEntityName))
            {
                return Step.Problem;
            }

            var now = _time.GetUtcNow();
            var outstanding = attestation.LastCanaryMessageId is not null && attestation.LastCanarySentAt is { } sentAt
                && (attestation.LastConfirmedAt is not { } confirmedAt || confirmedAt < sentAt);

            if (outstanding && await reader.HasRecordedArrivalAsync(ns, attestation.ObserverReference, attestation.LastCanaryMessageId!, ct).ConfigureAwait(false))
            {
                var recorded = await service.RecordCanaryConfirmedAsync(attestation.OwnerId, attestation.NamespaceId, ct).ConfigureAwait(false);
                return recorded.IsSuccess ? Step.Confirmed : Step.Problem;
            }

            // A canary not yet seen is given a third of the staleness bound before another is sent; a confirmed one is re-sent
            // on the same beat, so the confirmation is always refreshed well inside the bound.
            var beat = TimeSpan.FromMinutes(Math.Max(1, attestation.StalenessBoundMinutes / 3.0));
            if (attestation.LastCanarySentAt is { } last && now - last < beat)
            {
                return outstanding ? Step.Waiting : Step.Quiet;
            }

            var sender = sp.GetRequiredService<IMessageOperationsService>();
            var sentResult = await sender.SendReturningProviderIdAsync(new SendMessageRequest(
                NamespaceId: ns.Id,
                EntityName: attestation.DlqEntityName,
                Body: CanaryBody,
                ApplicationProperties: new Dictionary<string, object> { [CanaryProperty] = "1" }), ct).ConfigureAwait(false);

            if (sentResult.IsFailure || string.IsNullOrWhiteSpace(sentResult.Value))
            {
                // No id means the log cannot be asked about it — never record a canary that cannot be tracked.
                _logger.LogWarning("DLQ observer canary for {NamespaceId} was not sent or returned no id: {Reason}",
                    attestation.NamespaceId, sentResult.IsFailure ? sentResult.Error.Message : "the cloud gave no message id");
                return Step.Problem;
            }

            var marked = await service.RecordCanarySentAsync(attestation.OwnerId, attestation.NamespaceId, sentResult.Value!, ct).ConfigureAwait(false);
            return marked.IsSuccess ? Step.Sent : Step.Problem;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "DLQ observer check failed for namespace {NamespaceId}", attestation.NamespaceId);
            return Step.Problem;
        }
    }
}
