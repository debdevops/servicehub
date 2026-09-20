using System.Reflection;
using FluentAssertions;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.UnitTests.Architecture;

/// <summary>
/// Conformance gate for roadmap §9.4.5 / §15.1 / §33.1 invariants 3–4: AI may propose signals,
/// but no AI-adjacent type may ever create/modify <c>AutonomyGrant</c>, write the Recovery
/// Evidence Ledger, execute recovery, or bypass the Eligibility Gate. Two phases, both built on
/// <see cref="RecoveryPathIlScanner"/> — the identical IL-scan technique
/// <see cref="RecoveryPathCoverageTests"/> already uses for the replay/purge coverage invariant,
/// reused rather than reimplemented so the two tests can never silently drift apart.
/// </summary>
/// <remarks>
/// Phase 1 (AI-adjacent discovery) is dependency-based, not namespace-list-based (roadmap
/// Changelog Pass 11): a type is AI-adjacent if it is declared in
/// <c>ServiceHub.Infrastructure.AI</c>, OR any method it declares directly calls a member of
/// <see cref="IAIServiceClient"/> — route b exists to catch a type that resolves
/// <see cref="IAIServiceClient"/> from a per-cycle DI scope inside a method body rather than
/// holding it as a field, which a constructor/field-reflection scan would miss entirely.
/// (<c>AnomalyDetectionWorker</c> and <c>AnomaliesController</c> previously matched route b before
/// roadmap §5.B I3 replaced their AI-service-gated anomaly detection with deterministic
/// statistics; they no longer call any <see cref="IAIServiceClient"/> member, so route b now
/// correctly excludes them.)
/// Phase 2 (forbidden-reference scan) is per-type, not per-caller: once a type is AI-adjacent,
/// every method it declares — not only the one Phase 1 matched on — is checked against every
/// mutating <see cref="IRecoveryLedger"/>/<see cref="IMessageOperationsService"/> member.
/// The forbidden-member sets are derived from each interface's own reflected method list minus an
/// explicit read-only allowlist, not hand-copied — a future write method added to either
/// interface without updating this test is caught automatically (fail-closed), where the reverse
/// (a hand-maintained forbidden list) would silently miss it (fail-open).
/// <see cref="NoReasoningAgentAdjacentTypeReachesAMutatingLedgerOrProviderMemberOrADisallowedPlaybookLedgerMember"/>
/// extends this same technique to the reasoning companion (roadmap §7, W5): "AIBoundaryArchitectureTests,
/// extended to cover it." A second, narrower forbidden set applies to reasoning-agent-adjacent code
/// only — every <see cref="IPlaybookLedger"/> member except <c>ProposeAsync</c> and its read-only
/// queries, since <c>ProposeAsync</c> is the companion's one legal write anywhere in the system.
/// </remarks>
public sealed class AIBoundaryArchitectureTests
{
    private static readonly Type[] ScanAssemblyMarkers =
    {
        typeof(ServiceHub.Api.Controllers.V1.MessagesController),
        typeof(ServiceHub.Infrastructure.RecoveryLedger.RecoveryLedgerService),
    };

    /// <summary>
    /// Every read-only (non-mutating) <see cref="IRecoveryLedger"/> member, by name — AI-adjacent
    /// code may call these freely. Everything else on the interface is a write member and is
    /// forbidden to AI-adjacent code.
    /// </summary>
    private static readonly HashSet<string> RecoveryLedgerReadMethodNames = new()
    {
        nameof(IRecoveryLedger.VerifyChainAsync),
        nameof(IRecoveryLedger.GetOperationAsync),
        nameof(IRecoveryLedger.QueryOperationsAsync),
        nameof(IRecoveryLedger.QueryEntriesAsync),
        nameof(IRecoveryLedger.GetAgeingAsync),
        nameof(IRecoveryLedger.FindByMarkerAsync),
        nameof(IRecoveryLedger.FindHeuristicRecurrenceCandidatesAsync),
        nameof(IRecoveryLedger.GetEventsForOperationAsync),
        nameof(IRecoveryLedger.HasAgeingFlagAsync),
        nameof(IRecoveryLedger.FindLineageMatchesAsync),
        nameof(IRecoveryLedger.GetDispositionCountsAsync),
        nameof(IRecoveryLedger.GetDistinctSignatureHashesAsync),
        nameof(IRecoveryLedger.GetRecentVerifiedDispositionsAsync),
        nameof(IRecoveryLedger.GetAutonomyGrantAsync),
        nameof(IRecoveryLedger.IsEmergencyStopActiveAsync),
        nameof(IRecoveryLedger.HasUnsafeOutcomeFlagAsync),
        nameof(IRecoveryLedger.HasDuplicateAssociationAsync),
    };

