using Microsoft.AspNetCore.Mvc;
using ServiceHub.Core.Constants;
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

    /// <summary>Creates the controller.</summary>
    public RulesController(IRulesService rules) => _rules = rules ?? throw new ArgumentNullException(nameof(rules));

    /// <summary>One cloud's rules, with what actually happened under each.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<RuleView>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] CloudProviderType provider, CancellationToken cancellationToken) =>
        Ok(await _rules.ListAsync(OwnerId, provider, cancellationToken));

    /// <summary>Failures a rule can be created from.</summary>
    [HttpGet("sources")]
    [ProducesResponseType(typeof(IReadOnlyList<RuleSource>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Sources([FromQuery] CloudProviderType provider, CancellationToken cancellationToken) =>
        Ok(await _rules.SourcesAsync(OwnerId, provider, cancellationToken));

    /// <summary>Makes a rule. It starts on.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(RuleView), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateRuleRequest request, CancellationToken cancellationToken)
    {
        var result = await _rules.CreateAsync(
            OwnerId, request.Provider, request.Name, request.Reason, request.EntityName, request.SignatureHash,
            request.MaxPerHour ?? 10, request.WaitSeconds ?? 120, request.BackOff ?? true, cancellationToken);
        return result.IsFailure ? Problem(result.Error) : Created($"/api/v1/rules/{result.Value.Id}", result.Value);
    }

    /// <summary>Turns a rule on or off.</summary>
    [HttpPost("{id:long}/enabled")]
    [ProducesResponseType(typeof(RuleView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetEnabled(long id, [FromBody] EnabledRequest request, CancellationToken cancellationToken)
    {
        var result = await _rules.SetEnabledAsync(OwnerId, id, request.Enabled, cancellationToken);
        return result.IsFailure ? Problem(result.Error) : Ok(result.Value);
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

        return Ok(await _rules.TestAsync(OwnerId, AllowedNamespaceIds, request.Provider, request.Reason, request.EntityName, request.SignatureHash, request.Days ?? 7, cancellationToken));
    }

    /// <summary>A new rule.</summary>
    public sealed record CreateRuleRequest(
        CloudProviderType Provider, string Name, string? Reason, string? EntityName, string? SignatureHash, int? MaxPerHour, int? WaitSeconds, bool? BackOff);

    /// <summary>On or off.</summary>
    public sealed record EnabledRequest(bool Enabled);

    /// <summary>A rule to test.</summary>
    public sealed record TestRuleRequest(CloudProviderType Provider, string? Reason, string? EntityName, string? SignatureHash, int? Days);
}
