import { describe, it, expect } from 'vitest';
import {
  bucketEntryState, buildRecoveryLifecycle, computeLedgerStats, summarizeOperation,
} from '@/components/autonomy/recoveryOutcome';
import type { RecoveryEvent, RecoveryLedgerEntry, RecoveryOperation } from '@servicehub/ui-shared/lib/api/recovery';

function op(overrides: Partial<RecoveryOperation> = {}): RecoveryOperation {
  return {
    id: 'op-1',
    kind: 'Replay',
    trigger: 'Manual',
    actorIdentity: 'alex@contoso.com',
    actorKind: 'User',
    reason: 'INC-1',
    namespaceId: 'ns-1',
    namespaceNameSnapshot: 'contoso-dev',
    providerSnapshot: 'azure',
    environmentSnapshot: 'dev',
    scopeDescription: 'entity=orders',
    sourceRuleId: null,
    sourceJobId: null,
    serviceVersion: '4.0.0',
    openedAt: '2026-09-10T10:00:00Z',
    targetCount: 2,
    entryCount: 2,
    ...overrides,
  };
}

function entry(state: RecoveryLedgerEntry['state'], overrides: Partial<RecoveryLedgerEntry> = {}): RecoveryLedgerEntry {
  return {
    id: `e-${Math.random()}`,
    operationId: 'op-1',
    dlqMessageId: 1,
    namespaceId: 'ns-1',
    namespaceNameSnapshot: 'contoso-dev',
    providerSnapshot: 'azure',
    environmentSnapshot: 'dev',
    entityNameSnapshot: 'orders',
    entityTypeSnapshot: 'Queue',
    topicNameSnapshot: null,
    bodyHash: 'sha256-x',
    failureCategorySnapshot: 'TransientDependency',
    deadLetterReasonSnapshot: 'MaxDeliveryCountExceeded',
    signatureHashSnapshot: 'abcdef1234567890',
    targetEntity: 'orders',
    begunAt: '2026-09-10T10:00:01Z',
    markerApplied: true,
    state,
    disposition: null,
    verificationResult: null,
    verificationConfidence: null,
    observationWindowEndsAt: null,
    closedAt: null,
    ...overrides,
  };
}

function event(eventType: string, overrides: Partial<RecoveryEvent> = {}): RecoveryEvent {
  return {
    id: `ev-${Math.random()}`,
    ownerId: 'owner',
    seq: 1,
    entryId: null,
    operationId: 'op-1',
    eventType,
    occurredAt: '2026-09-10T10:00:02Z',
    actorIdentity: 'alex@contoso.com',
    actorKind: 'User',
    detailJson: null,
    prevHash: '',
    entryHash: '',
    schemaVersion: 1,
    ...overrides,
  };
}

describe('bucketEntryState', () => {
  it('never counts an unverified or unknown outcome as a success', () => {
    expect(bucketEntryState('Unverified')).toBe('unresolved');
    expect(bucketEntryState('ExecutionUnknown')).toBe('unresolved');
    expect(bucketEntryState('Recovered')).toBe('recovered');
    expect(bucketEntryState('Returned')).toBe('failed');
    expect(bucketEntryState('Declined')).toBe('blocked');
  });
});

describe('summarizeOperation', () => {
  it('is Recovered only when every entry recovered', () => {
    expect(summarizeOperation(op(), [entry('Recovered'), entry('Recovered')]).outcome).toBe('Recovered');
  });

  it('is Partial on a mix of recovered and returned', () => {
    expect(summarizeOperation(op(), [entry('Recovered'), entry('Returned')]).outcome).toBe('Partial');
  });

  it('is Failed when nothing recovered and something failed', () => {
    expect(summarizeOperation(op(), [entry('ExecutionFailed'), entry('Unverified')]).outcome).toBe('Failed');
  });

  it('reports InProgress while any entry is still being watched, even if only partly loaded', () => {
    expect(summarizeOperation(op({ entryCount: 10 }), [entry('Observing')]).outcome).toBe('InProgress');
  });

  it('refuses to claim an outcome when only part of the operation is loaded', () => {
    const summary = summarizeOperation(op({ entryCount: 10 }), [entry('Recovered')]);
    expect(summary.outcome).toBe('Unknown');
    expect(summary.complete).toBe(false);
  });

  it('is Blocked when the gate declined every entry', () => {
    expect(summarizeOperation(op(), [entry('Declined'), entry('Declined')]).outcome).toBe('Blocked');
  });

  it('calls a completed purge Purged, not Recovered', () => {
    expect(summarizeOperation(op({ kind: 'Purge' }), [entry('Discarded'), entry('Discarded')]).outcome).toBe('Purged');
  });

  it('marks an all-unverified operation as needing review', () => {
    expect(summarizeOperation(op(), [entry('Unverified'), entry('Unverified')]).outcome).toBe('NeedsReview');
  });
});

