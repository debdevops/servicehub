using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Authorization;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Controller for auto-replay rule operations.
/// Provides CRUD, testing, templates, and statistics for DLQ replay rules.
/// </summary>
[Route(ApiRoutes.Dlq.Rules.Base)]
[Tags("Auto-Replay Rules")]
public sealed class RulesController : ApiControllerBase
{
    private readonly DlqDbContext _dbContext;
    private readonly IRuleEngine _ruleEngine;
    private readonly ILogger<RulesController> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="RulesController"/> class.
    /// </summary>
    public RulesController(
        DlqDbContext dbContext,
        IRuleEngine ruleEngine,
        ILogger<RulesController> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _ruleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── 1. GET /api/v1/dlq/rules — List all rules ──────────────

    /// <summary>
    /// Gets all auto-replay rules, optionally filtered.
    /// </summary>
    /// <param name="enabledOnly">Only return enabled rules.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of rules.</returns>
    [HttpGet]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(typeof(IReadOnlyList<RuleResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RuleResponse>>> GetAll(
        [FromQuery] bool? enabledOnly = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _dbContext.AutoReplayRules.AsNoTracking().AsQueryable();

            if (enabledOnly == true)
                query = query.Where(r => r.Enabled);

            var rules = await query
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync(cancellationToken);

            var response = rules.Select(MapToResponse).ToList();
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list auto-replay rules");
            return ToActionResult<IReadOnlyList<RuleResponse>>(
                Error.Internal(ErrorCodes.Rule.NotFound, "Failed to retrieve rules"));
        }
    }

    // ── 2. GET /api/v1/dlq/rules/{id} — Get single rule ────────

    /// <summary>
    /// Gets a single auto-replay rule by ID.
    /// </summary>
    /// <param name="id">The rule ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rule details.</returns>
    [HttpGet("{id:long}")]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(typeof(RuleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RuleResponse>> GetById(
        long id,
        CancellationToken cancellationToken = default)
    {
        var rule = await _dbContext.AutoReplayRules
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (rule is null)
            return ToActionResult<RuleResponse>(
                Error.NotFound(ErrorCodes.Rule.NotFound, $"Rule {id} not found"));

        return Ok(MapToResponse(rule));
    }

    // ── 3. POST /api/v1/dlq/rules — Create rule ────────────────

    /// <summary>
    /// Creates a new auto-replay rule.
    /// </summary>
    /// <param name="request">The rule definition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created rule.</returns>
    [HttpPost]
    [RequireScope(ApiKeyScopes.DlqWrite)]
    [ProducesResponseType(typeof(RuleResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RuleResponse>> Create(
        [FromBody] CreateRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check for duplicate name
            var exists = await _dbContext.AutoReplayRules
                .AnyAsync(r => r.Name == request.Name, cancellationToken);

            if (exists)
                return ToActionResult<RuleResponse>(
                    Error.Conflict(ErrorCodes.Rule.AlreadyExists, $"A rule named '{request.Name}' already exists"));

            var entity = new AutoReplayRule
            {
                Name = request.Name,
                Description = request.Description,
                Enabled = request.Enabled,
                ConditionsJson = JsonSerializer.Serialize(request.Conditions, JsonOptions),
                ActionsJson = JsonSerializer.Serialize(request.Action, JsonOptions),
                CreatedAt = DateTimeOffset.UtcNow,
                MaxReplaysPerHour = request.MaxReplaysPerHour,
            };

            _dbContext.AutoReplayRules.Add(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created auto-replay rule {RuleId}/{RuleName}", entity.Id, entity.Name);

            return CreatedAtAction(
                nameof(GetById),
                new { id = entity.Id },
                MapToResponse(entity));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create auto-replay rule");
            return ToActionResult<RuleResponse>(
                Error.Internal(ErrorCodes.Rule.SaveFailed, "Failed to create rule"));
        }
    }

    // ── 4. PUT /api/v1/dlq/rules/{id} — Update rule ────────────

    /// <summary>
    /// Updates an existing auto-replay rule.
    /// </summary>
    /// <param name="id">The rule ID.</param>
    /// <param name="request">The updated rule definition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated rule.</returns>
    [HttpPut("{id:long}")]
    [RequireScope(ApiKeyScopes.DlqWrite)]
    [ProducesResponseType(typeof(RuleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RuleResponse>> Update(
        long id,
        [FromBody] CreateRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rule = await _dbContext.AutoReplayRules
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

            if (rule is null)
                return ToActionResult<RuleResponse>(
                    Error.NotFound(ErrorCodes.Rule.NotFound, $"Rule {id} not found"));

            // Check for duplicate name (excluding current rule)
            var duplicate = await _dbContext.AutoReplayRules
                .AnyAsync(r => r.Name == request.Name && r.Id != id, cancellationToken);

            if (duplicate)
                return ToActionResult<RuleResponse>(
                    Error.Conflict(ErrorCodes.Rule.AlreadyExists, $"A rule named '{request.Name}' already exists"));

            // AutoReplayRule uses init-only properties for Name/ConditionsJson/ActionsJson/Description,
            // so we need to remove the old entity and add a new one preserving stats.
            var updatedRule = new AutoReplayRule
            {
                Name = request.Name,
                Description = request.Description,
                Enabled = request.Enabled,
                ConditionsJson = JsonSerializer.Serialize(request.Conditions, JsonOptions),
                ActionsJson = JsonSerializer.Serialize(request.Action, JsonOptions),
                CreatedAt = rule.CreatedAt,
                UpdatedAt = DateTimeOffset.UtcNow,
                MatchCount = rule.MatchCount,
                SuccessCount = rule.SuccessCount,
                MaxReplaysPerHour = request.MaxReplaysPerHour,
            };

            _dbContext.AutoReplayRules.Remove(rule);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _dbContext.AutoReplayRules.Add(updatedRule);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated auto-replay rule {RuleId}/{RuleName}", updatedRule.Id, updatedRule.Name);

            return Ok(MapToResponse(updatedRule));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update auto-replay rule {RuleId}", id);
            return ToActionResult<RuleResponse>(
                Error.Internal(ErrorCodes.Rule.SaveFailed, "Failed to update rule"));
        }
    }

    // ── 5. DELETE /api/v1/dlq/rules/{id} — Delete rule ─────────

    /// <summary>
    /// Deletes an auto-replay rule.
    /// </summary>
    /// <param name="id">The rule ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No content on success.</returns>
    [HttpDelete("{id:long}")]
    [RequireScope(ApiKeyScopes.DlqWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        long id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rule = await _dbContext.AutoReplayRules
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

            if (rule is null)
                return ToActionResult(
                    Result.Failure(Error.NotFound(ErrorCodes.Rule.NotFound, $"Rule {id} not found")));

            _dbContext.AutoReplayRules.Remove(rule);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Deleted auto-replay rule {RuleId}/{RuleName}", rule.Id, rule.Name);

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete rule {RuleId}", id);
            return ToActionResult(
                Result.Failure(Error.Internal(ErrorCodes.Rule.DeleteFailed, "Failed to delete rule")));
        }
    }

    // ── 6. POST /api/v1/dlq/rules/{id}/toggle — Toggle rule ────

    /// <summary>
    /// Toggles an auto-replay rule's enabled/disabled state.
    /// </summary>
    /// <param name="id">The rule ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated rule.</returns>
    [HttpPost("{id:long}/toggle")]
    [RequireScope(ApiKeyScopes.DlqWrite)]
    [ProducesResponseType(typeof(RuleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RuleResponse>> Toggle(
        long id,
        CancellationToken cancellationToken = default)
    {
        var rule = await _dbContext.AutoReplayRules
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (rule is null)
            return ToActionResult<RuleResponse>(
                Error.NotFound(ErrorCodes.Rule.NotFound, $"Rule {id} not found"));

        rule.Enabled = !rule.Enabled;
        rule.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Toggled rule {RuleId} to {State}", rule.Id, rule.Enabled ? "enabled" : "disabled");

        return Ok(MapToResponse(rule));
    }

    // ── 7. POST /api/v1/dlq/rules/test — Test a rule ───────────

    /// <summary>
    /// Tests a rule (or ad-hoc conditions) against current active DLQ messages.
    /// Returns how many messages would match without actually replaying.
    /// </summary>
    /// <param name="request">The test request with conditions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Test results showing matched messages.</returns>
    [HttpPost("test")]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(typeof(RuleTestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RuleTestResponse>> TestRule(
        [FromBody] TestRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Resolve conditions
            List<RuleCondition> conditions;

            if (request.RuleId.HasValue)
            {
                var rule = await _dbContext.AutoReplayRules
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Id == request.RuleId.Value, cancellationToken);

                if (rule is null)
                    return ToActionResult<RuleTestResponse>(
                        Error.NotFound(ErrorCodes.Rule.NotFound, $"Rule {request.RuleId} not found"));

                conditions = JsonSerializer.Deserialize<List<RuleCondition>>(rule.ConditionsJson) ?? [];
            }
            else if (request.Conditions is { Count: > 0 })
            {
                conditions = request.Conditions.ToList();
            }
            else
            {
                return ToActionResult<RuleTestResponse>(
                    Error.Validation(ErrorCodes.Rule.ValidationFailed, "Either RuleId or Conditions must be provided"));
            }

            // Get active messages to test against
            var query = _dbContext.DlqMessages
                .AsNoTracking()
                .Where(m => m.Status == DlqMessageStatus.Active);

            if (request.NamespaceId.HasValue)
                query = query.Where(m => m.NamespaceId == request.NamespaceId.Value);

            var messages = await query
                .OrderByDescending(m => m.DetectedAtUtc)
                .Take(request.MaxMessages)
                .ToListAsync(cancellationToken);

            var results = _ruleEngine.EvaluateBatch(messages, conditions);
            var matched = results.Where(r => r.IsMatch).ToList();

            // Estimate success rate based on failure category of matched messages
            var matchedMessages = messages.Where(m => matched.Any(r => r.MessageId == m.Id)).ToList();
            var transientCount = matchedMessages.Count(m =>
                m.FailureCategory is FailureCategory.Transient or FailureCategory.MaxDelivery or FailureCategory.Expired);
            var estimatedSuccessRate = matchedMessages.Count > 0
                ? (double)transientCount / matchedMessages.Count
                : 0.0;

            var sampleMatches = matched.Take(10).Select(r => new RuleMatchResultResponse(
                MessageId: r.MessageId,
                ServiceBusMessageId: r.ServiceBusMessageId,
                EntityName: r.EntityName,
                IsMatch: r.IsMatch,
                MatchReason: r.MatchReason,
                DeadLetterReason: r.DeadLetterReason
            )).ToList();

            var response = new RuleTestResponse(
                TotalTested: messages.Count,
                MatchedCount: matched.Count,
                EstimatedSuccessRate: Math.Round(estimatedSuccessRate * 100, 1),
                SampleMatches: sampleMatches);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test rule");
            return ToActionResult<RuleTestResponse>(
                Error.Internal(ErrorCodes.Rule.TestFailed, "Failed to test rule"));
        }
    }

    // ── 8. GET /api/v1/dlq/rules/templates — Rule templates ────

    /// <summary>
    /// Gets a catalog of pre-built rule templates.
    /// </summary>
    /// <returns>List of rule templates.</returns>
    [HttpGet("templates")]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(typeof(IReadOnlyList<RuleTemplateResponse>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<RuleTemplateResponse>> GetTemplates()
    {
        var templates = GetBuiltInTemplates();
        return Ok(templates);
    }

    // ── Mapping ─────────────────────────────────────────────────

    private static RuleResponse MapToResponse(AutoReplayRule rule)
    {
        var conditions = DeserializeOrDefault<List<RuleCondition>>(rule.ConditionsJson) ?? [];
        var action = DeserializeOrDefault<RuleAction>(rule.ActionsJson) ?? new RuleAction();
        var successRate = rule.MatchCount > 0
            ? Math.Round((double)rule.SuccessCount / rule.MatchCount * 100, 1)
            : 0.0;

        return new RuleResponse(
            Id: rule.Id,
            Name: rule.Name,
            Description: rule.Description,
            Enabled: rule.Enabled,
            Conditions: conditions,
            Action: action,
            CreatedAt: rule.CreatedAt,
            UpdatedAt: rule.UpdatedAt,
            MatchCount: rule.MatchCount,
            SuccessCount: rule.SuccessCount,
            SuccessRate: successRate,
            MaxReplaysPerHour: rule.MaxReplaysPerHour);
    }

    private static T? DeserializeOrDefault<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    // ── Templates ───────────────────────────────────────────────

    private static IReadOnlyList<RuleTemplateResponse> GetBuiltInTemplates()
    {
        return new List<RuleTemplateResponse>
        {
            new(
                Id: "database-timeouts",
                Name: "Database Timeouts",
                Description: "Auto-replay messages that failed due to database connection timeouts. These are typically transient failures that resolve when the database recovers.",
                Category: "Transient",
                Conditions: [
                    new RuleCondition { Field = "DeadLetterReason", Operator = "Contains", Value = "timeout" },
                    new RuleCondition { Field = "DeadLetterErrorDescription", Operator = "Contains", Value = "database" },
                ],
                Action: new RuleAction { AutoReplay = true, DelaySeconds = 300, MaxRetries = 3, ExponentialBackoff = true },
                UsageCount: 47,
                Rating: 4.8
            ),
            new(
                Id: "payment-gateway-timeouts",
                Name: "Payment Gateway Timeouts",
                Description: "Auto-replay messages that failed due to payment gateway timeouts. Adds longer delay to allow gateway recovery.",
                Category: "Transient",
                Conditions: [
                    new RuleCondition { Field = "DeadLetterErrorDescription", Operator = "Contains", Value = "payment" },
                    new RuleCondition { Field = "DeadLetterErrorDescription", Operator = "Contains", Value = "timeout" },
                ],
                Action: new RuleAction { AutoReplay = true, DelaySeconds = 120, MaxRetries = 3, ExponentialBackoff = true },
                UsageCount: 23,
                Rating: 4.5
            ),
            new(
                Id: "max-delivery-exceeded",
                Name: "Max Delivery Exceeded",
                Description: "Replay messages that exceeded max delivery count. Often caused by transient processing failures that resolve on retry.",
                Category: "MaxDelivery",
                Conditions: [
                    new RuleCondition { Field = "FailureCategory", Operator = "Equals", Value = "MaxDelivery" },
                ],
                Action: new RuleAction { AutoReplay = true, DelaySeconds = 60, MaxRetries = 1 },
                UsageCount: 85,
                Rating: 4.2
            ),
            new(
                Id: "expired-messages",
                Name: "Expired Messages",
                Description: "Re-send messages that expired (TTL exceeded) before being processed. Useful for non-time-sensitive workloads.",
                Category: "Expired",
                Conditions: [
                    new RuleCondition { Field = "FailureCategory", Operator = "Equals", Value = "Expired" },
                ],
                Action: new RuleAction { AutoReplay = true, DelaySeconds = 30, MaxRetries = 1 },
                UsageCount: 31,
                Rating: 3.9
            ),
            new(
                Id: "transient-network-errors",
                Name: "Transient Network Errors",
                Description: "Auto-replay messages that failed due to transient network errors including connection resets and DNS failures.",
                Category: "Transient",
                Conditions: [
                    new RuleCondition { Field = "FailureCategory", Operator = "Equals", Value = "Transient" },
                ],
                Action: new RuleAction { AutoReplay = true, DelaySeconds = 180, MaxRetries = 3, ExponentialBackoff = true },
                UsageCount: 62,
                Rating: 4.6
            ),
            new(
                Id: "resource-not-found",
                Name: "Resource Not Found Retries",
                Description: "Retry messages that failed because a resource was not yet available (eventual consistency scenarios).",
                Category: "ResourceNotFound",
                Conditions: [
                    new RuleCondition { Field = "FailureCategory", Operator = "Equals", Value = "ResourceNotFound" },
                    new RuleCondition { Field = "DeliveryCount", Operator = "LessThan", Value = "5" },
                ],
                Action: new RuleAction { AutoReplay = true, DelaySeconds = 600, MaxRetries = 2 },
                UsageCount: 18,
                Rating: 3.7
            ),
            new(
                Id: "quota-exceeded",
                Name: "Quota/Throttling Recovery",
                Description: "Replay messages that failed due to rate limiting or quota exceeded errors, with longer delays.",
                Category: "QuotaExceeded",
                Conditions: [
                    new RuleCondition { Field = "FailureCategory", Operator = "Equals", Value = "QuotaExceeded" },
                ],
                Action: new RuleAction { AutoReplay = true, DelaySeconds = 900, MaxRetries = 2, ExponentialBackoff = true },
                UsageCount: 14,
                Rating: 4.1
            ),
        };
    }
}