    /// <summary>
    /// Every read-only (non-mutating) <see cref="IMessageOperationsService"/> member, by name.
    /// Everything else (<c>SendAsync</c>, <c>SendBatchAsync</c>, <c>DeadLetterMessagesAsync</c>,
    /// <c>ReplayMessageAsync</c>, <c>PurgeMessageAsync</c>) asks a provider to move or destroy a
    /// message and is forbidden to AI-adjacent code.
    /// </summary>
    private static readonly HashSet<string> MessageOperationsReadMethodNames = new()
    {
        nameof(IMessageOperationsService.PeekMessagesAsync),
        nameof(IMessageOperationsService.PeekDeadLetterMessagesAsync),
        nameof(IMessageOperationsService.GetMessageCountAsync),
        nameof(IMessageOperationsService.GetScheduledMessagesAsync),
    };

    private static readonly HashSet<MethodInfo> ForbiddenMembers = BuildForbiddenMemberSet();

    private static HashSet<MethodInfo> BuildForbiddenMemberSet()
    {
        var forbidden = new HashSet<MethodInfo>();

        foreach (var method in typeof(IRecoveryLedger).GetMethods())
        {
            if (!RecoveryLedgerReadMethodNames.Contains(method.Name))
            {
                forbidden.Add(method);
            }
        }

        foreach (var method in typeof(IMessageOperationsService).GetMethods())
        {
            if (!MessageOperationsReadMethodNames.Contains(method.Name))
            {
                forbidden.Add(method);
            }
        }

        return forbidden;
    }

    [Fact]
    public void NoAiAdjacentTypeReachesAMutatingLedgerOrProviderMember()
    {
        var assemblies = ScanAssemblyMarkers.Select(t => t.Assembly).Distinct().ToList();
        var allTypes = assemblies.SelectMany(a => a.GetTypes()).ToList();

        var aiAdjacentTypes = DiscoverAiAdjacentTypes(allTypes);

        // Canary: a scanner that silently finds nothing would pass vacuously forever. Verified
        // this session against current source (roadmap §5.B I3): AnomalyDetectionWorker and
        // AnomaliesController were deliberately decoupled from IAIServiceClient as part of
        // replacing the anomaly-detection stub with deterministic statistics (no ML, no LLM) —
        // route b no longer finds them, since neither calls any IAIServiceClient member anymore.
        // The AI-adjacent set is now just the namespace-based (route a) types.
        aiAdjacentTypes.Should().Contain(typeof(ServiceHub.Infrastructure.AI.DeterministicClassifier),
            "namespace-based discovery (route a) should find ServiceHub.Infrastructure.AI types");
        aiAdjacentTypes.Should().NotContain(typeof(ServiceHub.Infrastructure.BackgroundServices.AnomalyDetectionWorker),
            "roadmap §5.B I3 replaced its AI-service-gated detection with IAnomalyDetectionService " +
            "(deterministic statistics) — it no longer calls any IAIServiceClient member");
        aiAdjacentTypes.Should().NotContain(typeof(ServiceHub.Api.Controllers.V1.AnomaliesController),
            "roadmap §5.B I3 replaced its AI-service-gated detection with IAnomalyDetectionService " +
            "(deterministic statistics) — it no longer calls any IAIServiceClient member");
        aiAdjacentTypes.Count.Should().BeGreaterThanOrEqualTo(11,
            "the eleven ServiceHub.Infrastructure.AI-namespace types — finding fewer means discovery " +
            "itself has regressed, not that coverage improved");

        var violations = new List<string>();

        foreach (var type in aiAdjacentTypes)
        {
            foreach (var method in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var realBody = RecoveryPathIlScanner.ResolveRealMethodBody(method);
                var calledMethods = RecoveryPathIlScanner.GetDirectlyCalledMethods(realBody);

                foreach (var called in calledMethods)
                {
                    if (called is MethodInfo calledMethodInfo && ForbiddenMembers.Contains(calledMethodInfo))
                    {
                        var (owningType, owningMethodName) = RecoveryPathIlScanner.ResolveOwningMethod(method);
                        violations.Add(
                            $"{owningType.Name}.{owningMethodName} -> " +
                            $"{calledMethodInfo.DeclaringType!.Name}.{calledMethodInfo.Name}");
                    }
                }
            }
        }

        violations.Distinct().Should().BeEmpty(
            "no AI-adjacent type (roadmap §9.4.5: namespace ServiceHub.Infrastructure.AI, or any method " +
            "directly calling an IAIServiceClient member) may directly call any mutating member of " +
            "IRecoveryLedger or IMessageOperationsService — AI may propose signals, but deterministic " +
            "ServiceHub code decides, executes, and records (roadmap §33.1 invariants 1, 3, 4). " +
            "Offender(s): {0}",
            string.Join(", ", violations.Distinct()));
    }

