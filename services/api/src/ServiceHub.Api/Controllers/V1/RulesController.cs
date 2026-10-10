using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Security;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Auto Replay rules (unit 3.6). A rule is a failure and a pace — there is no rule language. Making or changing one never
/// sends anything: the Auto Replay agent applies rules, and only through the eligibility gate.
/// </summary>
[Route("api/v1/rules")]
public sealed class RulesController : ApiControllerBase
{
    private readonly IRulesService _rules;
    private readonly IAuditTrail _audit;

    /// <summary>Creates the controller.</summary>
    public RulesController(IRulesService rules, IAuditTrail audit)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    // A rule is a standing instruction that lets a machine replay on its own, so every change to one is on the audit trail with who made it.
    private Task AuditAsync(string action, string resource, bool succeeded, CancellationToken cancellationToken) =>
        _audit.RecordAsync(new AuditLog
        {
            Id = Guid.NewGuid(), Timestamp = DateTimeOffset.UtcNow, OwnerId = OwnerId, UserIdentity = Actor.Identity,
            Action = action, Outcome = succeeded ? AuditActions.Success : AuditActions.Failure, ResourceName = resource,
            CorrelationId = HttpContext.TraceIdentifier, HttpMethod = Request.Method, HttpPath = Request.Path.Value,
        }, cancellationToken);

    /// <summary>One cloud's rules, with what actually happened under each.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<RuleView>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] CloudProviderType? provider, CancellationToken cancellationToken) =>
        provider is { } cloud ? Ok(await _rules.ListAsync(OwnerId, cloud, cancellationToken)) : NeedsCloud();

