using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using ServiceHub.Core.Constants;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Security;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Looks at the messages in a namespace's queues and dead-letter queues, live, a page at a time (unit 2.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Read-only, except one send.</b> Nothing here moves, replays or deletes a message: anything that changes an existing
/// message goes through the ledger and the eligibility gate (units 2.5–2.7). The one write is <see cref="Send"/> (unit 6.14):
/// a new test message, audited, refused in Production (D46), with an intent header.
/// </para>
/// <para>
/// <b>No provider is named here</b> (rule R4). Whether peeking is safe to repeat is read from
/// <c>ProviderCapabilities.SupportsRepeatablePeek</c> and reported in every response, so a client
/// decides whether to refresh from the answer and never from the provider's name.
/// </para>
/// <para>
/// <b>Every list is a page.</b> There is no endpoint that returns an unbounded list; the API pages, so
/// the UI pages.
/// </para>
/// </remarks>
[Route("api/v1/namespaces/{namespaceId:guid}")]
public sealed partial class MessagesController : ApiControllerBase
{
    private const int DefaultPage = 25;

    // The message request DTO allows up to 1,000; a page a person can read never needs more than 100.
    private const int MaxPage = 100;

    private const string NonRepeatableWarning =
        "Looking at messages in this cloud counts as a delivery attempt, so it is not refreshed automatically.";

