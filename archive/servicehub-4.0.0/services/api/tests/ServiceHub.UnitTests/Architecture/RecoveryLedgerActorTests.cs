using System.Reflection;
using FluentAssertions;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.UnitTests.Architecture;

/// <summary>
/// Enforces roadmap §14.1: no method on <see cref="IRecoveryLedger"/> — nor any request type it
/// accepts — may take a caller-supplied actor identity as a bare <c>string</c>. Actor identity
/// enters the ledger exclusively as a server-resolved <see cref="RecoveryActor"/>, produced by
/// <c>ActorIdentityResolver</c>. This fails the build if a future change adds a loose
/// <c>string actor</c>/<c>identity</c> parameter that would let a caller assert its own identity.
/// </summary>
public sealed class RecoveryLedgerActorTests
{
    [Fact]
    public void NoIRecoveryLedgerMethod_AcceptsAStringParameterNamedLikeAnActorIdentity()
    {
        var offending = typeof(IRecoveryLedger).GetMethods()
            .SelectMany(m => m.GetParameters().Select(p => (Method: m, Parameter: p)))
            .Where(x => x.Parameter.ParameterType == typeof(string))
            .Where(x => x.Parameter.Name is not null &&
                        (x.Parameter.Name.Contains("actor", StringComparison.OrdinalIgnoreCase) ||
                         x.Parameter.Name.Contains("identity", StringComparison.OrdinalIgnoreCase)))
            .Select(x => $"{x.Method.Name}({x.Parameter.Name})")
            .ToList();

        offending.Should().BeEmpty(
            "IRecoveryLedger must only accept actor identity via the server-derived RecoveryActor " +
            "type — a string parameter named like an actor/identity would let a caller assert its " +
            "own identity");
    }

    [Fact]
    public void EveryIRecoveryLedgerRequestType_UsingActorIdentity_UsesRecoveryActorType()
    {
        var requestTypes = new[]
        {
            typeof(OpenRecoveryOperationRequest),
            typeof(BeginRecoveryEntryRequest),
            typeof(RecordExecutionRequest),
            typeof(RecordObservationRequest),
        };

        var offending = requestTypes
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(p => p.PropertyType == typeof(string))
            .Where(p => p.Name.Contains("actor", StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Contains("identity", StringComparison.OrdinalIgnoreCase))
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}")
            .ToList();

        offending.Should().BeEmpty(
            "actor identity must be carried as a RecoveryActor property (e.g. 'Actor'), never as " +
            "a bare string named like an identity");
    }
}
