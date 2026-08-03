import { describe, it, expect } from 'vitest';
import { computeReplaySafetyVerdict } from '@/components/dlq/replaySafetyVerdict';
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

function makeJob(status: BulkOperationJob['status']): BulkOperationJob {
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
