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
/// <see cref="IAIServiceClient"/> — found this way specifically because
/// <c>AnomalyDetectionWorker</c> resolves <see cref="IAIServiceClient"/> from a per-cycle DI
/// scope inside its method body rather than holding it as a field, which a
/// constructor/field-reflection scan would miss entirely.
/// Phase 2 (forbidden-reference scan) is per-type, not per-caller: once a type is AI-adjacent,
/// every method it declares — not only the one Phase 1 matched on — is checked against every
/// mutating <see cref="IRecoveryLedger"/>/<see cref="IMessageOperationsService"/> member.
/// The forbidden-member sets are derived from each interface's own reflected method list minus an
/// explicit read-only allowlist, not hand-copied — a future write method added to either
/// interface without updating this test is caught automatically (fail-closed), where the reverse
/// (a hand-maintained forbidden list) would silently miss it (fail-open).
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
        // this session against current source (roadmap §9.4.5 Pass 11): the AI-adjacent set must
        // include the AI-namespace types plus the two dependency-discovered outliers.
        aiAdjacentTypes.Should().Contain(typeof(ServiceHub.Infrastructure.AI.DeterministicClassifier),
            "namespace-based discovery (route a) should find ServiceHub.Infrastructure.AI types");
        aiAdjacentTypes.Should().Contain(typeof(ServiceHub.Infrastructure.BackgroundServices.AnomalyDetectionWorker),
            "call-site discovery (route b) must find AnomalyDetectionWorker even though it resolves " +
            "IAIServiceClient from a per-cycle DI scope inside its method body rather than holding it " +
            "as a field (roadmap §9.4.5 Pass 11) — a declared-member scan would miss this");
        aiAdjacentTypes.Should().Contain(typeof(ServiceHub.Api.Controllers.V1.AnomaliesController),
            "call-site discovery (route b) must find AnomaliesController's constructor-injected client " +
            "the same way it finds AnomalyDetectionWorker's DI-scope-resolved one");
        aiAdjacentTypes.Count.Should().BeGreaterThanOrEqualTo(13,
            "the eleven ServiceHub.Infrastructure.AI-namespace types plus the two dependency-discovered " +
            "outliers — finding fewer means discovery itself has regressed, not that coverage improved");

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
}