    /// <summary>
    /// Phase 1 (roadmap §9.4.5): a type is AI-adjacent if it is declared in
    /// <c>ServiceHub.Infrastructure.AI</c> (route a), or any method it declares directly calls a
    /// member of <see cref="IAIServiceClient"/> (route b) — checked against each method's real
    /// body, so an async method's IL-invisible wrapper doesn't hide a call its compiler-generated
    /// state machine actually makes.
    /// </summary>
    private static HashSet<Type> DiscoverAiAdjacentTypes(IEnumerable<Type> candidateTypes)
    {
        var aiAdjacent = new HashSet<Type>();

        foreach (var type in candidateTypes)
        {
            if (type.Namespace == "ServiceHub.Infrastructure.AI")
            {
                aiAdjacent.Add(type);
                continue;
            }

            foreach (var method in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var realBody = RecoveryPathIlScanner.ResolveRealMethodBody(method);
                var callsAiServiceClient = RecoveryPathIlScanner.GetDirectlyCalledMethods(realBody)
                    .Any(m => m.DeclaringType == typeof(IAIServiceClient));

                if (callsAiServiceClient)
                {
                    aiAdjacent.Add(type);
                    break;
                }
            }
        }

        return aiAdjacent;
    }

    /// <summary>
    /// Same technique as <see cref="DiscoverAiAdjacentTypes"/>, applied to the reasoning
    /// companion (roadmap §7, W5): a type is reasoning-agent-adjacent if it is declared in
    /// <c>ServiceHub.Infrastructure.Agent</c> (route a — the HTTP client, its health check, and
    /// its evidence mapper), or any method it declares directly calls a member of
    /// <see cref="IReasoningAgentClient"/> (route b — catches <c>ReasoningCompanionWorker</c>,
    /// which lives in <c>ServiceHub.Infrastructure.BackgroundServices</c>, not under
    /// <c>.Agent</c>).
    /// </summary>
    private static HashSet<Type> DiscoverReasoningAgentAdjacentTypes(IEnumerable<Type> candidateTypes)
    {
        var reasoningAgentAdjacent = new HashSet<Type>();

        foreach (var type in candidateTypes)
        {
            if (type.Namespace == "ServiceHub.Infrastructure.Agent")
            {
                reasoningAgentAdjacent.Add(type);
                continue;
            }

            foreach (var method in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var realBody = RecoveryPathIlScanner.ResolveRealMethodBody(method);
                var callsReasoningAgentClient = RecoveryPathIlScanner.GetDirectlyCalledMethods(realBody)
                    .Any(m => m.DeclaringType == typeof(IReasoningAgentClient));

                if (callsReasoningAgentClient)
                {
                    reasoningAgentAdjacent.Add(type);
                    break;
                }
            }
        }

        return reasoningAgentAdjacent;
    }

