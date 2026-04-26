using FluentAssertions;
using ServiceHub.Core.Entities;

namespace ServiceHub.UnitTests.Core.Entities;

public sealed class AutoReplayRuleTests
{
    private static AutoReplayRule MakeRule(bool enabled = true) =>
        new AutoReplayRule
        {
            Name = "Test Rule",
            OwnerId = TestConstants.TestOwnerId,
            ConditionsJson = """[{"field":"DeadLetterReason","operator":"Contains","value":"Timeout"}]""",
            ActionsJson = """{"autoReplay":true,"delaySeconds":60}""",
            CreatedAt = DateTimeOffset.UtcNow,
            Enabled = enabled,
        };

    [Fact]
    public void Default_Enabled_IsTrue()
    {
        var rule = MakeRule();
        rule.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Default_MaxReplaysPerHour_Is100()
    {
        var rule = MakeRule();
        rule.MaxReplaysPerHour.Should().Be(100);
    }

    [Fact]
    public void Default_MatchCount_IsZero()
    {
        var rule = MakeRule();
        rule.MatchCount.Should().Be(0);
    }

    [Fact]
    public void Default_SuccessCount_IsZero()
    {
        var rule = MakeRule();
        rule.SuccessCount.Should().Be(0);
    }

    [Fact]
    public void Default_UpdatedAt_IsNull()
    {
        var rule = MakeRule();
        rule.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void Enabled_CanBeSetToFalse()
    {
        var rule = MakeRule();
        rule.Enabled = false;
        rule.Enabled.Should().BeFalse();
    }

    [Fact]
    public void MatchCount_CanBeIncremented()
    {
        var rule = MakeRule();
        rule.MatchCount++;
        rule.MatchCount.Should().Be(1);
    }

    [Fact]
    public void SuccessCount_CanBeIncremented()
    {
        var rule = MakeRule();
        rule.SuccessCount++;
        rule.SuccessCount.Should().Be(1);
    }

    [Fact]
    public void Description_IsOptional()
    {
        var rule = new AutoReplayRule
        {
            Name = "Rule",
            OwnerId = TestConstants.TestOwnerId,
            ConditionsJson = "[]",
            ActionsJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        rule.Description.Should().BeNull();
    }

    [Fact]
    public void ReplayHistories_DefaultsToEmptyCollection()
    {
        var rule = MakeRule();
        rule.ReplayHistories.Should().NotBeNull().And.BeEmpty();
    }
}