describe('computeLedgerStats', () => {
  it('has no recovery rate until something has a verified verdict', () => {
    expect(computeLedgerStats([entry('Unverified'), entry('Declined')]).verifiedRecoveryRate).toBeNull();
  });

  it('excludes unverified entries from the rate rather than counting them as failures', () => {
    const stats = computeLedgerStats([entry('Recovered'), entry('Recovered'), entry('Recovered'), entry('Returned'), entry('Unverified')]);
    expect(stats.verifiedRecoveryRate).toBe(0.75);
    expect(stats.counts.unresolved).toBe(1);
  });
});

describe('buildRecoveryLifecycle', () => {
  it('tells a human-approved replay end to end', () => {
    const steps = buildRecoveryLifecycle({
      operation: op(),
      entries: [entry('Recovered'), entry('Recovered')],
      events: [event('EntryBegun'), event('ProviderAccepted'), event('ProviderAccepted')],
    });
    expect(steps.map(s => s.key)).toEqual(['detect', 'diagnose', 'propose', 'approve', 'execute', 'verify']);
    expect(steps[0].title).toBe('Dead-lettered on orders');
    expect(steps[1].title).toBe('Classified as TransientDependency');
    expect(steps[3].title).toBe('Authorized by alex@contoso.com');
    expect(steps[4].title).toBe('Asked Azure Service Bus to replay 2 messages');
    expect(steps[4].status).toBe('done');
    expect(steps[5].status).toBe('done');
  });

  it('says no human approval was needed for an automated replay', () => {
    const steps = buildRecoveryLifecycle({
      operation: op({ actorKind: 'Automation', actorIdentity: 'Rule:8', trigger: 'AutoRule', sourceRuleId: 8 }),
      entries: [entry('Recovered'), entry('Recovered')],
      events: [event('ProviderAccepted'), event('ProviderAccepted')],
    });
    expect(steps[2].title).toBe('Matched Auto-Replay Rule #8');
    expect(steps[3].title).toBe('No human approval required');
  });

  it('shows a gate refusal as blocked, with the recorded reason, and nothing executed', () => {
    const steps = buildRecoveryLifecycle({
      operation: op({ actorKind: 'Automation', trigger: 'AutoRule' }),
      entries: [entry('Declined'), entry('Declined')],
      events: [event('DispositionSet', { entryId: 'e1', detailJson: '{"reasonCode":"PROVIDER_CANNOT_VERIFY_ABSENCE"}' })],
    });
    expect(steps[3].status).toBe('blocked');
    expect(steps[3].detail).toMatch(/cannot independently verify DLQ absence/);
    expect(steps[4].status).toBe('skipped');
  });

  it('surfaces why an AWS replay could not be verified', () => {
    const steps = buildRecoveryLifecycle({
      operation: op({ providerSnapshot: 'aws', entryCount: 1 }),
      entries: [entry('Unverified', { providerSnapshot: 'aws' })],
      events: [event('ProviderAccepted'), event('ObservationUnavailable', { entryId: 'e1', detailJson: '{"reason":"AWS_NO_ABSENCE_PROOF"}' })],
    });
    expect(steps[5].status).toBe('warn');
    expect(steps[5].detail).toMatch(/AWS SQS has no non-destructive peek/);
  });

  it('never claims verification for a purge', () => {
    const steps = buildRecoveryLifecycle({
      operation: op({ kind: 'Purge' }),
      entries: [entry('Discarded'), entry('Discarded')],
      events: [],
    });
    expect(steps[5].status).toBe('skipped');
    expect(steps[4].title).toMatch(/purge 2 messages/);
  });
});
