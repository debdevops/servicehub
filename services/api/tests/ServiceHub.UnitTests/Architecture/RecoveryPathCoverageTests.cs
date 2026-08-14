using System.Reflection;
using FluentAssertions;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.UnitTests.Architecture;

/// <summary>
/// Conformance gate for the Recovery Evidence Ledger's central invariant (roadmap §5.2, §10.4,
/// §17.3, release gate G2): every code path that asks a provider to move or destroy a message
/// must obtain a ledger entry. Concretely: every method whose IL directly calls
/// <see cref="IMessageOperationsService.ReplayMessageAsync"/> or
/// <see cref="IMessageOperationsService.PurgeMessageAsync"/> must, in that same method body,
/// also call something on <see cref="IRecoveryLedger"/> — the one interface every wired
/// executor/controller uses to record the attempt.
/// </summary>
/// <remarks>
/// Scans compiled IL rather than source text so the check survives renames/reformatting and
/// catches a genuinely new, unwired call site the way a text-based grep could not. C# compiles
/// every <c>async</c> method into a compiler-generated state-machine type whose <c>MoveNext</c>
/// carries the real call sites — this walks those transparently, attributing findings back to
/// the human-authored method (<see cref="RecoveryPathIlScanner.ResolveOwningMethod"/>).
/// </remarks>
public sealed class RecoveryPathCoverageTests
{
    /// <summary>
    /// Methods intentionally exempt, keyed as "TypeName.MethodName", each with the reason it's
    /// safe. Empty by design — see roadmap release gate G2 ("passes with an empty exemption
    /// list"). Adding an entry here is a deliberate decision to be justified in review.
    /// </summary>
    private static readonly Dictionary<string, string> Exemptions = new();

