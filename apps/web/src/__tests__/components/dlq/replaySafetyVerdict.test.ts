import { describe, it, expect } from 'vitest';
import {
  computeReplaySafetyVerdict,
  summarizeReplayHistory,
  computeReplayRecurrence,
  computeReplayRecommendation,
  CONSECUTIVE_FAILURE_THRESHOLD,
} from '@/components/dlq/replaySafetyVerdict';
import type { ProviderCapabilities } from '@servicehub/ui-shared/lib/api/cloudBridge';
import type { BulkOperationJob } from '@servicehub/ui-shared/lib/api/bulkOperations';

const REPEATABLE_PEEK: ProviderCapabilities = {
  supportsMessageCounts: true,
  supportsManualDeadLetter: true,
  supportsPurge: false,
  supportsScheduledMessages: true,
  supportsRepeatablePeek: true,
  notes: '',
};

const DESTRUCTIVE_PEEK: ProviderCapabilities = {
  ...REPEATABLE_PEEK,
  supportsRepeatablePeek: false,
};

function makeJob(status: BulkOperationJob['status'], overrides: Partial<BulkOperationJob> = {}): BulkOperationJob {
  return {
    id: 'job-1',
    operationType: 'Replay',
    status,
    namespaceId: 'ns-1',
    namespaceDisplayName: 'ns',
    entityNameFilter: null,
    statusFilter: null,
    categoryFilter: null,
    from: null,
    to: null,
    totalMatched: 1,
    processedCount: 1,
    successCount: status === 'Completed' ? 1 : 0,
    failureCount: status === 'Completed' ? 0 : 1,
    skippedCount: 0,
    failureSample: null,
    errorSummary: null,
    createdAt: new Date().toISOString(),
    startedAt: new Date().toISOString(),
    completedAt: new Date().toISOString(),
    isCancellable: false,
    ...overrides,
  };
}

describe('computeReplaySafetyVerdict', () => {
  it('is "safe" when capabilities are unknown and there is no job history', () => {
    expect(computeReplaySafetyVerdict(undefined, undefined)).toBe('safe');
  });

  it('is "safe" on a repeatable-peek provider with no prior jobs', () => {
    expect(computeReplaySafetyVerdict(REPEATABLE_PEEK, undefined)).toBe('safe');
  });

  it('is "safe" on a repeatable-peek provider whose last job completed', () => {
    expect(computeReplaySafetyVerdict(REPEATABLE_PEEK, makeJob('Completed'))).toBe('safe');
  });

  it('is "review" on a destructive-peek provider even with a successful last job', () => {
    expect(computeReplaySafetyVerdict(DESTRUCTIVE_PEEK, makeJob('Completed'))).toBe('review');
  });

  it('is "review" when the most recent job failed, even on a repeatable-peek provider', () => {
    expect(computeReplaySafetyVerdict(REPEATABLE_PEEK, makeJob('Failed'))).toBe('review');
  });

  it('is "review" when the most recent job completed with errors', () => {
    expect(computeReplaySafetyVerdict(REPEATABLE_PEEK, makeJob('CompletedWithErrors'))).toBe('review');
  });

  it('is "safe" when the most recent job was merely cancelled (not a failure signal)', () => {
    expect(computeReplaySafetyVerdict(REPEATABLE_PEEK, makeJob('Cancelled'))).toBe('safe');
  });
});