    // Same shapes the message request DTO allows. Checked here so a bad name is a 400 with a sentence,
    // not a provider error.
    [GeneratedRegex(@"^[a-zA-Z0-9][\w\-\.\/]*$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityNamePattern();

    [GeneratedRegex(@"^[a-zA-Z0-9][\w\-\.]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SubscriptionNamePattern();

    private readonly INamespaceRepository _namespaces;
    private readonly ICloudProviderRouter _router;
    private readonly ILogger<MessagesController> _logger;
    private readonly IAuditTrail _audit;

    // What one test message may carry — the common case, not a bulk channel.
    private const int MaxBodyBytes = 256 * 1024;
    private const int MaxProperties = 20;

    /// <summary>Creates the controller.</summary>
    public MessagesController(INamespaceRepository namespaces, ICloudProviderRouter router, ILogger<MessagesController> logger, IAuditTrail audit)
    {
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _namespaces = namespaces ?? throw new ArgumentNullException(nameof(namespaces));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>A page of the messages waiting in a queue or subscription.</summary>
    /// <param name="namespaceId">The namespace.</param>
    /// <param name="entity">The queue, or the topic when <paramref name="subscription"/> is given.</param>
    /// <param name="subscription">The subscription, for a topic.</param>
    /// <param name="max">Page size, 1–100 (default 25).</param>
    /// <param name="from">Sequence number to start from — the previous page's <c>nextFromSequenceNumber</c>.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpGet("messages/peek")]
    [ProducesResponseType(typeof(PeekResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> PeekActive(
        Guid namespaceId, [FromQuery] string? entity, [FromQuery] string? subscription,
        [FromQuery] int? max, [FromQuery] long? from, CancellationToken cancellationToken) =>
        PeekAsync(namespaceId, entity, subscription, max, from, deadLetter: false, cancellationToken);

    /// <summary>A page of the dead-lettered messages of a queue or subscription.</summary>
    /// <inheritdoc cref="PeekActive"/>
    [HttpGet("dead-letter/peek")]
    [ProducesResponseType(typeof(PeekResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> PeekDeadLetter(
        Guid namespaceId, [FromQuery] string? entity, [FromQuery] string? subscription,
        [FromQuery] int? max, [FromQuery] long? from, CancellationToken cancellationToken) =>
        PeekAsync(namespaceId, entity, subscription, max, from, deadLetter: true, cancellationToken);

    /// <summary>
    /// One message, by sequence number. Only where peeking is repeatable: a lookup must never be the
    /// thing that changes a message's delivery state.
    /// </summary>
    /// <param name="namespaceId">The namespace.</param>
    /// <param name="sequenceNumber">The message's sequence number.</param>
    /// <param name="entity">The queue, or the topic when <paramref name="subscription"/> is given.</param>
    /// <param name="subscription">The subscription, for a topic.</param>
    /// <param name="deadLetter">True to look on the dead-letter side.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpGet("messages/{sequenceNumber:long}")]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetOne(
        Guid namespaceId, long sequenceNumber, [FromQuery] string? entity, [FromQuery] string? subscription,
        [FromQuery] bool deadLetter, CancellationToken cancellationToken)
    {
        if (sequenceNumber < 0)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'sequenceNumber' cannot be negative.");
        }

        var target = await ResolveAsync(namespaceId, entity, subscription, cancellationToken);
        if (target.Failure is not null)
        {
            return target.Failure;
        }

        var (_, provider, name, sub) = target.Value;
        if (!provider.Capabilities.SupportsRepeatablePeek)
        {
            return Problem(StatusCodes.Status409Conflict, ErrorCodes.CapabilityUnavailable,
                "This cloud can only look at a message by receiving it, which counts as a delivery attempt — so a message cannot be looked up on its own here.");
        }

        var request = new GetMessagesRequest(namespaceId, name, sub, deadLetter, MaxMessages: 1, FromSequenceNumber: sequenceNumber);
        var peeked = await PeekFromProviderAsync(provider, request, deadLetter, cancellationToken);
        if (peeked.IsFailure)
        {
            return Problem(peeked.Error);
        }

        // A peek from N returns the next message at or after N; only an exact hit is "that message".
        var found = peeked.Value.FirstOrDefault(m => m.SequenceNumber == sequenceNumber);
        return found is null
            ? Problem(StatusCodes.Status404NotFound, ErrorCodes.Message.NotFound,
                $"No message with sequence number {sequenceNumber} was found there. It may have been processed or moved.")
            : Ok(ToResponse(found));
    }

    /// <summary>
    /// What is waiting to be delivered later on one queue or subscription (unit 6.17), soonest first. Only where the cloud has
    /// scheduled messages at all; elsewhere a 409 that says so — never an empty list that reads as "none due". Read-only: there
    /// is no cancel here, because cancelling changes the cloud and has no ledger route yet.
    /// </summary>
    /// <param name="namespaceId">The namespace.</param>
    /// <param name="entity">The queue, or the topic when <paramref name="subscription"/> is given.</param>
    /// <param name="subscription">The subscription, for a topic.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpGet("messages/scheduled")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Scheduled(Guid namespaceId, [FromQuery] string? entity, [FromQuery] string? subscription, CancellationToken cancellationToken)
    {
        var target = await ResolveAsync(namespaceId, entity, subscription, cancellationToken);
        if (target.Failure is not null)
        {
            return target.Failure;
        }

        var (_, provider, name, sub) = target.Value;
        if (!provider.Capabilities.SupportsScheduledMessages)
        {
            return Problem(StatusCodes.Status409Conflict, ErrorCodes.Message.ScheduledUnsupported,
                "This cloud has no scheduled messages to list — messages here are delivered when sent (or after a short send-time delay).");
        }

        var found = await provider.GetMessageReceiver().GetScheduledMessagesAsync(namespaceId, name, sub, MaxPage, cancellationToken);
        if (found.IsFailure)
        {
            return Problem(found.Error);
        }

        const int PreviewChars = 200;
        var items = found.Value
            .OrderBy(m => m.ScheduledEnqueueTime ?? DateTimeOffset.MaxValue)
            .Select(m => new
            {
                m.MessageId, m.SequenceNumber, scheduledFor = m.ScheduledEnqueueTime, m.SizeInBytes, m.ContentType,
                bodyPreview = m.Body is null ? null : m.Body.Length <= PreviewChars ? m.Body : m.Body[..PreviewChars] + "…",
            })
            .ToList();
        return Ok(new { entity = name, subscription = sub, messages = items, capped = items.Count >= MaxPage });
    }

    /// <summary>What a send carries: one message, to one queue or topic.</summary>
    /// <param name="Entity">The queue or topic.</param>
    /// <param name="IsTopic">True when <paramref name="Entity"/> is a topic.</param>
    /// <param name="Body">The body, as text.</param>
    /// <param name="ContentType">For example <c>application/json</c>.</param>
    /// <param name="Properties">Application properties, as text.</param>
    public sealed record SendRequest(string? Entity, bool IsTopic, string? Body, string? ContentType, IReadOnlyDictionary<string, string>? Properties);

    /// <summary>
    /// Puts one new message onto a queue or topic (unit 6.14). Audited; refused in a Production namespace — 4.1.0 stays out of
    /// production (D46). The answer says what the cloud accepted, and nothing more.
    /// </summary>
    /// <param name="namespaceId">The namespace.</param>
    /// <param name="request">The message.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpPost("messages")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Send(Guid namespaceId, [FromBody] SendRequest request, CancellationToken cancellationToken)
    {
        if (!Security.IntentHeaders.Declares(Request, Security.IntentHeaders.SendMessage))
        {
            return Problem(StatusCodes.Status428PreconditionRequired, ErrorCodes.IntentRequired, Security.IntentHeaders.MissingDetail("send a message", Security.IntentHeaders.SendMessage));
        }

        if (string.IsNullOrEmpty(request?.Body))
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.Message.BodyRequired, "A message needs a body.");
        }

        if (System.Text.Encoding.UTF8.GetByteCount(request.Body) > MaxBodyBytes)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.Message.BodyTooLarge, "The body is larger than 256 KB, more than one test message needs.");
        }

        if (request.Properties is { Count: > MaxProperties } || request.Properties?.Keys.Any(string.IsNullOrWhiteSpace) == true)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, $"Up to {MaxProperties} properties, each with a name.");
        }

        var target = await ResolveAsync(namespaceId, request.Entity, null, cancellationToken);
        if (target.Failure is not null)
        {
            return target.Failure;
        }

        var (ns, provider, name, _) = target.Value;
        if (ns.Environment == Core.Enums.EnvironmentType.Prod)
        {
            return Problem(StatusCodes.Status409Conflict, ErrorCodes.CapabilityUnavailable,
                "This namespace is marked Production. ServiceHub 4.1.0 does not send messages into production — send test messages to a Dev or UAT namespace.");
        }

        if (await DeniedUnlessAsync(Core.Enums.GovernanceRole.Operator, ns.Id, null, "send a message here", cancellationToken) is { } denied) return denied;

        var sent = await provider.GetMessageSender().SendAsync(new SendMessageRequest(
            NamespaceId: ns.Id, EntityName: name, Body: request.Body,
            ContentType: string.IsNullOrWhiteSpace(request.ContentType) ? null : request.ContentType.Trim(),
            ApplicationProperties: request.Properties?.ToDictionary(p => p.Key.Trim(), p => (object)p.Value),
            IsTopic: request.IsTopic), cancellationToken);

        await _audit.RecordAsync(new AuditLog
        {
            Id = Guid.NewGuid(), Timestamp = DateTimeOffset.UtcNow, OwnerId = OwnerId, UserIdentity = Actor.Identity,
            Action = AuditActions.MessageSend, Outcome = sent.IsSuccess ? AuditActions.Success : AuditActions.Failure,
            NamespaceId = ns.Id, ResourceName = name,
            CorrelationId = HttpContext.TraceIdentifier, HttpMethod = Request.Method, HttpPath = Request.Path.Value,
        }, cancellationToken);

        if (sent.IsFailure)
        {
            return Problem(sent.Error);
        }

        _logger.LogInformation("Sent one message to {Entity} in namespace {NamespaceId}", LogRedactor.SanitiseForLog(name), ns.Id);
        return Ok(new { accepted = true, entity = name, isTopic = request.IsTopic, detail = $"The cloud accepted one message onto {name}." });
    }

    private async Task<IActionResult> PeekAsync(
        Guid namespaceId, string? entity, string? subscription, int? max, long? from, bool deadLetter,
        CancellationToken cancellationToken)
    {
        if (max is < 1 or > MaxPage)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed,
                $"'max' must be between 1 and {MaxPage}.");
        }

        if (from is < 0)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'from' cannot be negative.");
        }

        var target = await ResolveAsync(namespaceId, entity, subscription, cancellationToken);
        if (target.Failure is not null)
        {
            return target.Failure;
        }

        var (_, provider, name, sub) = target.Value;
        var repeatable = provider.Capabilities.SupportsRepeatablePeek;

        // Without a repeatable peek there is no cursor: every call is a fresh receive. Saying so beats
        // silently returning the same page for every "next".
        if (from is not null && !repeatable)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed,
                "This cloud cannot page a peek — every look is a fresh receive — so 'from' is not supported.");
        }

        var size = Math.Clamp(max ?? DefaultPage, 1, MaxPage);
        var request = new GetMessagesRequest(namespaceId, name, sub, deadLetter, size, from);

        _logger.LogInformation(
            "Peeking {Max} {Side} messages of {Entity} in namespace {NamespaceId}",
            size, deadLetter ? "dead-letter" : "active", LogRedactor.SanitiseForLog(name), namespaceId);

        var peeked = await PeekFromProviderAsync(provider, request, deadLetter, cancellationToken);
        if (peeked.IsFailure)
        {
            return Problem(peeked.Error);
        }

        var messages = peeked.Value.Take(size).Select(ToResponse).ToList();
        var next = repeatable && messages.Count == size ? messages[^1].SequenceNumber + 1 : (long?)null;

        return Ok(new PeekResponse(
            namespaceId, name, sub, deadLetter, messages,
            new PeekPaging(size, messages.Count, next),
            new PeekSafety(repeatable, repeatable ? null : NonRepeatableWarning)));
    }

    private static async Task<Core.Results.Result<IReadOnlyList<Message>>> PeekFromProviderAsync(
        ICloudMessagingProvider provider, GetMessagesRequest request, bool deadLetter, CancellationToken cancellationToken)
    {
        var receiver = provider.GetMessageReceiver();
        return deadLetter
            ? await receiver.PeekDeadLetterMessagesAsync(request, cancellationToken)
            : await receiver.PeekMessagesAsync(request, cancellationToken);
    }

    private sealed record Target(Namespace Namespace, ICloudMessagingProvider Provider, string Entity, string? Subscription);

    private readonly record struct Resolved(Target? Success, ObjectResult? Failure)
    {
        public Target Value => Success!;

        public void Deconstruct(out Namespace ns, out ICloudMessagingProvider provider, out string entity, out string? subscription) =>
            (ns, provider, entity, subscription) = (Success!.Namespace, Success.Provider, Success.Entity, Success.Subscription);
    }

    private async Task<Resolved> ResolveAsync(Guid namespaceId, string? entity, string? subscription, CancellationToken cancellationToken)
    {
        var name = entity?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 256 || !EntityNamePattern().IsMatch(name))
        {
            return new Resolved(null, Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed,
                "'entity' is required: the queue, or the topic when you also give a 'subscription'."));
        }

        var sub = string.IsNullOrWhiteSpace(subscription) ? null : subscription.Trim();
        if (sub is not null && (sub.Length > 256 || !SubscriptionNamePattern().IsMatch(sub)))
        {
            return new Resolved(null, Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed,
                "'subscription' contains characters a subscription name cannot have."));
        }

        var found = await GetVisibleNamespaceAsync(_namespaces, namespaceId, cancellationToken);
        if (found.IsFailure)
        {
            return new Resolved(null, Problem(found.Error));
        }

        var ns = found.Value;
        if (!_router.IsRegistered(ns.Provider))
        {
            return new Resolved(null, Problem(StatusCodes.Status503ServiceUnavailable, ErrorCodes.CapabilityUnavailable,
                $"This build of ServiceHub has no adapter for '{ns.Provider}', so '{ns.Name}' cannot be reached."));
        }

        return new Resolved(new Target(ns, _router.Resolve(ns.Provider), name, sub), null);
    }

    private static MessageResponse ToResponse(Message m) => new(
        m.MessageId, m.SequenceNumber, m.Body, m.ContentType, m.CorrelationId, m.SessionId, m.Subject,
        m.EnqueuedTime, m.DeliveryCount, m.DeadLetterReason, m.DeadLetterErrorDescription,
        m.ApplicationProperties, m.SizeInBytes, m.IsFromDeadLetter);
}