    /// <summary>
    /// Every <see cref="IPlaybookLedger"/> member the reasoning companion is permitted to call —
    /// <c>ProposeAsync</c> (its only legal write) plus every read-only query member. Everything
    /// else (<c>MarkUnderReviewAsync</c>, <c>ReviseAsync</c>, <c>DispositionAsync</c>,
    /// <c>ExpireAsync</c>, <c>SupersedeAsync</c>, <c>RevokeAsync</c>) records a human decision or
    /// a system-authored lifecycle transition and is forbidden to reasoning-agent-adjacent code —
    /// roadmap §7: "Nothing it produces executes, promotes, or confirms anything by itself."
    /// </summary>
    private static readonly HashSet<string> PlaybookLedgerAllowedForReasoningAgentMethodNames = new()
    {
        nameof(IPlaybookLedger.ProposeAsync),
        nameof(IPlaybookLedger.QueryEntriesAsync),
        nameof(IPlaybookLedger.GetEntryAsync),
        nameof(IPlaybookLedger.GetDueForExpiryAsync),
        nameof(IPlaybookLedger.GetEventsForEntryAsync),
        nameof(IPlaybookLedger.VerifyChainAsync),
    };

    private static readonly HashSet<MethodInfo> ForbiddenPlaybookLedgerMembersForReasoningAgent = BuildForbiddenPlaybookLedgerMemberSet();

    private static HashSet<MethodInfo> BuildForbiddenPlaybookLedgerMemberSet()
    {
        var forbidden = new HashSet<MethodInfo>();

        foreach (var method in typeof(IPlaybookLedger).GetMethods())
        {
            if (!PlaybookLedgerAllowedForReasoningAgentMethodNames.Contains(method.Name))
            {
                forbidden.Add(method);
            }
        }

        return forbidden;
    }

    [Fact]
    public void NoReasoningAgentAdjacentTypeReachesAMutatingLedgerOrProviderMemberOrADisallowedPlaybookLedgerMember()
    {
        var assemblies = ScanAssemblyMarkers.Select(t => t.Assembly).Distinct().ToList();
        var allTypes = assemblies.SelectMany(a => a.GetTypes()).ToList();

        var reasoningAgentAdjacentTypes = DiscoverReasoningAgentAdjacentTypes(allTypes);

        // Canary: verified this session against current source (roadmap §7, W5) —
        // ReasoningCompanionWorker is the only type outside ServiceHub.Infrastructure.Agent that
        // calls an IReasoningAgentClient member; a scanner that finds nothing would pass
        // vacuously forever.
        reasoningAgentAdjacentTypes.Should().Contain(
            typeof(ServiceHub.Infrastructure.Agent.ReasoningAgentClient),
            "namespace-based discovery (route a) should find ServiceHub.Infrastructure.Agent types");
        reasoningAgentAdjacentTypes.Should().Contain(
            typeof(ServiceHub.Infrastructure.BackgroundServices.ReasoningCompanionWorker),
            "ReasoningCompanionWorker calls IReasoningAgentClient.ProposeAsync (route b) despite " +
            "living outside the ServiceHub.Infrastructure.Agent namespace");

        var forbidden = ForbiddenMembers.Concat(ForbiddenPlaybookLedgerMembersForReasoningAgent).ToHashSet();
        var violations = new List<string>();

        foreach (var type in reasoningAgentAdjacentTypes)
        {
            foreach (var method in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var realBody = RecoveryPathIlScanner.ResolveRealMethodBody(method);
                var calledMethods = RecoveryPathIlScanner.GetDirectlyCalledMethods(realBody);

                foreach (var called in calledMethods)
                {
                    if (called is MethodInfo calledMethodInfo && forbidden.Contains(calledMethodInfo))
                    {
                        var (owningType, owningMethodName) = RecoveryPathIlScanner.ResolveOwningMethod(method);
                        violations.Add(
                            $"{owningType.Name}.{owningMethodName} -> " +
                            $"{calledMethodInfo.DeclaringType!.Name}.{calledMethodInfo.Name}");
                    }
                }
            }
        }

        violations.Distinct().Should().BeEmpty(
            "no reasoning-agent-adjacent type (roadmap §7: namespace ServiceHub.Infrastructure.Agent, " +
            "or any method directly calling an IReasoningAgentClient member) may directly call any " +
            "mutating member of IRecoveryLedger or IMessageOperationsService, or any IPlaybookLedger " +
            "member other than ProposeAsync or a read-only query — the reasoning companion proposes, " +
            "it never executes, promotes, or confirms anything itself. Offender(s): {0}",
            string.Join(", ", violations.Distinct()));
    }
}
