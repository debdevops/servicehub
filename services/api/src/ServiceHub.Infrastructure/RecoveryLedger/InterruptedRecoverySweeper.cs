using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.RecoveryLedger;

/// <summary>When this ServiceHub process began. Anything the ledger holds as still <c>Executing</c> from before it is stale.</summary>
/// <param name="At">The moment the host's services were registered, which is before any request or agent can run.</param>
public sealed record RecoverySweepCutoff(DateTimeOffset At);

/// <summary>
/// At startup, settles recovery attempts a previous run left half-written. A replay opens its ledger entry as <c>Executing</c>,
/// calls the cloud, then records the answer; if ServiceHub stops in between, the entry stays <c>Executing</c> for ever and the
/// cloud may or may not have sent the message. One instance runs at a time (the instance lock), so an <c>Executing</c> entry that
/// began before this process did cannot belong to a live call. It becomes <c>ExecutionUnknown</c> — never success, never failure
/// — and blocks another attempt on that message until a person resolves it.
/// </summary>
/// <remarks>
/// A failure here must not stop ServiceHub starting, and it does not weaken the safety: an unswept <c>Executing</c> entry
/// blocks a second attempt just as an unknown one does.
/// </remarks>
public sealed class InterruptedRecoverySweeper : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly RecoverySweepCutoff _cutoff;
    private readonly ILogger<InterruptedRecoverySweeper> _logger;

    /// <summary>Creates it.</summary>
    public InterruptedRecoverySweeper(IServiceScopeFactory scopes, RecoverySweepCutoff cutoff, ILogger<InterruptedRecoverySweeper> logger)
    {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _cutoff = cutoff ?? throw new ArgumentNullException(nameof(cutoff));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var settled = await scope.ServiceProvider.GetRequiredService<IRecoveryLedger>()
                .ReconcileInterruptedAsync(_cutoff.At, cancellationToken).ConfigureAwait(false);
            if (settled > 0)
            {
                _logger.LogWarning(
                    "{Count} recovery attempt(s) were interrupted by a restart; each is now 'outcome unknown' and waits for a person",
                    settled);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not settle interrupted recovery attempts at startup; they stay open and still block a repeat");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
