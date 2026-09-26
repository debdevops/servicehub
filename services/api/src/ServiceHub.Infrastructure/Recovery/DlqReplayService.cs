using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;
using ServiceHub.Core.Security;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;

namespace ServiceHub.Infrastructure.Recovery;

/// <summary>
/// Replays one dead letter: shows what would happen, then does it through the gate and the ledger (unit 2.7).
/// </summary>
/// <remarks>
/// The order is the safety: <b>gate → open a ledger entry → touch the cloud → record what the cloud said</b>.
/// The entry exists before the cloud is called, so a crash mid-call leaves an <c>Executing</c> entry to be
/// reconciled, never a replay with no record. What the cloud said is recorded with
/// <see cref="CancellationToken.None"/>: a caller who hangs up must not lose the evidence of something
/// that already happened. A replay is never retried, and no provider is named here (R4).
/// </remarks>
public sealed class DlqReplayService : IDlqReplayService
{
    private const string OriginalEntity = "original-entity";
    private const string NotActive = "NOT_ACTIVE";

    private readonly ServiceHubDbContext _db;
    private readonly IRecoveryLedger _ledger;
    private readonly IRecoveryEligibilityGate _gate;
    private readonly IMessageOperationsService _operations;
    private readonly ICloudProviderRouter _router;
    private readonly IAuditTrail _audit;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DlqReplayService> _logger;