describe('summarizeReplayHistory', () => {
  it('counts zero of everything for an empty history', () => {
    const summary = summarizeReplayHistory([], 0);
    expect(summary).toEqual({
      successCount: 0,
      failureCount: 0,
      cancelledCount: 0,
      skippedMessageCount: 0,
      lastReplayStatus: null,
      lastReplayAt: null,
      consecutiveFailureCount: 0,
      isTruncated: false,
    });
  });

  it('counts successful, failed, and cancelled jobs by status', () => {
    const items = [
      makeJob('Completed'),
      makeJob('Failed'),
      makeJob('CompletedWithErrors'),
      makeJob('Cancelled'),
    ];
    const summary = summarizeReplayHistory(items, 4);
    expect(summary.successCount).toBe(1);
    expect(summary.failureCount).toBe(2);
    expect(summary.cancelledCount).toBe(1);
  });

  it('sums skippedCount across every job in the fetched history', () => {
    const items = [makeJob('Completed', { skippedCount: 2 }), makeJob('Completed', { skippedCount: 3 })];
    expect(summarizeReplayHistory(items, 2).skippedMessageCount).toBe(5);
  });

  it('takes the last replay status/timestamp from the most recent (first) item', () => {
    const items = [makeJob('Failed', { createdAt: '2026-08-01T00:00:00Z' }), makeJob('Completed', { createdAt: '2026-07-01T00:00:00Z' })];
    const summary = summarizeReplayHistory(items, 2);
    expect(summary.lastReplayStatus).toBe('Failed');
    expect(summary.lastReplayAt).toBe('2026-08-01T00:00:00Z');
  });

  it('counts an unbroken run of unsuccessful jobs from the most recent job backward', () => {
    const items = [makeJob('Failed'), makeJob('CompletedWithErrors'), makeJob('Failed'), makeJob('Completed'), makeJob('Failed')];
    expect(summarizeReplayHistory(items, 5).consecutiveFailureCount).toBe(3);
  });

  it('streak is zero when the most recent job was not unsuccessful', () => {
    const items = [makeJob('Completed'), makeJob('Failed'), makeJob('Failed')];
    expect(summarizeReplayHistory(items, 3).consecutiveFailureCount).toBe(0);
  });

  it('flags truncation when totalCount exceeds the fetched item count', () => {
    expect(summarizeReplayHistory([makeJob('Completed')], 5).isTruncated).toBe(true);
    expect(summarizeReplayHistory([makeJob('Completed')], 1).isTruncated).toBe(false);
  });
});

describe('computeReplayRecurrence', () => {
  it('returns null when there is no last-seen timestamp to compare against', () => {
    expect(computeReplayRecurrence([makeJob('Completed')], undefined)).toBeNull();
  });

  it('returns null when there is no terminal, non-cancelled job', () => {
    expect(computeReplayRecurrence([], '2026-08-01T00:00:00Z')).toBeNull();
    const cancelledOnly = [makeJob('Cancelled', { completedAt: '2026-07-01T00:00:00Z' })];
    expect(computeReplayRecurrence(cancelledOnly, '2026-08-01T00:00:00Z')).toBeNull();
  });

  it('reports recurrence when the signature was last seen after the replay job completed', () => {
    const items = [makeJob('Completed', { completedAt: '2026-07-01T00:00:00Z' })];
    const result = computeReplayRecurrence(items, '2026-08-01T00:00:00Z');
    expect(result).toEqual({ recurred: true, replayCompletedAt: '2026-07-01T00:00:00Z' });
  });

  it('reports no recurrence when the signature was last seen before the replay job completed', () => {
    const items = [makeJob('Completed', { completedAt: '2026-08-01T00:00:00Z' })];
    const result = computeReplayRecurrence(items, '2026-07-01T00:00:00Z');
    expect(result).toEqual({ recurred: false, replayCompletedAt: '2026-08-01T00:00:00Z' });
  });

  it('skips a cancelled job to find the most recent terminal job underneath it', () => {
    const items = [
      makeJob('Cancelled', { completedAt: '2026-08-05T00:00:00Z' }),
      makeJob('Completed', { completedAt: '2026-07-01T00:00:00Z' }),
    ];
    const result = computeReplayRecurrence(items, '2026-08-01T00:00:00Z');
    expect(result).toEqual({ recurred: true, replayCompletedAt: '2026-07-01T00:00:00Z' });
  });
});

describe('computeReplayRecommendation', () => {
  it('flags investigation when the consecutive-failure streak meets the threshold', () => {
    const summary = summarizeReplayHistory(
      Array.from({ length: CONSECUTIVE_FAILURE_THRESHOLD }, () => makeJob('Failed')),
      CONSECUTIVE_FAILURE_THRESHOLD,
    );
    expect(computeReplayRecommendation('safe', summary)).toBe('Investigate the underlying failure before replaying.');
  });

  it('returns null for a capability-driven "review" verdict with no streak (avoids restating the banner)', () => {
    const summary = summarizeReplayHistory([makeJob('Completed')], 1);
    expect(computeReplayRecommendation('review', summary)).toBeNull();
  });

  it('recommends replay after verification when the verdict is safe and there is no streak', () => {
    const summary = summarizeReplayHistory([makeJob('Completed')], 1);
    expect(computeReplayRecommendation('safe', summary)).toBe('Replay can be attempted after verifying current namespace state.');
  });

  it('the streak takes priority over a "review" verdict', () => {
    const summary = summarizeReplayHistory(
      Array.from({ length: CONSECUTIVE_FAILURE_THRESHOLD }, () => makeJob('Failed')),
      CONSECUTIVE_FAILURE_THRESHOLD,
    );
    expect(computeReplayRecommendation('review', summary)).toBe('Investigate the underlying failure before replaying.');
  });
});
