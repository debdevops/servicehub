using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure;

/// <summary>
/// Evaluates auto-replay rule conditions against DLQ messages.
/// Supports field extraction, multiple operators, and batch evaluation.
/// </summary>
public sealed class RuleEngine : IRuleEngine
{
    private readonly ILogger<RuleEngine> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RuleEngine"/> class.
    /// </summary>
    public RuleEngine(ILogger<RuleEngine> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public RuleMatchResult Evaluate(DlqMessage message, IReadOnlyList<RuleCondition> conditions)
    {
        foreach (var condition in conditions)
        {
            var fieldValue = ExtractField(message, condition.Field, condition.PropertyKey);
            if (!MatchCondition(fieldValue, condition))
            {
                return new RuleMatchResult
                {
                    MessageId = message.Id,
                    ServiceBusMessageId = message.MessageId,
                    EntityName = message.EntityName,
                    IsMatch = false,
                    MatchReason = $"Condition failed: {condition.Field} {condition.Operator} '{condition.Value}'",
                    DeadLetterReason = message.DeadLetterReason,
                };
            }
        }

        return new RuleMatchResult
        {
            MessageId = message.Id,
            ServiceBusMessageId = message.MessageId,
            EntityName = message.EntityName,
            IsMatch = true,
            MatchReason = $"All {conditions.Count} condition(s) matched",
            DeadLetterReason = message.DeadLetterReason,
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<RuleMatchResult> EvaluateBatch(
        IReadOnlyList<DlqMessage> messages,
        IReadOnlyList<RuleCondition> conditions)
    {
        var results = new List<RuleMatchResult>(messages.Count);
        foreach (var message in messages)
        {
            results.Add(Evaluate(message, conditions));
        }
        return results;
    }

    /// <inheritdoc />
    public IReadOnlyList<(AutoReplayRule Rule, RuleAction Action)> FindMatchingRules(
        DlqMessage message,
        IReadOnlyList<AutoReplayRule> rules)
    {
        var matches = new List<(AutoReplayRule, RuleAction)>();

        foreach (var rule in rules)
        {
            if (!rule.Enabled)
                continue;

            try
            {
                var conditions = JsonSerializer.Deserialize<List<RuleCondition>>(rule.ConditionsJson);
                var action = JsonSerializer.Deserialize<RuleAction>(rule.ActionsJson);

                if (conditions is null || action is null)
                {
                    _logger.LogWarning("Rule {RuleId} has invalid JSON, skipping", rule.Id);
                    continue;
                }

                var result = Evaluate(message, conditions);
                if (result.IsMatch)
                {
                    matches.Add((rule, action));
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize rule {RuleId} JSON", rule.Id);
            }
        }

        return matches;
    }

    // ── Field Extraction ───────────────────────────────────────────

    private static string? ExtractField(DlqMessage message, string field, string? propertyKey)
    {
        return field.ToUpperInvariant() switch
        {
            "DEADLETTERREASON" => message.DeadLetterReason,
            "DEADLETTERERRORDESCRIPTION" => message.DeadLetterErrorDescription,
            "FAILURECATEGORY" => message.FailureCategory.ToString(),
            "ENTITYNAME" => message.EntityName,
            "DELIVERYCOUNT" => message.DeliveryCount.ToString(),
            "CONTENTTYPE" => message.ContentType,
            "TOPICNAME" => message.TopicName,
            "CORRELATIONID" => message.CorrelationId,
            "STATUS" => message.Status.ToString(),
            "BODYPREVIEW" => message.BodyPreview,
            "APPLICATIONPROPERTY" => ExtractApplicationProperty(message, propertyKey),
            _ => null,
        };
    }

    private static string? ExtractApplicationProperty(DlqMessage message, string? propertyKey)
    {
        if (string.IsNullOrWhiteSpace(propertyKey) || string.IsNullOrWhiteSpace(message.ApplicationPropertiesJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(message.ApplicationPropertiesJson);
            if (doc.RootElement.TryGetProperty(propertyKey, out var prop))
            {
                return prop.ValueKind == JsonValueKind.String
                    ? prop.GetString()
                    : prop.GetRawText();
            }
        }
        catch (JsonException)
        {
            // invalid JSON — ignore
        }

        return null;
    }

    // ── Condition Matching ──────────────────────────────────────────

    private static bool MatchCondition(string? fieldValue, RuleCondition condition)
    {
        var comparison = condition.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        return condition.Operator.ToUpperInvariant() switch
        {
            "CONTAINS" => fieldValue?.Contains(condition.Value, comparison) == true,
            "NOTCONTAINS" => fieldValue is null || !fieldValue.Contains(condition.Value, comparison),
            "EQUALS" => string.Equals(fieldValue, condition.Value, comparison),
            "NOTEQUALS" => !string.Equals(fieldValue, condition.Value, comparison),
            "STARTSWITH" => fieldValue?.StartsWith(condition.Value, comparison) == true,
            "ENDSWITH" => fieldValue?.EndsWith(condition.Value, comparison) == true,
            "REGEX" => MatchRegex(fieldValue, condition.Value, condition.CaseSensitive),
            "GREATERTHAN" => CompareNumeric(fieldValue, condition.Value) > 0,
            "LESSTHAN" => CompareNumeric(fieldValue, condition.Value) < 0,
            "IN" => MatchIn(fieldValue, condition.Value, comparison),
            _ => false,
        };
    }

    private static bool MatchRegex(string? fieldValue, string pattern, bool caseSensitive)
    {
        if (fieldValue is null)
            return false;

        try
        {
            var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            return Regex.IsMatch(fieldValue, pattern, options, TimeSpan.FromSeconds(1));
        }
        catch (RegexParseException)
        {
            return false;
        }
    }

    private static int CompareNumeric(string? fieldValue, string conditionValue)
    {
        if (double.TryParse(fieldValue, out var a) && double.TryParse(conditionValue, out var b))
            return a.CompareTo(b);

        return string.Compare(fieldValue, conditionValue, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchIn(string? fieldValue, string commaSeparatedValues, StringComparison comparison)
    {
        if (fieldValue is null)
            return false;

        var values = commaSeparatedValues.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return values.Any(v => string.Equals(fieldValue, v, comparison));
    }
}
