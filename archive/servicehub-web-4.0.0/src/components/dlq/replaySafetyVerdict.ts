import type { ProviderCapabilities } from '@servicehub/ui-shared/lib/api/cloudBridge';
import type { BulkOperationJob } from '@servicehub/ui-shared/lib/api/bulkOperations';

export type ReplaySafetyVerdict = 'safe' | 'review';

/** A job outcome that did not fully succeed — the same definition the safety verdict already used. */
const UNSUCCESSFUL_STATUSES: ReadonlySet<BulkOperationJob['status']> = new Set([
  'Failed',
  'CompletedWithErrors',
]);

/**
 * §6.4's replay-safety verdict — a fixed lookup over two already-existing facts (provider
 * capability, most recent job outcome), never a computed/weighted score.
 */
export function computeReplaySafetyVerdict(
  capabilities: ProviderCapabilities | undefined,
  mostRecentJob: BulkOperationJob | undefined,
): ReplaySafetyVerdict {
  const destructivePeek = capabilities !== undefined && !capabilities.supportsRepeatablePeek;
  const lastJobUnsuccessful = mostRecentJob !== undefined && UNSUCCESSFUL_STATUSES.has(mostRecentJob.status);
  return destructivePeek || lastJobUnsuccessful ? 'review' : 'safe';
}

/** Number of consecutive unsuccessful jobs (most recent first) needed before flagging a streak. */
export const CONSECUTIVE_FAILURE_THRESHOLD = 3;

/** Deterministic replay-outcome counts over a signature's fetched job history — no inference. */
export interface ReplayHistorySummary {
  successCount: number;
  failureCount: number;
  cancelledCount: number;
  skippedMessageCount: number;
  lastReplayStatus: BulkOperationJob['status'] | null;
  lastReplayAt: string | null;
  /** Unbroken run of unsuccessful jobs, most recent first (0 if the most recent job wasn't unsuccessful). */
  consecutiveFailureCount: number;
  /** True when `items` doesn't cover the full history (more jobs exist than were fetched). */
  isTruncated: boolean;
}

/**
 * Folds a signature's replay job history (already fetched, most-recent-first, per
 * `ISignatureReplayService.ListJobsAsync`'s ordering) into literal counts and a consecutive-
 * failure streak — every field here is a direct count or the first item in an already-sorted
 * list, never a score.
 */
export function summarizeReplayHistory(items: BulkOperationJob[], totalCount: number): ReplayHistorySummary {
  let successCount = 0;
  let failureCount = 0;
  let cancelledCount = 0;
  let skippedMessageCount = 0;

  for (const job of items) {
    if (job.status === 'Completed') successCount++;
    else if (UNSUCCESSFUL_STATUSES.has(job.status)) failureCount++;
    else if (job.status === 'Cancelled') cancelledCount++;
    skippedMessageCount += job.skippedCount;
  }

  let consecutiveFailureCount = 0;
  for (const job of items) {
    if (!UNSUCCESSFUL_STATUSES.has(job.status)) break;
    consecutiveFailureCount++;
  }

  const mostRecent = items[0] as BulkOperationJob | undefined;

  return {
    successCount,
    failureCount,
    cancelledCount,
    skippedMessageCount,
    lastReplayStatus: mostRecent?.status ?? null,
    lastReplayAt: mostRecent?.createdAt ?? null,
    consecutiveFailureCount,
    isTruncated: totalCount > items.length,
  };
}

/** Whether this signature recurred after its most recent terminal (non-cancelled) replay job, and when that job finished. */
export interface ReplayRecurrenceFact {
  recurred: boolean;
  replayCompletedAt: string;
}

/**
 * Compares the signature's own last-observed-occurrence timestamp against its most recent
 * terminal, non-cancelled replay job's completion time — a single timestamp comparison over two
 * already-durable facts, never an inference about causation. Returns `null` when there's no
 * terminal job to compare against (nothing replayed yet, or every job is still in flight).
 */
export function computeReplayRecurrence(
  items: BulkOperationJob[],
  lastSeenAt: string | undefined,
): ReplayRecurrenceFact | null {
  if (!lastSeenAt) return null;

  const lastTerminalJob = items.find((job) => job.completedAt !== null && job.status !== 'Cancelled');
  if (!lastTerminalJob || lastTerminalJob.completedAt === null) return null;

  return {
    recurred: new Date(lastSeenAt).getTime() > new Date(lastTerminalJob.completedAt).getTime(),
    replayCompletedAt: lastTerminalJob.completedAt,
  };
}

/**
 * The evidence-based recommendation line for the Replay Safety & History panel — a fixed lookup
 * over the consecutive-failure streak this module already computed. Returns `null` when the
 * verdict banner above already says everything this evidence would add (a capability-driven
 * "review" verdict with no streak) — the panel must not restate the same fact twice.
 */
export function computeReplayRecommendation(verdict: ReplaySafetyVerdict, summary: ReplayHistorySummary): string | null {
  if (summary.consecutiveFailureCount >= CONSECUTIVE_FAILURE_THRESHOLD) {
    return 'Investigate the underlying failure before replaying.';
  }
  if (verdict === 'review') {
    return null;
  }
  return 'Replay can be attempted after verifying current namespace state.';
}
