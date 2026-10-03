using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Events.Payloads;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;
using ServiceHub.Core.Security;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Security;

namespace ServiceHub.Infrastructure.Webhooks;

/// <summary>
/// <inheritdoc cref="IEscalationDelivery"/>
/// </summary>
/// <remarks>
/// <b>The SSRF guard is not re-implemented and not bypassed.</b> Each channel is sent through its own instance of the copied
/// <see cref="WebhookNotifier"/>, configured with that channel's URL and format and the same pinned-address, no-redirect HTTP
/// handler — so every channel gets exactly the validation the single configured URL gets.
/// </remarks>
public sealed class WebhookChannelSender : IEscalationDelivery
{
    /// <summary>The named HTTP client every channel uses (pinned connect, no redirects).</summary>
    public const string HttpClientName = "webhooks";

    private readonly ServiceHubDbContext _db;
    private readonly IConnectionStringProtector _protector;
    private readonly IHttpClientFactory _http;
    private readonly IEnumerable<IWebhookMessageFormatter> _formatters;
    private readonly IDnsResolver _dns;
    private readonly WebhookOptions _serverChannel;
    private readonly ILoggerFactory _loggers;
    private readonly ILogger<WebhookChannelSender> _logger;
    private readonly TimeProvider _time;

    /// <summary>Creates it.</summary>
    public WebhookChannelSender(
        ServiceHubDbContext db, IConnectionStringProtector protector, IHttpClientFactory http, IEnumerable<IWebhookMessageFormatter> formatters,
        IDnsResolver dns, IOptions<WebhookOptions> serverChannel, ILoggerFactory loggers, TimeProvider? time = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _formatters = formatters ?? throw new ArgumentNullException(nameof(formatters));
        _dns = dns ?? throw new ArgumentNullException(nameof(dns));
        _serverChannel = serverChannel?.Value ?? new WebhookOptions();
        _loggers = loggers ?? throw new ArgumentNullException(nameof(loggers));
        _logger = loggers.CreateLogger<WebhookChannelSender>();
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task DeliverAsync(EscalationRaisedPayload escalation, string ownerId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(escalation);

        // The one the server's configuration names (4.0.0's single-URL model) still works beside Settings' channels.
        if (_serverChannel.Enabled && !string.IsNullOrWhiteSpace(_serverChannel.Url))
        {
            var sent = await SendAsync(_serverChannel.Url, _serverChannel.Format, escalation, cancellationToken).ConfigureAwait(false);
            if (sent.IsFailure)
            {
                _logger.LogWarning("An escalation could not be delivered to the configured webhook: {Error}", sent.Error.Message);
            }
        }

        var channels = await _db.NotificationChannels.Where(c => c.OwnerId == ownerId && c.Enabled).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var channel in channels)
        {
            await DeliverToAsync(channel, escalation, cancellationToken).ConfigureAwait(false);
        }

        if (channels.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<(bool Delivered, string? Error)> SendTestAsync(Guid channelId, string ownerId, CancellationToken cancellationToken)
    {
        var channel = await _db.NotificationChannels.FirstOrDefaultAsync(c => c.Id == channelId && c.OwnerId == ownerId, cancellationToken).ConfigureAwait(false);
        if (channel is null)
        {
            return (false, "That channel was not found.");
        }

        var test = new EscalationRaisedPayload
        {
            Kind = "test", ReasonCode = "TEST", RaisedAtUtc = _time.GetUtcNow(),
            Reason = "This is a test from ServiceHub. When an agent stops and needs a person, the message will look like this.",
        };
        var ok = await DeliverToAsync(channel, test, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (ok, channel.LastError);
    }

    private async Task<bool> DeliverToAsync(NotificationChannel channel, EscalationRaisedPayload escalation, CancellationToken cancellationToken)
    {
        var url = _protector.Unprotect(channel.UrlEncrypted);
        if (url.IsFailure)
        {
            channel.LastError = "The saved address could not be decrypted — set the channel up again.";
            return false;
        }

        var sent = await SendAsync(url.Value, channel.Format, escalation, cancellationToken).ConfigureAwait(false);
        if (sent.IsSuccess)
        {
            channel.LastDeliveredAt = _time.GetUtcNow();
            channel.LastError = null;
            return true;
        }

        var why = LogRedactor.SanitiseForLog(sent.Error.Message);
        channel.LastError = why.Length > 500 ? why[..500] : why;
        _logger.LogWarning("An escalation could not be delivered to channel {ChannelId}", channel.Id);
        return false;
    }

    private async Task<Result> SendAsync(string url, WebhookFormat format, EscalationRaisedPayload e, CancellationToken cancellationToken)
    {
        try
        {
            var notifier = new WebhookNotifier(
                _http.CreateClient(HttpClientName),
                Options.Create(new WebhookOptions { Enabled = true, Url = url, Format = format, PublicUrl = _serverChannel.PublicUrl }),
                _formatters, _loggers.CreateLogger<WebhookNotifier>(), _dns);
            return await notifier.NotifyEscalationAsync(e.Kind, e.ReasonCode, e.Reason, e.NamespaceName, e.Provider, e.Entity, e.EntryId, e.AgentId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Delivery is best-effort by design.
        catch (Exception ex)
        {
            return Result.Failure(Error.ExternalService("Webhook.DeliveryFailed", ex.Message));
        }
#pragma warning restore CA1031
    }
}
