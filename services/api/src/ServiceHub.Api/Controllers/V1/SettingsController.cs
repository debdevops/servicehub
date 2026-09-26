using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceHub.Api.Security;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Events;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// What the Settings modal reads and changes (units 6.3, 6.10): notification channels, the security facts, and emergency stop.
/// Connections are the namespaces API; preferences live in the browser.
/// </summary>
/// <remarks>
/// <b>The bell is not here to switch off</b> — it cannot be (R7). A channel's URL is a secret: stored encrypted, never returned.
/// Emergency stop is an Admin's, with a typed confirmation (owner decision O3): it stops ServiceHub acting on its own; replays a
/// person starts still go through their checks.
/// </remarks>
[Route("api/v1/settings")]
public sealed class SettingsController : ApiControllerBase
{
    private const string StopWord = "STOP";

    private readonly ServiceHubDbContext _db;
    private readonly IConnectionStringProtector _protector;
    private readonly IEscalationDelivery _delivery;
    private readonly IRecoveryLedger _ledger;
    private readonly IConfiguration _configuration;
    private readonly WebhookOptions _serverChannel;
    private readonly IPlatformEventBus? _bus;

    /// <summary>Creates the controller.</summary>
    public SettingsController(
        ServiceHubDbContext db, IConnectionStringProtector protector, IEscalationDelivery delivery, IRecoveryLedger ledger,
        IConfiguration configuration, IOptions<WebhookOptions> serverChannel, IPlatformEventBus? bus = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _serverChannel = serverChannel?.Value ?? new WebhookOptions();
        _bus = bus;
    }