    /// <summary>Failures a rule can be created from.</summary>
    [HttpGet("sources")]
    [ProducesResponseType(typeof(IReadOnlyList<RuleSource>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Sources([FromQuery] CloudProviderType? provider, CancellationToken cancellationToken) =>
        provider is { } cloud ? Ok(await _rules.SourcesAsync(OwnerId, AllowedNamespaceIds, cloud, cancellationToken)) : NeedsCloud();

    /// <summary>The distinct dead letters this cloud's rules are holding for a person — one message counted once however many rules match it.</summary>
    [HttpGet("held")]
    [ProducesResponseType(typeof(RulesHeld), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Held([FromQuery] CloudProviderType? provider, CancellationToken cancellationToken) =>
        provider is { } cloud ? Ok(await _rules.HeldAsync(OwnerId, AllowedNamespaceIds, cloud, cancellationToken)) : NeedsCloud();

    // Making, switching, changing or deleting a rule decides what a machine may replay, so it must be meant — a stray DELETE (a pasted URL, a script) is refused.
    private ObjectResult? NeedsIntent(string intent, string words) =>
        IntentHeaders.Declares(Request, intent)
            ? null
            : Problem(StatusCodes.Status428PreconditionRequired, ErrorCodes.IntentRequired, IntentHeaders.MissingDetail(words, intent));

    // A rule belongs to one cloud, and an enum left out would quietly mean the first one — so a missing cloud is refused, never defaulted.
    private ObjectResult NeedsCloud() => Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "Say which cloud: give a 'provider'.");

    /// <summary>Makes a rule. It starts on.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(RuleView), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateRuleRequest request, CancellationToken cancellationToken)
    {
        if (NeedsIntent(IntentHeaders.CreateRule, "make an auto-replay rule") is { } missing) return missing;
        if (request.Provider is not { } cloud)
        {
            return NeedsCloud();
        }

        // A rule starts on — it hands a machine the right to replay — so creating one is an Approver's call, like switching one on.
        if (await DeniedUnlessAsync(GovernanceRole.Approver, null, PillarKind.Recover, "create an auto-replay rule", cancellationToken) is { } denied) return denied;
        var result = await _rules.CreateAsync(
            OwnerId, cloud, request.Name, request.Reason, request.EntityName, request.SignatureHash,
            request.MaxPerHour ?? 10, request.WaitSeconds ?? 120, request.BackOff ?? true, cancellationToken);
        await AuditAsync(AuditActions.RuleCreate, result.IsSuccess ? $"{result.Value.Id} · {result.Value.Name}" : request.Name, result.IsSuccess, cancellationToken);
        return result.IsFailure ? Problem(result.Error) : Created($"/api/v1/rules/{result.Value.Id}", result.Value);
    }

    /// <summary>Turns a rule on or off.</summary>
    [HttpPost("{id:long}/enabled")]
    [ProducesResponseType(typeof(RuleView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetEnabled(long id, [FromBody] EnabledRequest request, CancellationToken cancellationToken)
    {
        if (NeedsIntent(IntentHeaders.SwitchRule, "switch an auto-replay rule on or off") is { } missing) return missing;
        // Switching a rule on hands a machine the right to replay: an Approver's call. Switching it off only takes that away.
        if (await DeniedUnlessAsync(request.Enabled ? GovernanceRole.Approver : GovernanceRole.Operator, null, PillarKind.Recover,
                request.Enabled ? "switch an auto-replay rule on" : "switch an auto-replay rule off", cancellationToken) is { } denied) return denied;
        var result = await _rules.SetEnabledAsync(OwnerId, id, request.Enabled, cancellationToken);
        await AuditAsync(AuditActions.RuleToggle, $"{id} · {(request.Enabled ? "on" : "off")}", result.IsSuccess, cancellationToken);
        return result.IsFailure ? Problem(result.Error) : Ok(result.Value);
    }

    /// <summary>Changes a rule's name and pace. Raising the pace widens what a machine may do, so it is an Approver's call, like making a rule.</summary>
    [HttpPut("{id:long}")]
    [ProducesResponseType(typeof(RuleView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateRuleRequest request, CancellationToken cancellationToken)
    {
        if (NeedsIntent(IntentHeaders.UpdateRule, "change an auto-replay rule") is { } missing) return missing;
        if (await DeniedUnlessAsync(GovernanceRole.Approver, null, PillarKind.Recover, "change an auto-replay rule", cancellationToken) is { } denied) return denied;
        var result = await _rules.UpdateAsync(OwnerId, id, request.Name, request.MaxPerHour ?? 10, request.WaitSeconds ?? 120, request.BackOff ?? true, cancellationToken);
        await AuditAsync(AuditActions.RuleUpdate, $"{id} · {request.Name}", result.IsSuccess, cancellationToken);
        return result.IsFailure ? Problem(result.Error) : Ok(result.Value);
    }

    /// <summary>Deletes a rule. Only takes authority away, so an Operator may — like switching one off. Its replays stay in the ledger.</summary>
    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        if (NeedsIntent(IntentHeaders.DeleteRule, "delete an auto-replay rule") is { } missing) return missing;
        if (await DeniedUnlessAsync(GovernanceRole.Operator, null, PillarKind.Recover, "delete an auto-replay rule", cancellationToken) is { } denied) return denied;
        var result = await _rules.DeleteAsync(OwnerId, id, cancellationToken);
        await AuditAsync(AuditActions.RuleDelete, id.ToString(System.Globalization.CultureInfo.InvariantCulture), result.IsSuccess, cancellationToken);
        return result.IsFailure ? Problem(result.Error) : NoContent();
    }

    /// <summary>The dead letters the rule matches right now. Sends nothing; Replay all hands these to the bulk preview.</summary>
    [HttpGet("{id:long}/matches")]
    [ProducesResponseType(typeof(IReadOnlyList<long>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Matches(long id, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        var result = await _rules.MatchesAsync(OwnerId, AllowedNamespaceIds, id, limit ?? 500, cancellationToken);
        return result.IsFailure ? Problem(result.Error) : Ok(result.Value);
    }

    /// <summary>Makes rules for the most common failures nothing covers yet. Each starts on, so it is an Approver's call.</summary>
    [HttpPost("generate")]
    [ProducesResponseType(typeof(IReadOnlyList<RuleView>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Generate([FromBody] GenerateRulesRequest request, CancellationToken cancellationToken)
    {
        if (NeedsIntent(IntentHeaders.GenerateRules, "make auto-replay rules") is { } missing) return missing;
        if (request.Provider is not { } cloud)
        {
            return NeedsCloud();
        }

        if (await DeniedUnlessAsync(GovernanceRole.Approver, null, PillarKind.Recover, "create auto-replay rules", cancellationToken) is { } denied) return denied;
        var made = await _rules.GenerateAsync(OwnerId, AllowedNamespaceIds, cloud, request.Max ?? 5, cancellationToken);
        await AuditAsync(AuditActions.RuleGenerate, $"{cloud} · {made.Count} rule(s)", true, cancellationToken);
        return Ok(made);
    }

    /// <summary>What a rule would have done over the last days, by today's checks. Sends nothing.</summary>
    [HttpPost("test")]
    [ProducesResponseType(typeof(RuleTest), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Test([FromBody] TestRuleRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) && string.IsNullOrWhiteSpace(request.EntityName) && string.IsNullOrWhiteSpace(request.SignatureHash))
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "Give at least one condition to test.");
        }

        if (request.Provider is not { } cloud)
        {
            return NeedsCloud();
        }

        return Ok(await _rules.TestAsync(OwnerId, AllowedNamespaceIds, cloud, request.Reason, request.EntityName, request.SignatureHash, request.Days ?? 7, cancellationToken));
    }

    /// <summary>A new rule.</summary>
    public sealed record CreateRuleRequest(
        CloudProviderType? Provider, string Name, string? Reason, string? EntityName, string? SignatureHash, int? MaxPerHour, int? WaitSeconds, bool? BackOff);

    /// <summary>A rule's new name and pace.</summary>
    public sealed record UpdateRuleRequest(string Name, int? MaxPerHour, int? WaitSeconds, bool? BackOff);

    /// <summary>Which cloud to make rules for, and at most how many.</summary>
    public sealed record GenerateRulesRequest(CloudProviderType? Provider, int? Max);

    /// <summary>On or off.</summary>
    public sealed record EnabledRequest(bool Enabled);

    /// <summary>A rule to test.</summary>
    public sealed record TestRuleRequest(CloudProviderType? Provider, string? Reason, string? EntityName, string? SignatureHash, int? Days);
}
