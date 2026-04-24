using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure;

namespace ServiceHub.UnitTests.Infrastructure;

public sealed class RuleEngineTests
{
    private readonly RuleEngine _sut = new(NullLogger<RuleEngine>.Instance);

    // ── Helpers ────────────────────────────────────────────────────

    private static DlqMessage MakeMessage(
        string? reason = null,
        string? desc = null,
        string? bodyPreview = null,
        int deliveryCount = 3,
        string? entityName = "orders-queue",
        string? contentType = "application/json",
        string? appPropsJson = null,
        FailureCategory category = FailureCategory.Unknown)
    {
        return new DlqMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            SequenceNumber = 1,
            BodyHash = "hash",
            NamespaceId = Guid.NewGuid(),
            OwnerId = TestConstants.TestOwnerId,
            EntityName = entityName ?? "orders-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeadLetterReason = reason,
            DeadLetterErrorDescription = desc,
            BodyPreview = bodyPreview,
            DeliveryCount = deliveryCount,
            ContentType = contentType,
            ApplicationPropertiesJson = appPropsJson,
            FailureCategory = category,
        };
    }

    private static RuleCondition Condition(
        string field, string op, string value,
        bool caseSensitive = false, string? propertyKey = null) =>
        new() { Field = field, Operator = op, Value = value, CaseSensitive = caseSensitive, PropertyKey = propertyKey };

    // ═══════════════════════════════════════════════════════════════
    //  Empty conditions
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_EmptyConditions_AlwaysMatches()
    {
        var result = _sut.Evaluate(MakeMessage(), []);
        result.IsMatch.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Contains / NotContains
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_Contains_Matches()
    {
        var msg = MakeMessage(reason: "MaxDeliveryCountExceeded");
        var result = _sut.Evaluate(msg, [Condition("DeadLetterReason", "Contains", "MaxDelivery")]);
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Contains_CaseInsensitive_Matches()
    {
        var msg = MakeMessage(reason: "MaxDeliveryCountExceeded");
        var result = _sut.Evaluate(msg, [Condition("DeadLetterReason", "Contains", "maxdelivery")]);
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Contains_CaseSensitive_NoMatch()
    {
        var msg = MakeMessage(reason: "MaxDeliveryCountExceeded");
        var result = _sut.Evaluate(msg, [Condition("DeadLetterReason", "Contains", "maxdelivery", caseSensitive: true)]);
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_Contains_NullField_NoMatch()
    {
        var msg = MakeMessage(reason: null);
        var result = _sut.Evaluate(msg, [Condition("DeadLetterReason", "Contains", "anything")]);
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_NotContains_Matches()
    {
        var msg = MakeMessage(reason: "TTLExpiredException");
        var result = _sut.Evaluate(msg, [Condition("DeadLetterReason", "NotContains", "MaxDelivery")]);
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_NotContains_NullField_Matches()
    {
        var msg = MakeMessage(reason: null);
        var result = _sut.Evaluate(msg, [Condition("DeadLetterReason", "NotContains", "anything")]);
        result.IsMatch.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Equals / NotEquals
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_Equals_Matches()
    {
        var msg = MakeMessage(entityName: "orders-queue");
        var result = _sut.Evaluate(msg, [Condition("EntityName", "Equals", "orders-queue")]);
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Equals_DifferentValue_NoMatch()
    {
        var msg = MakeMessage(entityName: "orders-queue");
        var result = _sut.Evaluate(msg, [Condition("EntityName", "Equals", "payments-queue")]);
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_NotEquals_Matches()
    {
        var msg = MakeMessage(entityName: "orders-queue");
        var result = _sut.Evaluate(msg, [Condition("EntityName", "NotEquals", "payments-queue")]);
        result.IsMatch.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════
    //  StartsWith / EndsWith
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_StartsWith_Matches()
    {
        var msg = MakeMessage(reason: "MaxDeliveryCountExceeded");
        var result = _sut.Evaluate(msg, [Condition("DeadLetterReason", "StartsWith", "MaxDelivery")]);
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_StartsWith_NoMatch()
    {
        var msg = MakeMessage(reason: "MaxDeliveryCountExceeded");
        var result = _sut.Evaluate(msg, [Condition("DeadLetterReason", "StartsWith", "TTL")]);
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_EndsWith_Matches()
    {
        var msg = MakeMessage(reason: "MaxDeliveryCountExceeded");
        var result = _sut.Evaluate(msg, [Condition("DeadLetterReason", "EndsWith", "Exceeded")]);
        result.IsMatch.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Regex
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_Regex_ValidPattern_Matches()
    {
        var msg = MakeMessage(reason: "MaxDeliveryCountExceeded");
        var result = _sut.Evaluate(msg, [Condition("DeadLetterReason", "Regex", @"Max\w+Exceeded")]);
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Regex_NoMatch()
    {
        var msg = MakeMessage(reason: "TTLExpiredException");
        var result = _sut.Evaluate(msg, [Condition("DeadLetterReason", "Regex", @"^MaxDelivery")]);
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_Regex_InvalidPattern_ReturnsFalse()
    {
        var msg = MakeMessage(reason: "AnyReason");
        var result = _sut.Evaluate(msg, [Condition("DeadLetterReason", "Regex", "[invalid(")]);
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_Regex_NullField_ReturnsFalse()
    {
        var msg = MakeMessage(reason: null);
        var result = _sut.Evaluate(msg, [Condition("DeadLetterReason", "Regex", ".*")]);
        result.IsMatch.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════
    //  GreaterThan / LessThan (numeric)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_GreaterThan_Matches()
    {
        var msg = MakeMessage(deliveryCount: 10);
        var result = _sut.Evaluate(msg, [Condition("DeliveryCount", "GreaterThan", "5")]);
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_GreaterThan_NotGreater_NoMatch()
    {
        var msg = MakeMessage(deliveryCount: 3);
        var result = _sut.Evaluate(msg, [Condition("DeliveryCount", "GreaterThan", "5")]);
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_LessThan_Matches()
    {
        var msg = MakeMessage(deliveryCount: 2);
        var result = _sut.Evaluate(msg, [Condition("DeliveryCount", "LessThan", "5")]);
        result.IsMatch.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════
    //  In
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_In_Matches()
    {
        var msg = MakeMessage(reason: "TTLExpiredException");
        var result = _sut.Evaluate(msg,
            [Condition("DeadLetterReason", "In", "TTLExpiredException,MaxDeliveryCountExceeded")]);
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_In_NotInList_NoMatch()
    {
        var msg = MakeMessage(reason: "SessionFilterMismatch");
        var result = _sut.Evaluate(msg,
            [Condition("DeadLetterReason", "In", "TTLExpiredException,MaxDeliveryCountExceeded")]);
        result.IsMatch.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Unknown operator
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_UnknownOperator_NoMatch()
    {
        var msg = MakeMessage(reason: "anything");
        var result = _sut.Evaluate(msg, [Condition("DeadLetterReason", "Between", "a,z")]);
        result.IsMatch.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Field extraction
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_Field_FailureCategory_Matches()
    {
        var msg = MakeMessage(category: FailureCategory.Transient);
        var result = _sut.Evaluate(msg, [Condition("FailureCategory", "Equals", "Transient")]);
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Field_DeliveryCount_Matches()
    {
        var msg = MakeMessage(deliveryCount: 5);
        var result = _sut.Evaluate(msg, [Condition("DeliveryCount", "Equals", "5")]);
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Field_ContentType_Matches()
    {
        var msg = MakeMessage(contentType: "application/json");
        var result = _sut.Evaluate(msg, [Condition("ContentType", "Contains", "json")]);
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Field_ApplicationProperty_ValidJson_Matches()
    {
        var msg = MakeMessage(appPropsJson: """{"eventType":"OrderCreated"}""");
        var result = _sut.Evaluate(msg,
            [Condition("ApplicationProperty", "Equals", "OrderCreated", propertyKey: "eventType")]);
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Field_ApplicationProperty_MissingKey_NoMatch()
    {
        var msg = MakeMessage(appPropsJson: """{"otherKey":"value"}""");
        var result = _sut.Evaluate(msg,
            [Condition("ApplicationProperty", "Equals", "OrderCreated", propertyKey: "eventType")]);
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_Field_ApplicationProperty_InvalidJson_NoMatch()
    {
        var msg = MakeMessage(appPropsJson: "not valid json");
        var result = _sut.Evaluate(msg,
            [Condition("ApplicationProperty", "Equals", "anything", propertyKey: "key")]);
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_Field_UnknownField_NoMatch()
    {
        var msg = MakeMessage(reason: "anything");
        var result = _sut.Evaluate(msg, [Condition("NonExistentField", "Equals", "value")]);
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_MultipleConditions_AllMustMatch()
    {
        var msg = MakeMessage(reason: "MaxDeliveryCountExceeded", deliveryCount: 10);
        var conditions = new[]
        {
            Condition("DeadLetterReason", "Contains", "MaxDelivery"),
            Condition("DeliveryCount", "GreaterThan", "5"),
        };

        var result = _sut.Evaluate(msg, conditions);
        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_MultipleConditions_FirstFails_ReturnsFalse()
    {
        var msg = MakeMessage(reason: "TTLExpiredException", deliveryCount: 10);
        var conditions = new[]
        {
            Condition("DeadLetterReason", "Contains", "MaxDelivery"),
            Condition("DeliveryCount", "GreaterThan", "5"),
        };

        var result = _sut.Evaluate(msg, conditions);
        result.IsMatch.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Match result metadata
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_Match_IncludesMessageId()
    {
        var msg = MakeMessage();
        var result = _sut.Evaluate(msg, []);
        result.ServiceBusMessageId.Should().Be(msg.MessageId);
        result.EntityName.Should().Be(msg.EntityName);
    }

    [Fact]
    public void Evaluate_NoMatch_IncludesFailureReason()
    {
        var msg = MakeMessage(reason: "TTL");
        var result = _sut.Evaluate(msg, [Condition("DeadLetterReason", "Contains", "MaxDelivery")]);
        result.IsMatch.Should().BeFalse();
        result.MatchReason.Should().NotBeNullOrEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    //  EvaluateBatch
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void EvaluateBatch_ReturnsOneResultPerMessage()
    {
        var messages = new[]
        {
            MakeMessage(reason: "MaxDeliveryCountExceeded"),
            MakeMessage(reason: "TTLExpiredException"),
            MakeMessage(reason: "SessionFilterMismatch"),
        };
        var conditions = new[] { Condition("DeadLetterReason", "Contains", "MaxDelivery") };

        var results = _sut.EvaluateBatch(messages, conditions);

        results.Should().HaveCount(3);
        results[0].IsMatch.Should().BeTrue();
        results[1].IsMatch.Should().BeFalse();
        results[2].IsMatch.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════
    //  FindMatchingRules
    // ═══════════════════════════════════════════════════════════════

    private static AutoReplayRule MakeRule(
        bool enabled,
        string conditionsJson,
        string? actionsJson = null)
    {
        return new AutoReplayRule
        {
            Name = "Test Rule",
            OwnerId = TestConstants.TestOwnerId,
            Enabled = enabled,
            ConditionsJson = conditionsJson,
            ActionsJson = actionsJson ?? """{"autoReplay":true,"delaySeconds":30}""",
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    [Fact]
    public void FindMatchingRules_DisabledRule_IsSkipped()
    {
        var msg = MakeMessage(reason: "MaxDeliveryCountExceeded");
        var rules = new[]
        {
            MakeRule(enabled: false, conditionsJson: """[{"field":"DeadLetterReason","operator":"Contains","value":"MaxDelivery"}]"""),
        };

        var matches = _sut.FindMatchingRules(msg, rules);
        matches.Should().BeEmpty();
    }

    [Fact]
    public void FindMatchingRules_InvalidJson_IsSkippedSafely()
    {
        var msg = MakeMessage(reason: "anything");
        var rules = new[]
        {
            MakeRule(enabled: true, conditionsJson: "not json at all"),
        };

        var act = () => _sut.FindMatchingRules(msg, rules);
        act.Should().NotThrow();
        act().Should().BeEmpty();
    }

    [Fact]
    public void FindMatchingRules_MatchingRule_IsReturned()
    {
        var msg = MakeMessage(reason: "MaxDeliveryCountExceeded");
        var rules = new[]
        {
            MakeRule(enabled: true,
                conditionsJson: """[{"field":"DeadLetterReason","operator":"Contains","value":"MaxDelivery"}]"""),
        };

        var matches = _sut.FindMatchingRules(msg, rules);
        matches.Should().HaveCount(1);
    }

    [Fact]
    public void FindMatchingRules_NonMatchingRule_IsNotReturned()
    {
        var msg = MakeMessage(reason: "TTLExpiredException");
        var rules = new[]
        {
            MakeRule(enabled: true,
                conditionsJson: """[{"field":"DeadLetterReason","operator":"Contains","value":"MaxDelivery"}]"""),
        };

        var matches = _sut.FindMatchingRules(msg, rules);
        matches.Should().BeEmpty();
    }

    [Fact]
    public void FindMatchingRules_MultipleMatchingRules_AllReturned()
    {
        var msg = MakeMessage(reason: "MaxDeliveryCountExceeded");
        var rules = new[]
        {
            MakeRule(enabled: true,
                conditionsJson: """[{"field":"DeadLetterReason","operator":"Contains","value":"MaxDelivery"}]"""),
            MakeRule(enabled: true,
                conditionsJson: """[{"field":"EntityName","operator":"Contains","value":"orders"}]"""),
        };

        var matches = _sut.FindMatchingRules(msg, rules);
        matches.Should().HaveCount(2);
    }
}
