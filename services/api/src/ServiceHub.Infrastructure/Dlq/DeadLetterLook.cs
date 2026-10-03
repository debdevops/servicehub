using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Events;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.Dlq;

/// <summary>
/// One look at one namespace's dead letters, asked for by a person (<see cref="IDeadLetterLook"/>). It runs the
/// same <see cref="DlqScanner"/> the DLQ monitor runs — same recording, same "absence only from a scan that
/// could see" rule, same recurrence detection — with the person's request as the consent to look.
/// </summary>
public sealed class DeadLetterLook : IDeadLetterLook
{
    // One look per namespace at a time: two overlapping receives on SQS would each hide what the other is holding.
    private static readonly ConcurrentDictionary<Guid, byte> Running = new();

    private readonly IServiceScopeFactory _scopes;
    private readonly ICloudProviderRouter _router;
    private readonly TimeProvider _time;
    private readonly ILogger<DeadLetterLook> _logger;

    /// <summary>Creates it.</summary>
    public DeadLetterLook(IServiceScopeFactory scopes, ICloudProviderRouter router, ILogger<DeadLetterLook> logger, TimeProvider? time = null)
    {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<DeadLetterLookResult> LookNowAsync(Namespace ns, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ns);
        var now = _time.GetUtcNow();
        var destructive = _router.IsRegistered(ns.Provider) && !_router.Resolve(ns.Provider).Capabilities.SupportsRepeatablePeek;

        if (!Running.TryAdd(ns.Id, 0))
        {
            return new DeadLetterLookResult("busy", 0, 0, 0, 0, destructive, "ServiceHub is already looking at this namespace.", now);
        }

        try
        {
            NamespaceScanResult result;
            using (var scope = _scopes.CreateScope())
            {
                // Its own scope, like the monitor's: a scan's context must never be shared with a request's.
                var scanner = ActivatorUtilities.CreateInstance<DlqScanner>(scope.ServiceProvider);
                result = await scanner.ScanAsync(ns, cancellationToken, askedByPerson: true).ConfigureAwait(false);

                if (result.Outcome == ScanOutcome.Scanned && (result.NewMessages > 0 || result.Resolved > 0)
                    && scope.ServiceProvider.GetService<IPlatformEventBus>() is { } bus)
                {
                    await bus.PublishAsync(new PlatformEvent
                    {
                        Source = "dead-letter-look", Category = EventCategories.Dlq, EventType = EventTypes.DlqMessageDetected,
                        CloudProvider = ns.Provider.ToString().ToLowerInvariant(), NamespaceId = ns.Id, NamespaceName = ns.Name, Actor = ns.OwnerId,
                    }, cancellationToken).ConfigureAwait(false);
                }
            }

            return result.Outcome == ScanOutcome.Scanned
                ? new DeadLetterLookResult("looked", result.EntitiesExamined, result.NewMessages, result.Resolved, result.Unconfirmed, destructive, null, now)
                : new DeadLetterLookResult("failed", 0, 0, 0, 0, destructive, result.Reason ?? "The cloud could not be read.", now);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // A failed look is an answer, not a 500: nothing was changed.
        catch (Exception ex)
        {
            _logger.LogError(ex, "Looking at the dead letters of namespace {NamespaceId} failed", ns.Id);
            return new DeadLetterLookResult("failed", 0, 0, 0, 0, destructive, "The cloud could not be read. Nothing was changed.", now);
        }
#pragma warning restore CA1031
        finally
        {
            Running.TryRemove(ns.Id, out _);
        }
    }
}
