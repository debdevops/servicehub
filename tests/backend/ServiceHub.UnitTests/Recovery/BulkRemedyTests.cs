using FluentAssertions;
using ServiceHub.Core.Constants;
using ServiceHub.Infrastructure.BulkOperations;

namespace ServiceHub.UnitTests.Recovery;

/// <summary>
/// A message held back from a bulk replay because an earlier attempt was never confirmed is not unlocked by an approver —
/// only by saying what happened to that attempt. Its remedy must say so rather than fall through to "an approver decides".
/// </summary>
public sealed class BulkRemedyTests
{
    [Fact]
    public void An_unknown_earlier_outcome_is_not_told_to_wait_for_an_approver()
    {
        var remedy = BulkOperationService.Remedy(EscalationReasons.ReplayOutcomeUnknown);

        remedy.Should().Contain("Say what happened");
        remedy.Should().NotBe(BulkOperationService.Remedy("SOMETHING_NEW").Replace("SOMETHING_NEW", EscalationReasons.ReplayOutcomeUnknown));
    }
}
