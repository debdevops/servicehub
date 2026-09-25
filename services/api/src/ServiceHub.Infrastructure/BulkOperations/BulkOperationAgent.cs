using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.BulkOperations;

/// <summary>
/// Runs started bulk replays (unit 3.2) — the first agent that changes something, and it does so only for a job a person
/// started from a stored preview.
/// </summary>
/// <remarks>
/// <b>Authority: ActsWithApproval.</b> Every message goes through <see cref="IDlqReplayService.ReplayAsync"/>, so each is
/// judged by the eligibility gate again at the moment of sending, and each writes its own ledger entry. The agent never
/// calls a cloud itself. It sends at the previewed pace, honours a cancel before the next message, and stops itself after
/// N messages in a row are not accepted. A crash mid-send leaves an item <see cref="BulkItemState.Sending"/>; it becomes
/// <see cref="BulkItemState.Unknown"/> — never "failed", because a duplicate is worse than a pause.
/// </remarks>
public sealed class BulkOperationAgent : IAgent
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BulkOperationAgent> _logger;

    /// <summary>Creates the agent.</summary>
    public BulkOperationAgent(IServiceScopeFactory scopes, ILogger<BulkOperationAgent> logger)
    {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Descriptor = new AgentDescriptor(
            Id: "bulk-replay",
            Name: "Bulk Replay",
            Purpose: "Sends back the dead letters a person chose and previewed, one at a time, each checked and recorded on its own.",
            Kind: AgentKind.Act,
            Authority: AgentAuthority.ActsWithApproval,
            Cadence: TimeSpan.FromSeconds(2),
            Notes: "Never starts on its own: a run begins only from a preview a person started.");
    }

    /// <inheritdoc />
    public AgentDescriptor Descriptor { get; }

    /// <inheritdoc />
    public async Task<AgentCycleResult> ExecuteCycleAsync(CancellationToken ct)
    {
        List<Guid> running;
        using (var scope = _scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
            running = await db.BulkOperationJobs.AsNoTracking().Where(j => j.Status == BulkOperationStatus.Running)
                .OrderBy(j => j.StartedAt).Select(j => j.Id).ToListAsync(ct).ConfigureAwait(false);
        }

        if (running.Count == 0)
        {
            return AgentCycleResult.Idle("no bulk replay is running");
        }

        var attempted = 0;
        foreach (var id in running)
        {
            attempted += await RunAsync(id, ct).ConfigureAwait(false);
        }

        return new AgentCycleResult(running.Count, attempted, $"worked on {running.Count} bulk replay(s), {attempted} message(s) attempted");
    }

    private async Task<int> RunAsync(Guid jobId, CancellationToken ct)
    {
        var attempted = 0;
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
        var time = scope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;

        var job = await db.BulkOperationJobs.Include(j => j.Items).FirstOrDefaultAsync(j => j.Id == jobId, ct).ConfigureAwait(false);
        if (job is null || job.Status != BulkOperationStatus.Running)
        {
            return 0;
        }

        // A message left in flight by a crash: whether it was sent is not known.
        foreach (var stuck in job.Items.Where(i => i.State == BulkItemState.Sending))
        {
            stuck.State = BulkItemState.Unknown;
            stuck.ReasonCode = "INTERRUPTED";
        }

        var actor = new RecoveryActor(job.ActorIdentity, job.ActorKind);
        var delay = TimeSpan.FromSeconds(1 / job.PerSecond);
        var inARow = 0;

        var queue = job.Items.Where(i => i.State == BulkItemState.Queued).OrderBy(i => i.Position).ToList();
        if (job.SampleOnly)
        {
            queue = queue.Take(1).ToList();
        }

        foreach (var item in queue)
        {
            // Cancel is a request in the database, honoured before the next message — never mid-message.
            await db.Entry(job).ReloadAsync(ct).ConfigureAwait(false);
            if (job.CancelRequested || ct.IsCancellationRequested)
            {
                break;
            }

            item.State = BulkItemState.Sending;
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

            attempted++;

            // Each replay gets its OWN scope, so its own database context. The replay service may hit a concurrency
            // conflict with the DLQ monitor (which marks the message resolved the moment it leaves the queue) and, rightly,
            // carry on — but the conflicted row would stay tracked, and sharing this job's context would make the very
            // next save of the job's progress throw and kill the run. Job bookkeeping and cloud work never share state.
            using var itemScope = _scopes.CreateScope();
            var namespaces = itemScope.ServiceProvider.GetRequiredService<INamespaceRepository>();
            var replay = itemScope.ServiceProvider.GetRequiredService<IDlqReplayService>();
            var ns = (await namespaces.GetByIdAsync(item.NamespaceId, CancellationToken.None).ConfigureAwait(false)) is { IsSuccess: true } found ? found.Value : null;
            if (ns is null)
            {
                item.State = BulkItemState.Failed;
                item.ReasonCode = "NOT_FOUND";
            }
            else
            {
                try
                {
                    var outcome = await replay.ReplayAsync(item.DlqMessageId, ns, actor, "bulk-replay", job.Id.ToString(), CancellationToken.None).ConfigureAwait(false);
                    if (outcome.IsFailure)
                    {
                        // The gate refused at the moment of sending, or the cloud rejected before acting.
                        item.State = BulkItemState.Failed;
                        item.ReasonCode = outcome.Error.Code;
                    }
                    else
                    {
                        item.RecoveryEntryId = outcome.Value.EntryId;
                        item.ReasonCode = outcome.Value.ErrorCode;
                        item.State = outcome.Value.Result switch
                        {
                            "accepted" => BulkItemState.Sent,
                            "rejected" => BulkItemState.Failed,
                            _ => BulkItemState.Unknown,
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Bulk replay {JobId}: dead letter {Id} threw; recording it as unknown", job.Id, item.DlqMessageId);
                    item.State = BulkItemState.Unknown;
                    item.ReasonCode = "INTERRUPTED";
                }
            }

            inARow = item.State == BulkItemState.Sent ? 0 : inARow + 1;
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

            if (inARow >= job.StopAfterConsecutiveFailures)
            {
                job.Status = BulkOperationStatus.Stopped;
                job.EndedReason = $"Stopped itself: {inARow} messages in a row were not accepted. Nothing further was sent.";
                break;
            }

            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        var now = time.GetUtcNow();
        if (job.Status == BulkOperationStatus.Running)
        {
            await db.Entry(job).ReloadAsync(CancellationToken.None).ConfigureAwait(false);
            if (job.CancelRequested)
            {
                job.Status = BulkOperationStatus.Cancelled;
                job.EndedReason = "Stopped by a person. Messages not yet sent were left exactly as they were.";
            }
            else if (job.SampleOnly && !ct.IsCancellationRequested)
            {
                job.Status = BulkOperationStatus.Completed;
                job.EndedReason = "A sample of 1 was sent. Look at the result, then preview the rest.";
            }
            else if (!ct.IsCancellationRequested && job.Items.All(i => i.State != BulkItemState.Queued && i.State != BulkItemState.Sending))
            {
                job.Status = BulkOperationStatus.Completed;
            }
        }

        if (job.Status != BulkOperationStatus.Running)
        {
            foreach (var left in job.Items.Where(i => i.State is BulkItemState.Queued or BulkItemState.Sending))
            {
                left.State = BulkItemState.Skipped;
            }

            job.EndedAt = now;
        }

        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        return attempted;
    }
}