    /// <summary>Creates the service.</summary>
    public DlqReplayService(
        ServiceHubDbContext db, IRecoveryLedger ledger, IRecoveryEligibilityGate gate, IMessageOperationsService operations,
        ICloudProviderRouter router, IAuditTrail audit, IConfiguration configuration, ILogger<DlqReplayService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<EligibilityDecision?> CheckEligibilityAsync(
        long dlqMessageId, Namespace ns, RecoveryActor actor, RecoveryOperationKind kind, CancellationToken cancellationToken)
    {
        var message = await LoadAsync(dlqMessageId, ns, tracking: false, cancellationToken);
        return message is null ? null : await EvaluateAsync(message, ns, actor, kind, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<ReplayProposal>> ProposeAsync(
        long dlqMessageId, Namespace ns, RecoveryActor actor, CancellationToken cancellationToken)
    {
        var message = await LoadAsync(dlqMessageId, ns, tracking: false, cancellationToken);
        if (message is null)
        {
            return Result<ReplayProposal>.Failure(Error.NotFound(ErrorCodes.Message.NotFound, $"Dead letter '{dlqMessageId}' was not found."));
        }

        var decision = await EvaluateAsync(message, ns, actor, RecoveryOperationKind.Replay, cancellationToken);
        var capabilities = _router.IsRegistered(ns.Provider) ? _router.Resolve(ns.Provider).Capabilities : ProviderCapabilities.For(ns.Provider);
        var prior = (await _ledger.FindLineageMatchesAsync(
            ns.OwnerId, ns.Id, message.EntityName, message.BodyHash, DateTimeOffset.UtcNow.AddDays(-90), cancellationToken)).Count;

        var reason = message.DeadLetterReason;
        var others = await _db.DlqMessages.AsNoTracking().CountAsync(
            m => m.NamespaceId == ns.Id && m.EntityName == message.EntityName && m.Status == DlqMessageStatus.Active
                 && m.Id != message.Id && m.DeadLetterReason == reason, cancellationToken);

        var active = message.Status == DlqMessageStatus.Active;
        var canExecute = active && decision.Verdict == EligibilityVerdict.Allow;

        var checks = new List<ReplayCheck>
        {
            new("status", "Still in the dead-letter queue", active ? "passed" : "blocked",
                active ? null : "ServiceHub has already seen this one leave the queue."),
            new("environment", ns.Environment == EnvironmentType.Prod ? "A production namespace" : "Not a production namespace",
                ns.Environment == EnvironmentType.Prod ? "blocked" : "passed", ns.Environment.ToString()),
            new("frequency", prior >= RecoveryEligibilityGate.RecurrenceLineageCap ? "Replayed several times already" : "Not replayed too often",
                prior >= RecoveryEligibilityGate.RecurrenceLineageCap ? "warning" : "passed",
                $"{prior} of {RecoveryEligibilityGate.RecurrenceLineageCap} earlier attempts"),
            new("verification", capabilities.CanProveDlqAbsence ? "The cloud can confirm whether it stayed fixed" : "The cloud cannot prove it stayed fixed",
                capabilities.CanProveDlqAbsence ? "passed" : "warning",
                capabilities.CanProveDlqAbsence ? null : "The result will read “verification required”, never “verified”."),
        };

        return Result<ReplayProposal>.Success(new ReplayProposal(
            message.Id, message.MessageId, message.EntityName, TargetOf(message),
            ns.DisplayName ?? ns.Name, ns.Provider.ToString().ToLowerInvariant(), ns.Environment.ToString(),
            capabilities.SupportsRecoveryMarker, prior, RecoveryEligibilityGate.RecurrenceLineageCap, others,
            RecoveryLedgerService.ResolveObservationWindowHours(_configuration), capabilities.CanProveDlqAbsence,
            decision.Verdict.ToString(), decision.ReasonCode, decision.Verdict == EligibilityVerdict.Escalate,
            canExecute, canExecute ? null : (!active ? NotActive : decision.ReasonCode), checks));
    }

    /// <inheritdoc />
    public async Task<Result<ReplayOutcome>> ReplayAsync(
        long dlqMessageId, Namespace ns, RecoveryActor actor, string? intentHeader, string? correlationId,
        CancellationToken cancellationToken, long? ruleId = null)
    {
        var message = await LoadAsync(dlqMessageId, ns, tracking: true, cancellationToken);
        if (message is null)
        {
            return Result<ReplayOutcome>.Failure(Error.NotFound(ErrorCodes.Message.NotFound, $"Dead letter '{dlqMessageId}' was not found."));
        }

        if (message.Status != DlqMessageStatus.Active)
        {
            return Result<ReplayOutcome>.Failure(Error.Conflict(NotActive, "This message is no longer in the dead-letter queue, so there is nothing to replay."));
        }

        var decision = await EvaluateAsync(message, ns, actor, RecoveryOperationKind.Replay, cancellationToken);
        if (decision.Verdict != EligibilityVerdict.Allow)
        {
            await AuditAsync(ns, actor, message, "refused", decision.ReasonCode, correlationId, cancellationToken);
            var refusal = $"Replay is not allowed here ({decision.ReasonCode}).";
            return Result<ReplayOutcome>.Failure(decision.Verdict == EligibilityVerdict.Deny
                ? Error.Forbidden(decision.ReasonCode ?? "DENIED", refusal)
                : Error.Conflict(decision.ReasonCode ?? "ESCALATED", refusal + " A person with approval rights has to decide."));
        }

        // From here the attempt is on the record whatever happens.
        var (entity, subscription) = SourceOf(message);
        var operation = await _ledger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = ns.OwnerId, Kind = RecoveryOperationKind.Replay, Trigger = ruleId is null ? RecoveryTrigger.Manual : RecoveryTrigger.AutoRule, SourceRuleId = ruleId, Actor = actor,
            IntentHeader = intentHeader, NamespaceId = ns.Id, NamespaceNameSnapshot = ns.Name, ProviderSnapshot = ns.Provider,
            EnvironmentSnapshot = ns.Environment, ScopeDescription = $"entity={message.EntityName}; message={message.MessageId}",
            CorrelationId = correlationId, TargetCount = 1,
        }, CancellationToken.None);
        if (operation.IsFailure)
        {
            return Result<ReplayOutcome>.Failure(operation.Error);
        }

        var entry = await _ledger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Value.Id, OwnerId = ns.OwnerId, Actor = actor, DlqMessageId = message.Id, NamespaceId = ns.Id,
            NamespaceNameSnapshot = ns.Name, ProviderSnapshot = ns.Provider, EnvironmentSnapshot = ns.Environment,
            EntityNameSnapshot = message.EntityName, EntityTypeSnapshot = message.EntityType.ToString(),
            TopicNameSnapshot = message.TopicName, SourceMessageIdSnapshot = message.MessageId,
            SourceSequenceNumberSnapshot = message.SequenceNumber, BodyHash = message.BodyHash,
            DeadLetterReasonSnapshot = message.DeadLetterReason, TargetEntity = TargetOf(message),
            // The signature is what trust is earned against (unit 4.1): without it no outcome would ever count.
            SignatureHashSnapshot = message.SignatureHash,
        }, CancellationToken.None);
        if (entry.IsFailure)
        {
            return Result<ReplayOutcome>.Failure(entry.Error);
        }

