using FluentAssertions;
using ServiceHub.Core.Components;

namespace ServiceHub.UnitTests.Components;

/// <summary>The recoverability boundary: the class is calculated from the facts, and the safe side is the default.</summary>
public sealed class RecoverabilityTests
{
    [Theory]
    [InlineData(CopyKind.Held, RecoveryPathKind.Adapter, ProofLevel.NotReturned, false, RecoverabilityClass.R1ReplayableProvable)]
    [InlineData(CopyKind.Held, RecoveryPathKind.Adapter, ProofLevel.Processed, false, RecoverabilityClass.R1ReplayableProvable)]
    [InlineData(CopyKind.Held, RecoveryPathKind.Adapter, ProofLevel.Accepted, false, RecoverabilityClass.R2ReplayableUnproven)]
    [InlineData(CopyKind.Held, RecoveryPathKind.Adapter, ProofLevel.None, false, RecoverabilityClass.R2ReplayableUnproven)]
    [InlineData(CopyKind.Held, RecoveryPathKind.Native, ProofLevel.None, false, RecoverabilityClass.R3CloudRedriveOnly)]
    [InlineData(CopyKind.None, RecoveryPathKind.Resubmit, ProofLevel.None, false, RecoverabilityClass.R3CloudRedriveOnly)]
    [InlineData(CopyKind.Pointer, RecoveryPathKind.Adapter, ProofLevel.NotReturned, false, RecoverabilityClass.R4PointerOnly)]
    [InlineData(CopyKind.None, RecoveryPathKind.None, ProofLevel.None, true, RecoverabilityClass.R5DetectOnly)]
    [InlineData(CopyKind.Held, RecoveryPathKind.None, ProofLevel.None, false, RecoverabilityClass.R5DetectOnly)]
    [InlineData(CopyKind.None, RecoveryPathKind.None, ProofLevel.None, false, RecoverabilityClass.R6NotRecoverable)]
    public void The_class_follows_from_the_facts(CopyKind copy, RecoveryPathKind path, ProofLevel proof, bool signal, RecoverabilityClass expected) =>
        new RecoverabilityFacts(copy, path, proof, signal).Classify(validated: true).Should().Be(expected);

    [Fact]
    public void Nothing_is_classified_above_unknown_until_a_live_run_backs_it()
    {
        // The strongest claim possible, unvalidated, is still "unknown": no replay, a person decides.
        var best = new RecoverabilityFacts(CopyKind.Held, RecoveryPathKind.Adapter, ProofLevel.Processed);

        best.Classify(validated: false).Should().Be(RecoverabilityClass.R0Unknown);
        RecoverabilityClass.R0Unknown.OffersReplay().Should().BeFalse();
        RecoverabilityClass.R0Unknown.IsHandOffOnly().Should().BeTrue();
    }

    [Fact]
    public void Two_components_with_the_same_facts_get_the_same_class()
    {
        var a = new RecoverabilityFacts(CopyKind.Held, RecoveryPathKind.Adapter, ProofLevel.Accepted);
        var b = new RecoverabilityFacts(CopyKind.Held, RecoveryPathKind.Adapter, ProofLevel.Accepted);

        a.Classify(true).Should().Be(b.Classify(true));
    }

    [Theory]
    [InlineData(RecoverabilityClass.R0Unknown, false, false, false)]
    [InlineData(RecoverabilityClass.R1ReplayableProvable, true, false, true)]
    [InlineData(RecoverabilityClass.R2ReplayableUnproven, true, false, false)]
    [InlineData(RecoverabilityClass.R3CloudRedriveOnly, false, true, false)]
    [InlineData(RecoverabilityClass.R4PointerOnly, true, false, false)]
    [InlineData(RecoverabilityClass.R5DetectOnly, false, false, false)]
    [InlineData(RecoverabilityClass.R6NotRecoverable, false, false, false)]
    public void Each_class_allows_exactly_what_the_boundary_says(RecoverabilityClass c, bool replay, bool redrive, bool autonomy)
    {
        c.OffersReplay().Should().Be(replay);
        c.OffersCloudRedrive().Should().Be(redrive);
        // Only a provable replay may ever read Verified or earn the right to run without asking.
        c.MayEarnAutonomy().Should().Be(autonomy);
    }

    [Fact]
    public void A_class_that_offers_no_move_is_hand_off_only_and_never_both()
    {
        foreach (var c in Enum.GetValues<RecoverabilityClass>())
        {
            (c.IsHandOffOnly() && (c.OffersReplay() || c.OffersCloudRedrive())).Should().BeFalse($"{c} cannot both hand off and offer a move");
            (c.IsHandOffOnly() || c.OffersReplay() || c.OffersCloudRedrive()).Should().BeTrue($"{c} must have exactly one answer");
        }
    }
}
