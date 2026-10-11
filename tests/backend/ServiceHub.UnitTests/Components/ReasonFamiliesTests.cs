using System.Text.RegularExpressions;
using FluentAssertions;
using ServiceHub.Core.Components;
using ServiceHub.Core.Constants;
using ServiceHub.UnitTests.Architecture;

namespace ServiceHub.UnitTests.Components;

/// <summary>Every way ServiceHub stops has a family, so a new stop reason cannot ship without one (plan D7, unit 0.7).</summary>
public sealed class ReasonFamiliesTests
{
    [Fact]
    public void Every_reason_code_escalation_reasons_knows_has_a_family()
    {
        // Read from the source, because most codes are string literals in a switch, not constants.
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot.Directory.FullName, "services/api/src/ServiceHub.Core/Constants/EscalationReasons.cs"));
        var codes = Regex.Matches(source, "\"([A-Z][A-Z_]{5,})\"").Select(m => m.Groups[1].Value).Distinct().ToList();

        codes.Should().HaveCountGreaterThan(10, "the scan must actually find the codes");
        codes.Where(c => ReasonFamilies.FamilyOf(c) is null).Should().BeEmpty("a stop reason with no family would show no label");
    }

    [Fact]
    public void A_family_is_never_given_to_a_code_that_does_not_exist()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot.Directory.FullName, "services/api/src/ServiceHub.Core/Constants/EscalationReasons.cs"));

        ReasonFamilies.KnownCodes.Where(c => !source.Contains($"\"{c}\"", StringComparison.Ordinal)).Should().BeEmpty();
    }

    [Fact]
    public void An_unknown_code_gets_no_label_rather_than_a_guess()
    {
        ReasonFamilies.FamilyOf("SOMETHING_NEW").Should().BeNull();
        ReasonFamilies.FamilyOf(null).Should().BeNull();
    }

    [Fact]
    public void The_families_the_screens_rely_on_are_stable()
    {
        ReasonFamilies.FamilyOf("PROVIDER_CANNOT_VERIFY_ABSENCE").Should().Be(ReasonFamily.Unverified);
        ReasonFamilies.FamilyOf("RECURRENCE_CAP_EXCEEDED").Should().Be(ReasonFamily.RetryBudgetExhausted);
        ReasonFamilies.FamilyOf(EscalationReasons.ReplayOutcomeUnknown).Should().Be(ReasonFamily.OutcomeUnknown);
        ReasonFamilies.FamilyOf(EscalationReasons.AgentStale).Should().Be(ReasonFamily.AgentHealth);
    }
}
