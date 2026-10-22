using ServiceHub.Core.Results;

namespace ServiceHub.Core.Components;

/// <summary>One bounded read: what it returned, or that it failed (and why, when the failure was not just the time budget running out).</summary>
public sealed record SourceRead<T>(T Value, bool Failed, Error? Error);

/// <summary>
/// The shape every source's multi-place read shares (extracted when the second source needed it, PLAN-PLUG-AND-PLAY-COMPONENTS §3.2):
/// a few reads at once, one shared time budget, and a failed read that leaves the page incomplete instead of losing the others.
/// </summary>
public static class SourceReads
{
    /// <summary>How many reads run at once, so a busy account is not hit with every call together.</summary>
    public const int MaxParallel = 4;

    /// <summary>
    /// Runs <paramref name="read"/> for each input, at most <see cref="MaxParallel"/> at a time. A read that throws, or is cut off by the
    /// budget, comes back <see cref="SourceRead{T}.Failed"/> with <paramref name="empty"/> as its value. Only the caller's own cancellation
    /// is rethrown, once every read has stopped.
    /// </summary>
    /// <param name="inputs">What to read.</param>
    /// <param name="read">One read. Receives the budget's token.</param>
    /// <param name="toError">Turns a cloud failure into the error to show.</param>
    /// <param name="empty">The value of a failed read.</param>
    /// <param name="budget">A token that is cancelled when the listing's time is up.</param>
    /// <param name="caller">The caller's own token, used only to tell their cancellation from the budget's.</param>
    public static async Task<IReadOnlyList<SourceRead<T>>> RunAsync<TIn, T>(
        IEnumerable<TIn> inputs, Func<TIn, CancellationToken, Task<T>> read, Func<Exception, Error> toError, T empty, CancellationToken budget, CancellationToken caller)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(toError);

        using var slots = new SemaphoreSlim(MaxParallel);
        var results = await Task.WhenAll(inputs.Select(input => OneAsync(input, read, toError, empty, slots, budget))).ConfigureAwait(false);
        caller.ThrowIfCancellationRequested();
        return results;
    }

    private static async Task<SourceRead<T>> OneAsync<TIn, T>(
        TIn input, Func<TIn, CancellationToken, Task<T>> read, Func<Exception, Error> toError, T empty, SemaphoreSlim slots, CancellationToken budget)
    {
        try
        {
            await slots.WaitAsync(budget).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new SourceRead<T>(empty, true, null);
        }

        try
        {
            return new SourceRead<T>(await read(input, budget).ConfigureAwait(false), false, null);
        }
#pragma warning disable CA1031 // One throttled or denied read leaves the page incomplete; it never loses the others.
        catch (Exception ex)
        {
            return new SourceRead<T>(empty, true, ex is OperationCanceledException ? null : toError(ex));
        }
#pragma warning restore CA1031
        finally
        {
            slots.Release();
        }
    }
}