        // The cloud call. An exception means we do not know what happened — that is its own state, never "failed".
        RecoveryExecutionOutcome executed;
        string? errorCode = null;
        string? errorMessage = null;
        var markerApplied = false;
        try
        {
            var result = await _operations.ReplayMessageAsync(
                ns.Id, entity, subscription, message.SequenceNumber, entry.Value.Id, cancellationToken);
            if (result.IsSuccess)
            {
                executed = RecoveryExecutionOutcome.Accepted;
                markerApplied = result.Value;
            }
            else if (NothingCanHaveHappened(result.Error))
            {
                executed = RecoveryExecutionOutcome.Rejected;
                errorCode = result.Error.Code;
                errorMessage = LogRedactor.SanitiseForLog(result.Error.Message);
            }
            else
            {
                // An internal, timeout or upstream failure may have struck after the cloud acted. Calling that
                // "failed" would invite a second replay and a duplicate message.
                executed = RecoveryExecutionOutcome.Unknown;
                errorCode = result.Error.Code;
                errorMessage = "ServiceHub lost contact with the cloud before it could tell whether the message was sent back.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Replay of dead letter {Id} threw; recording its outcome as unknown", message.Id);
            executed = RecoveryExecutionOutcome.Unknown;
            errorMessage = "ServiceHub lost contact with the cloud before it could tell whether the message was sent back.";
        }

