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
/// <b>Read-only.</b> Nothing here moves, replays or deletes a message: anything that changes a cloud goes
/// through the ledger and the eligibility gate (units 2.5–2.7), and those do not exist yet — so neither
/// does a delete endpoint.
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

    /// <summary>Creates the controller.</summary>
    public MessagesController(INamespaceRepository namespaces, ICloudProviderRouter router, ILogger<MessagesController> logger)
    {
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