    /// <summary>Everything the Settings modal shows that the server knows.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var channels = await _db.NotificationChannels.AsNoTracking().Where(c => c.OwnerId == OwnerId).OrderBy(c => c.CreatedAt).ToListAsync(cancellationToken);
        var keys = _configuration.GetSection("Security:Authentication:ApiKeys").GetChildren()
            .Count(k => !string.IsNullOrWhiteSpace(k.GetChildren().Any() ? k["Key"] : k.Value));
        return Ok(new
        {
            notifications = new
            {
                bellAlwaysOn = true,
                serverChannel = _serverChannel.Enabled && !string.IsNullOrWhiteSpace(_serverChannel.Url)
                    ? new { format = _serverChannel.Format.ToString().ToLowerInvariant(), setBy = "the server's configuration (Webhooks__Url)" }
                    : null,
                channels = channels.Select(ToView),
            },
            security = new { apiKeysConfigured = keys, credentialsEncryptedAtRest = true, keyFingerprint = _protector.GetKeyFingerprint() },
            emergencyStop = await StopViewAsync(cancellationToken),
        });
    }

    /// <summary>Sets up a Slack, Teams or generic webhook channel. Admin. Intent <c>add-channel</c>.</summary>
    [HttpPost("channels")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> AddChannel([FromBody] AddChannelRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IntentHeaders.Declares(Request, IntentHeaders.AddChannel))
        {
            return Problem(StatusCodes.Status428PreconditionRequired, ErrorCodes.IntentRequired, IntentHeaders.MissingDetail("add a notification channel", IntentHeaders.AddChannel));
        }

        if (await DeniedUnlessAsync(GovernanceRole.Admin, null, null, "set up notifications", cancellationToken) is { } denied) return denied;

        // The full SSRF check runs on every send (same guard as 4.0.0); here only the obvious is refused, in words.
        if (!Uri.TryCreate(request.Url?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "Paste the webhook's full https:// address.");
        }

        var label = string.IsNullOrWhiteSpace(request.Label) ? DefaultLabel(request.Format) : request.Label.Trim();
        if (label.Length > 120)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "Keep the name under 120 characters.");
        }

        var encrypted = _protector.Protect(uri.ToString());
        if (encrypted.IsFailure)
        {
            return Problem(encrypted.Error);
        }

        var channel = new NotificationChannel
        {
            OwnerId = OwnerId, Format = request.Format, Label = label, UrlEncrypted = encrypted.Value,
            CreatedAt = DateTimeOffset.UtcNow, CreatedBy = Actor.Identity,
        };
        _db.NotificationChannels.Add(channel);
        await _db.SaveChangesAsync(cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ToView(channel));
    }

    /// <summary>Turns a channel on or off. Admin.</summary>
    [HttpPost("channels/{id:guid}/enabled")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetEnabled(Guid id, [FromBody] EnabledRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await DeniedUnlessAsync(GovernanceRole.Admin, null, null, "change notifications", cancellationToken) is { } denied) return denied;
        var channel = await _db.NotificationChannels.FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == OwnerId, cancellationToken);
        if (channel is null) return Problem(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "That channel was not found.");
        channel.Enabled = request.Enabled;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(ToView(channel));
    }

    /// <summary>Removes a channel. Admin. Intent <c>remove-channel</c>.</summary>
    [HttpDelete("channels/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> RemoveChannel(Guid id, CancellationToken cancellationToken)
    {
        if (!IntentHeaders.Declares(Request, IntentHeaders.RemoveChannel))
        {
            return Problem(StatusCodes.Status428PreconditionRequired, ErrorCodes.IntentRequired, IntentHeaders.MissingDetail("remove this channel", IntentHeaders.RemoveChannel));
        }

        if (await DeniedUnlessAsync(GovernanceRole.Admin, null, null, "remove a notification channel", cancellationToken) is { } denied) return denied;
        var channel = await _db.NotificationChannels.FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == OwnerId, cancellationToken);
        if (channel is null) return Problem(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "That channel was not found.");
        _db.NotificationChannels.Remove(channel);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Sends one clearly-marked test message to a channel and says how it went. Operator.</summary>
    [HttpPost("channels/{id:guid}/test")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Test(Guid id, CancellationToken cancellationToken)
    {
        if (await DeniedUnlessAsync(GovernanceRole.Operator, null, null, "send a test message", cancellationToken) is { } denied) return denied;
        var (delivered, error) = await _delivery.SendTestAsync(id, OwnerId, cancellationToken);
        return Ok(new { delivered, error });
    }

    /// <summary>Whether emergency stop is on — for the banner every screen shows while it is. Anyone.</summary>
    [HttpGet("emergency-stop")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> EmergencyStop(CancellationToken cancellationToken) => Ok(await StopViewAsync(cancellationToken));

    /// <summary>
    /// Switches emergency stop on or off (owner decision O3): Admin, intent <c>emergency-stop</c>. Switching it on needs a reason
    /// and the word STOP typed — it is meant to be deliberate, not a stray click.
    /// </summary>
    [HttpPost("emergency-stop")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> SetEmergencyStop([FromBody] EmergencyStopRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IntentHeaders.Declares(Request, IntentHeaders.EmergencyStop))
        {
            return Problem(StatusCodes.Status428PreconditionRequired, ErrorCodes.IntentRequired, IntentHeaders.MissingDetail("change emergency stop", IntentHeaders.EmergencyStop));
        }

        if (await DeniedUnlessAsync(GovernanceRole.Admin, null, null, request.Active ? "switch emergency stop on" : "switch emergency stop off", cancellationToken) is { } denied) return denied;

        if (request.Active && (string.IsNullOrWhiteSpace(request.Reason) || !string.Equals(request.Confirm?.Trim(), StopWord, StringComparison.Ordinal)))
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, $"Say why, and type {StopWord} to confirm.");
        }

        var current = await _ledger.GetEmergencyStopStateAsync(OwnerId, cancellationToken);
        if (current.Active != request.Active)
        {
            var written = await _ledger.RecordEmergencyControlEventAsync(OwnerId, Actor, request.Active, request.Reason?.Trim(), cancellationToken);
            if (written.IsFailure) return Problem(written.Error);
            if (_bus is not null)
            {
                // Every open screen should look again: the banner appears or goes.
                await _bus.PublishAsync(new PlatformEvent
                {
                    Source = "settings", Category = EventCategories.Escalation, EventType = EventTypes.ReplayCompleted, Actor = OwnerId,
                }, cancellationToken);
            }
        }

        return Ok(await StopViewAsync(cancellationToken));
    }

    private async Task<object> StopViewAsync(CancellationToken cancellationToken)
    {
        var s = await _ledger.GetEmergencyStopStateAsync(OwnerId, cancellationToken);
        return new { active = s.Active, by = s.By is null ? null : RecoveryActorLabel.For(s.By), at = s.At, reason = s.Reason };
    }

    private static object ToView(NotificationChannel c) => new
    {
        c.Id, format = c.Format.ToString().ToLowerInvariant(), c.Label, c.Enabled, c.CreatedAt,
        createdBy = RecoveryActorLabel.For(c.CreatedBy), c.LastDeliveredAt, c.LastError,
    };

    private static string DefaultLabel(WebhookFormat format) => format switch
    {
        WebhookFormat.Slack => "Slack",
        WebhookFormat.Teams => "Microsoft Teams",
        _ => "Webhook",
    };

    /// <summary>A new channel.</summary>
    /// <param name="Format">slack · teams · generic.</param>
    /// <param name="Label">What to call it, e.g. #ops-alerts.</param>
    /// <param name="Url">The webhook address — stored encrypted, never shown again.</param>
    public sealed record AddChannelRequest(WebhookFormat Format, string? Label, string? Url);

    /// <summary>On or off.</summary>
    public sealed record EnabledRequest(bool Enabled);

    /// <summary>Emergency stop on or off.</summary>
    /// <param name="Active">On (true) or off.</param>
    /// <param name="Reason">Why — required to switch it on.</param>
    /// <param name="Confirm">The word STOP, to switch it on.</param>
    public sealed record EmergencyStopRequest(bool Active, string? Reason, string? Confirm);
}