        var recorded = await _ledger.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Value.Id, OwnerId = ns.OwnerId, Actor = actor, Outcome = executed,
            RecoveryMarker = markerApplied ? entry.Value.Id.ToString() : null, MarkerApplied = markerApplied,
            ProviderDetailJson = errorCode is null ? null : System.Text.Json.JsonSerializer.Serialize(new { errorCode }),
        }, CancellationToken.None);
        var final = recorded.IsSuccess ? recorded.Value : entry.Value;

        var status = executed.ToString().ToLowerInvariant();
        _db.ReplayHistories.Add(new ReplayHistory
        {
            DlqMessageId = message.Id, RuleId = ruleId, OwnerId = ns.OwnerId, NamespaceId = ns.Id, RecoveryEntryId = entry.Value.Id,
            MessageId = message.MessageId, SourceEntity = message.EntityName, ReplayedAt = DateTimeOffset.UtcNow,
            ReplayedBy = actor.Identity, ReplayStrategy = OriginalEntity, ReplayedToEntity = TargetOf(message),
            OutcomeStatus = status, ErrorDetails = errorMessage,
        });

        if (executed == RecoveryExecutionOutcome.Accepted)
        {
            message.Status = DlqMessageStatus.Resolved;
            message.ResolvedAt = DateTimeOffset.UtcNow;
            message.ResolutionCause = DlqResolutionCause.ReplayedByServiceHub;
        }

        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // A scan resolved the row first. The ledger holds the truth either way.
            _logger.LogWarning(ex, "Dead letter {Id} was updated by a scan while it was being replayed", message.Id);
        }

        await AuditAsync(ns, actor, message, status, errorCode, correlationId, CancellationToken.None);

        var sentence = executed switch
        {
            RecoveryExecutionOutcome.Accepted => $"Sent back to {TargetOf(message)}. ServiceHub will watch for it coming back.",
            RecoveryExecutionOutcome.Rejected => errorMessage ?? "The cloud did not accept it, so nothing was sent back.",
            _ => errorMessage ?? "Whether it was sent back is not known. Check the queue before trying again.",
        };
        return Result<ReplayOutcome>.Success(new ReplayOutcome(
            entry.Value.Id, operation.Value.Id, status, final.State.ToString(), markerApplied && final.MarkerApplied,
            final.ObservationWindowEndsAt, sentence, errorCode));
    }

    /// <inheritdoc />
    public async Task<ReplayPage> ListAsync(
        IReadOnlyCollection<Guid> namespaceIds, string? result, long? dlqMessageId, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var rows = _db.ReplayHistories.AsNoTracking().Where(r => namespaceIds.Contains(r.NamespaceId));
        if (!string.IsNullOrWhiteSpace(result))
        {
            var wanted = result.Trim().ToLowerInvariant();
            rows = rows.Where(r => r.OutcomeStatus == wanted);
        }

        if (dlqMessageId is { } message)
        {
            rows = rows.Where(r => r.DlqMessageId == message);
        }

        var total = await rows.CountAsync(cancellationToken);
        var pageRows = await rows.OrderByDescending(r => r.ReplayedAt).ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        var entryIds = pageRows.Where(r => r.RecoveryEntryId != null).Select(r => r.RecoveryEntryId!.Value).ToList();
        var entries = await _db.RecoveryLedgerEntries.AsNoTracking().Where(e => entryIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, cancellationToken);

        // Whether each namespace's cloud can prove the queue stayed empty — a capability of the namespace, never its name.
        var providers = await _db.Namespaces.AsNoTracking().Where(n => namespaceIds.Contains(n.Id)).ToDictionaryAsync(n => n.Id, n => n.Provider, cancellationToken);
        bool CanConfirm(Guid ns) =>
            providers.TryGetValue(ns, out var p) && (_router.IsRegistered(p) ? _router.Resolve(p).Capabilities : ProviderCapabilities.For(p)).CanProveDlqAbsence;

        var nullableIds = entryIds.Select(x => (Guid?)x).ToList();
        var reasons = (await _db.RecoveryEvents.AsNoTracking()
            .Where(e => nullableIds.Contains(e.EntryId) && e.EventType == RecoveryEventType.ObservationUnavailable)
            .Select(e => new { e.EntryId, e.DetailJson }).ToListAsync(cancellationToken))
            .Where(e => e.EntryId is not null)
            .ToDictionary(e => e.EntryId!.Value, e => ReasonOf(e.DetailJson));

        var items = pageRows.Select(r =>
        {
            entries.TryGetValue(r.RecoveryEntryId ?? Guid.Empty, out var entry);
            reasons.TryGetValue(r.RecoveryEntryId ?? Guid.Empty, out var reason);
            return new ReplayListItem(
                r.Id, r.DlqMessageId, r.NamespaceId, providers.TryGetValue(r.NamespaceId, out var provider) ? provider.ToString().ToLowerInvariant() : "unknown",
                r.MessageId, r.SourceEntity, r.ReplayedToEntity, r.ReplayedAt, r.ReplayedBy, ActorOf(r.ReplayedBy),
                r.OutcomeStatus, entry?.State.ToString(), entry?.ObservationWindowEndsAt, entry?.MarkerApplied ?? false,
                VerificationOf(r, entry, reason, CanConfirm(r.NamespaceId)));
        }).ToList();

        return new ReplayPage(items, total, page, pageSize);
    }

    /// <summary>
    /// What the ledger entry says about the replay, in the one shape every cloud shares. <c>verified</c> needs BOTH
    /// the ledger's <c>Recovered</c> AND a cloud that can prove absence — a belt-and-braces check on R4, so a
    /// mis-set state can never render as proof.
    /// </summary>
    private static ReplayVerification VerificationOf(ReplayHistory row, RecoveryLedgerEntry? entry, string? reason, bool canConfirm)
    {
        const string setUpObserver = "SETUP_DLQ_OBSERVER";
        if (row.OutcomeStatus == "rejected")
        {
            return new ReplayVerification("not_sent", null, null, null, canConfirm, null);
        }

        if (row.OutcomeStatus == "unknown" || entry?.State == RecoveryEntryState.ExecutionUnknown)
        {
            return new ReplayVerification("unknown", null, null, null, canConfirm, null);
        }

        return entry?.State switch
        {
            RecoveryEntryState.Recovered when canConfirm =>
                new ReplayVerification("verified", null, null, entry.ObservationWindowEndsAt, true, null),
            RecoveryEntryState.Recovered or RecoveryEntryState.Unverified =>
                new ReplayVerification("verification_required", reason, null, entry.ObservationWindowEndsAt, canConfirm,
                    canConfirm || (reason is not null && !reason.EndsWith("_NO_ABSENCE_PROOF", StringComparison.Ordinal)) ? null : setUpObserver),
            RecoveryEntryState.Returned =>
                new ReplayVerification("returned", null, entry.VerificationConfidence?.ToString(), entry.ObservationWindowEndsAt, canConfirm, null),
            _ => new ReplayVerification("watching", null, null, entry?.ObservationWindowEndsAt, canConfirm, canConfirm ? null : setUpObserver),
        };
    }

    /// <summary>The recorded identity as a person may read it — a kind and words, never a name that was not supplied.</summary>
    private static ReplayActor ActorOf(string identity)
    {
        var kind = identity.StartsWith("ApiKey:", StringComparison.Ordinal) ? "apiKey"
            : identity.StartsWith("System:", StringComparison.Ordinal) ? "system"
            : "user";
        return new ReplayActor(identity, kind, RecoveryActorLabel.For(identity), RecoveryActorLabel.IsSession(identity));
    }

    private static string? ReasonOf(string? detailJson)
    {
        if (string.IsNullOrEmpty(detailJson))
        {
            return null;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(detailJson);
            return doc.RootElement.TryGetProperty("reason", out var r) ? r.GetString() : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private async Task<DlqMessage?> LoadAsync(long id, Namespace ns, bool tracking, CancellationToken cancellationToken)
    {
        var query = tracking ? _db.DlqMessages : _db.DlqMessages.AsNoTracking();
        return await query.FirstOrDefaultAsync(m => m.Id == id && m.NamespaceId == ns.Id && m.OwnerId == ns.OwnerId, cancellationToken);
    }

    private Task<EligibilityDecision> EvaluateAsync(
        DlqMessage message, Namespace ns, RecoveryActor actor, RecoveryOperationKind kind, CancellationToken cancellationToken) =>
        _gate.EvaluateAsync(
            new RecoveryEligibilityRequest(
                ns.OwnerId, kind, actor.Kind, actor.Kind == RecoveryActorKind.Automation ? RecoveryTrigger.AutoRule : RecoveryTrigger.Manual,
                ns.Id, message.EntityName, message.BodyHash, SignatureHash: message.SignatureHash, ns.Environment,
                // The namespace's own cloud: whether absence can be proven is that cloud's capability.
                Provider: ns.Provider),
            cancellationToken);

    /// <summary>
    /// True for refusals that happen before or instead of the cloud acting — bad input, not found, forbidden,
    /// conflict, rate limited. Anything else (internal, timeout, upstream) leaves the outcome unknown.
    /// </summary>
    private static bool NothingCanHaveHappened(Error error) =>
        error.Type is ErrorType.Validation or ErrorType.NotFound or ErrorType.Unauthorized or ErrorType.Forbidden
            or ErrorType.Conflict or ErrorType.BusinessRule or ErrorType.RateLimited;

    /// <summary>The queue or topic to receive from, and the subscription when it is one.</summary>
    private static (string Entity, string? Subscription) SourceOf(DlqMessage m) =>
        m.EntityType == ServiceBusEntityType.Subscription ? (m.TopicName ?? TopicPart(m.EntityName), SubscriptionPart(m)) : (m.EntityName, null);

    /// <summary>
    /// A subscription is stored as <c>topic/subscriptions/name</c> (the scanner's one spelling for every cloud), but a cloud is
    /// asked for the subscription by its own bare name. Passing the stored path made every subscription replay fail with
    /// "topic not found".
    /// </summary>
    private static string SubscriptionPart(DlqMessage m)
    {
        const string segment = "/subscriptions/";
        var i = m.EntityName.IndexOf(segment, StringComparison.Ordinal);
        if (i >= 0)
        {
            return m.EntityName[(i + segment.Length)..];
        }

        return m.TopicName is { } topic && m.EntityName.StartsWith(topic + "/", StringComparison.Ordinal) ? m.EntityName[(topic.Length + 1)..] : m.EntityName;
    }

    private static string TopicPart(string entityName)
    {
        var i = entityName.IndexOf('/', StringComparison.Ordinal);
        return i > 0 ? entityName[..i] : entityName;
    }

    /// <summary>Where it goes back to — the queue it came from, or the topic for a subscription.</summary>
    private static string TargetOf(DlqMessage m) => m.TopicName ?? m.EntityName;

    private async Task AuditAsync(
        Namespace ns, RecoveryActor actor, DlqMessage message, string outcome, string? error, string? correlationId, CancellationToken cancellationToken)
    {
        try
        {
            await _audit.RecordAsync(new AuditLog
            {
                Id = Guid.NewGuid(), Timestamp = DateTimeOffset.UtcNow, OwnerId = ns.OwnerId, UserIdentity = actor.Identity,
                Action = AuditActions.ReplayMessage, Outcome = outcome == "accepted" ? AuditActions.Success : AuditActions.Failure, NamespaceId = ns.Id, NamespaceName = ns.Name,
                CloudProvider = ns.Provider.ToString().ToLowerInvariant(), Environment = ns.Environment.ToString(),
                EntityName = message.EntityName, ResourceName = message.MessageId,
                ErrorDetails = error is null ? (outcome == "accepted" ? null : outcome) : LogRedactor.SanitiseForLog(error), CorrelationId = correlationId,
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not record the replay of {Id} in the audit trail", message.Id);
        }
    }
}
