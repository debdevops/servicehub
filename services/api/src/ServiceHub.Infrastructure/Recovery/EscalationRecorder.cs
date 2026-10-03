using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Events;
using ServiceHub.Core.Events.Payloads;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.Recovery;

/// <summary>
/// Turns "an agent stopped and needs a person" into durable pending work and one <c>EscalationRaised</c> event (units 5.1,
/// 5.2, 5.6).
/// </summary>
/// <remarks>
/// <para>
/// <b>One per escalation, not one per retry.</b> Auto Replay looks at the same held message every cycle; a Declined entry is
/// written for a dead letter only once, ever. A person's "no" therefore sticks until the failure changes — a new failure is a
/// new dead-letter row and may be asked about. A <b>Deny</b> is never recorded here: it is not approvable.
/// </para>
/// <para>A notification failing never fails the escalation: the ledger row is the truth, the event only says "look again".</para>
/// </remarks>
public sealed class EscalationRecorder
{
    private readonly ServiceHubDbContext _db;
    private readonly IRecoveryLedger _ledger;
    private readonly IPlatformEventBus? _bus;
    private readonly ILogger<EscalationRecorder> _logger;
    private readonly TimeProvider _time;

    /// <summary>Creates it.</summary>
    public EscalationRecorder(ServiceHubDbContext db, IRecoveryLedger ledger, ILogger<EscalationRecorder> logger, IPlatformEventBus? bus = null, TimeProvider? time = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _bus = bus;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Records that <paramref name="rule"/> wanted to replay <paramref name="message"/> and the gate escalated. Returns true when
    /// a new pending item was written; false when it was a Deny or this dead letter was already asked about.
    /// </summary>
    public async Task<bool> RecordHeldReplayAsync(
        DlqMessage message, Namespace ns, AutoReplayRule rule, RecoveryActor actor, EligibilityDecision decision, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(ns);
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.Verdict != EligibilityVerdict.Escalate)
        {
            return false;
        }

        var askedBefore = await _db.RecoveryLedgerEntries.AsNoTracking()
            .AnyAsync(e => e.OwnerId == ns.OwnerId && e.DlqMessageId == message.Id && e.State == RecoveryEntryState.Declined, ct).ConfigureAwait(false);
        if (askedBefore)
        {
            return false;
        }

        var code = decision.ReasonCode ?? "ESCALATED";
        var operation = await _ledger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = ns.OwnerId, Kind = RecoveryOperationKind.Replay, Trigger = RecoveryTrigger.AutoRule, SourceRuleId = rule.Id, Actor = actor,
            NamespaceId = ns.Id, NamespaceNameSnapshot = ns.Name, ProviderSnapshot = ns.Provider, EnvironmentSnapshot = ns.Environment,
            ScopeDescription = $"entity={message.EntityName}; message={message.MessageId}; held for a person", TargetCount = 1,
        }, ct).ConfigureAwait(false);
        if (operation.IsFailure)
        {
            _logger.LogWarning("Could not record an escalation for dead letter {Id}: {Error}", message.Id, operation.Error.Message);
            return false;
        }

        var declined = await _ledger.RecordDeclinedAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Value.Id, OwnerId = ns.OwnerId, Actor = actor, DlqMessageId = message.Id, NamespaceId = ns.Id,
            NamespaceNameSnapshot = ns.Name, ProviderSnapshot = ns.Provider, EnvironmentSnapshot = ns.Environment,
            EntityNameSnapshot = message.EntityName, EntityTypeSnapshot = message.EntityType.ToString(), TopicNameSnapshot = message.TopicName,
            SourceMessageIdSnapshot = message.MessageId, SourceSequenceNumberSnapshot = message.SequenceNumber, BodyHash = message.BodyHash,
            DeadLetterReasonSnapshot = message.DeadLetterReason, SignatureHashSnapshot = message.SignatureHash, TargetEntity = message.EntityName,
        }, code, JsonSerializer.Serialize(new { reasonCode = code, matchedCount = decision.MatchedCount, ruleId = rule.Id }), ct).ConfigureAwait(false);
        if (declined.IsFailure)
        {
            _logger.LogWarning("Could not record an escalation for dead letter {Id}: {Error}", message.Id, declined.Error.Message);
            return false;
        }

        await PublishAsync(new EscalationRaisedPayload
        {
            Kind = "approval", EntryId = declined.Value.Id, NamespaceId = ns.Id, NamespaceName = ns.Name,
            Provider = ns.Provider.ToString().ToLowerInvariant(), Entity = message.EntityName, ReasonCode = code,
            Reason = EscalationReasons.Describe(code), RaisedAtUtc = _time.GetUtcNow(),
        }, ns.OwnerId, ns.Id, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>Raises the event for an agent that stopped working (unit 5.6). The item itself is live registry state.</summary>
    public Task RaiseAgentAsync(AgentRuntimeState agent, string reasonCode, string ownerId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return PublishAsync(new EscalationRaisedPayload
        {
            Kind = "agent", AgentId = agent.Descriptor.Id, ReasonCode = reasonCode,
            Reason = $"{agent.Descriptor.Name}: {EscalationReasons.Describe(reasonCode)}", RaisedAtUtc = _time.GetUtcNow(),
        }, ownerId, null, ct);
    }

    private async Task PublishAsync(EscalationRaisedPayload payload, string ownerId, Guid? namespaceId, CancellationToken ct)
    {
        if (_bus is null)
        {
            return;
        }

        try
        {
            await _bus.PublishAsync(new PlatformEvent
            {
                Source = "escalation", Category = EventCategories.Escalation, EventType = EventTypes.EscalationRaised,
                Severity = EventSeverity.Warning, Actor = ownerId, NamespaceId = namespaceId, CloudProvider = payload.Provider, Payload = payload,
            }, ct).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A notification failing must never fail the escalation it announces.
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Escalation recorded, but announcing it failed — the pending item is still there");
        }
#pragma warning restore CA1031
    }
}