    [Fact]
    public void EveryCallerOfReplayOrPurgeMessageAsyncAlsoCallsTheRecoveryLedgerInTheSameMethod()
    {
        var assemblies = new[]
        {
            typeof(ServiceHub.Api.Controllers.V1.MessagesController).Assembly,
            typeof(ServiceHub.Infrastructure.RecoveryLedger.RecoveryLedgerService).Assembly,
        };

        var violations = new List<string>();
        var callersFound = new HashSet<string>();

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    var calledMethods = RecoveryPathIlScanner.GetDirectlyCalledMethods(method).ToList();

                    var callsReplayOrPurge = calledMethods.Any(m =>
                        m.DeclaringType == typeof(IMessageOperationsService)
                        && (m.Name == nameof(IMessageOperationsService.ReplayMessageAsync)
                            || m.Name == nameof(IMessageOperationsService.PurgeMessageAsync)));

                    if (!callsReplayOrPurge)
                    {
                        continue;
                    }

                    var (owningType, owningMethodName) = RecoveryPathIlScanner.ResolveOwningMethod(method);
                    var key = $"{owningType.Name}.{owningMethodName}";
                    callersFound.Add(key);

                    if (Exemptions.ContainsKey(key))
                    {
                        continue;
                    }

                    var callsRecoveryLedger = calledMethods.Any(m => m.DeclaringType == typeof(IRecoveryLedger));

                    if (!callsRecoveryLedger)
                    {
                        violations.Add(key);
                    }
                }
            }
        }

        // A scanner that silently finds nothing would pass vacuously forever — this is the
        // canary. The seven recovery paths compile down to six distinct (type, method) callers
        // (BulkOperationExecutor.ProcessMessageAsync makes both its replay and purge calls from
        // the same method).
        callersFound.Should().HaveCountGreaterThanOrEqualTo(6,
            "the IL scan should find every known caller of ReplayMessageAsync/PurgeMessageAsync " +
            "(MessagesController x2, BulkOperationExecutor, SignatureReplayExecutor, " +
            "AutoReplayExecutor, RulesController) — finding fewer means the scanner itself has " +
            "regressed, not that coverage improved");

        violations.Distinct().Should().BeEmpty(
            "every method that calls IMessageOperationsService.ReplayMessageAsync/PurgeMessageAsync " +
            "must also call IRecoveryLedger in the same method — either the call is unrecorded (a " +
            "regression of the Recovery Evidence Ledger's core guarantee), or it is a deliberate, " +
            "reviewed exception that belongs in RecoveryPathCoverageTests.Exemptions with a written reason. " +
            "Offender(s): {0}",
            string.Join(", ", violations.Distinct()));
    }

    /// <summary>
    /// Roadmap §9/Phase B, §27 acceptance criterion 1: every code path that calls
    /// <see cref="IMessageOperationsService.ReplayMessageAsync"/> or
    /// <see cref="IMessageOperationsService.PurgeMessageAsync"/> must also call
    /// <see cref="IRecoveryEligibilityGate.EvaluateAsync"/> in that same method body — the
    /// deterministic safety-decision point every recovery attempt passes through, human or
    /// automation, before the provider is ever contacted. Same scanner, same exemption
    /// mechanism, and the same canary as
    /// <see cref="EveryCallerOfReplayOrPurgeMessageAsyncAlsoCallsTheRecoveryLedgerInTheSameMethod"/> —
    /// deliberately re-uses <see cref="Exemptions"/> so the two invariants can never silently
    /// diverge on which callers are exempt.
    /// </summary>
    [Fact]
    public void EveryCallerOfReplayOrPurgeMessageAsyncAlsoCallsTheEligibilityGateInTheSameMethod()
    {
        var assemblies = new[]
        {
            typeof(ServiceHub.Api.Controllers.V1.MessagesController).Assembly,
            typeof(ServiceHub.Infrastructure.RecoveryLedger.RecoveryLedgerService).Assembly,
        };

        var violations = new List<string>();
        var callersFound = new HashSet<string>();

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    var calledMethods = RecoveryPathIlScanner.GetDirectlyCalledMethods(method).ToList();

                    var callsReplayOrPurge = calledMethods.Any(m =>
                        m.DeclaringType == typeof(IMessageOperationsService)
                        && (m.Name == nameof(IMessageOperationsService.ReplayMessageAsync)
                            || m.Name == nameof(IMessageOperationsService.PurgeMessageAsync)));

                    if (!callsReplayOrPurge)
                    {
                        continue;
                    }

                    var (owningType, owningMethodName) = RecoveryPathIlScanner.ResolveOwningMethod(method);
                    var key = $"{owningType.Name}.{owningMethodName}";
                    callersFound.Add(key);

                    if (Exemptions.ContainsKey(key))
                    {
                        continue;
                    }

                    // Two hops, not one: MessagesController.ReplayMessage/PurgeMessage delegate
                    // their gate check to the shared private TryBeginRecoveryAsync helper rather
                    // than duplicating message-lookup/claim logic per caller — a legitimate
                    // same-type delegation pattern, not a bypass. Bounded to private helpers
                    // declared on the same type (not an arbitrary transitive call graph) so a
                    // genuine bypass elsewhere still fails this test.
                    var callsEligibilityGate = CallsInterfaceWithinTwoHops(
                        calledMethods, owningType, typeof(IRecoveryEligibilityGate));

                    if (!callsEligibilityGate)
                    {
                        violations.Add(key);
                    }
                }
            }
        }

        callersFound.Should().HaveCountGreaterThanOrEqualTo(6,
            "the IL scan should find every known caller of ReplayMessageAsync/PurgeMessageAsync " +
            "(MessagesController x2, BulkOperationExecutor, SignatureReplayExecutor, " +
            "AutoReplayExecutor, RulesController) — finding fewer means the scanner itself has " +
            "regressed, not that coverage improved");

        violations.Distinct().Should().BeEmpty(
            "every method that calls IMessageOperationsService.ReplayMessageAsync/PurgeMessageAsync " +
            "must also call IRecoveryEligibilityGate in the same method — either the attempt bypasses " +
            "the deterministic safety gate (a regression of roadmap §9's core guarantee), or it is a " +
            "deliberate, reviewed exception that belongs in RecoveryPathCoverageTests.Exemptions with " +
            "a written reason. Offender(s): {0}",
            string.Join(", ", violations.Distinct()));
    }

    /// <summary>
    /// True if <paramref name="directCalls"/> calls <paramref name="targetInterface"/> directly,
    /// or calls a private helper declared on <paramref name="owningType"/> that itself does
    /// (one hop of same-type delegation — e.g. <c>MessagesController.TryBeginRecoveryAsync</c>).
    /// Does not follow calls into any other type, so a genuine bypass elsewhere still fails.
    /// </summary>
    private static bool CallsInterfaceWithinTwoHops(
        IReadOnlyList<MethodBase> directCalls, Type owningType, Type targetInterface)
    {
        if (directCalls.Any(m => m.DeclaringType == targetInterface))
        {
            return true;
        }

        foreach (var call in directCalls.Where(m => m.DeclaringType == owningType))
        {
            var realBody = RecoveryPathIlScanner.ResolveRealMethodBody(call);
            if (RecoveryPathIlScanner.GetDirectlyCalledMethods(realBody).Any(m => m.DeclaringType == targetInterface))
            {
                return true;
            }
        }

        return false;
    }
}
