import { describe, it, expect } from 'vitest';
import {
  RECOVERY_STATE_EXPLANATIONS,
  RECOVERY_UNVERIFIED_REASON_LABELS,
  APPROVAL_QUEUE_REASON_LABELS,
  describeRecoveryDetailReason,
  describeRecoveryEventDetail,
  describeApprovalQueueReason,
  type RecoveryEntryState,
} from '../../../lib/api/recovery';

const ALL_STATES: RecoveryEntryState[] = [
  'Executing', 'Observing', 'ExecutionFailed', 'ExecutionUnknown',
  'Recovered', 'Returned', 'Discarded', 'Unverified', 'WrittenOff', 'Expired', 'Declined',
];

describe('RECOVERY_STATE_EXPLANATIONS', () => {
  it.each(ALL_STATES)('has a complete, non-empty explanation for %s', (state) => {
    const explanation = RECOVERY_STATE_EXPLANATIONS[state];
    expect(explanation).toBeDefined();
    expect(explanation.summary.length).toBeGreaterThan(0);
    expect(explanation.whatHappened.length).toBeGreaterThan(0);
    expect(explanation.whyKnown.length).toBeGreaterThan(0);
    expect(explanation.cannotProve.length).toBeGreaterThan(0);
    expect(explanation.nextStep.length).toBeGreaterThan(0);
  });

  it('covers exactly the 11 known states, no more, no fewer', () => {
    expect(Object.keys(RECOVERY_STATE_EXPLANATIONS).sort()).toEqual([...ALL_STATES].sort());
  });
});

describe('describeRecoveryDetailReason', () => {
  it('returns null for null input', () => {
    expect(describeRecoveryDetailReason(null)).toBeNull();
  });

  it('returns null for malformed JSON', () => {
    expect(describeRecoveryDetailReason('not json')).toBeNull();
  });

  it.each(Object.keys(RECOVERY_UNVERIFIED_REASON_LABELS))('resolves the known code %s to its human sentence', (code) => {
    const detailJson = JSON.stringify({ reason: code });
    expect(describeRecoveryDetailReason(detailJson)).toBe(RECOVERY_UNVERIFIED_REASON_LABELS[code]);
  });

  it('returns the raw code, not a fabricated label, for an unrecognized reason code', () => {
    expect(describeRecoveryDetailReason(JSON.stringify({ reason: 'SOME_FUTURE_CODE' }))).toBe('SOME_FUTURE_CODE');
  });

  it('returns null when the reason field is absent', () => {
    expect(describeRecoveryDetailReason(JSON.stringify({ somethingElse: true }))).toBeNull();
  });
});

// The event chain records a reason in three different shapes depending on which ledger writer
// produced the event. Rendering all of them through the `reason`-only parser dropped the
// recorded reason for the overwhelming majority of real events (gate refusals and provider
// rejections both) and showed "—" instead.
describe('describeRecoveryEventDetail', () => {
  it('returns null for null or empty input', () => {
    expect(describeRecoveryEventDetail(null)).toBeNull();
    expect(describeRecoveryEventDetail('')).toBeNull();
  });

  it('still resolves the verification-coverage {"reason"} shape', () => {
    expect(describeRecoveryEventDetail(JSON.stringify({ reason: 'AWS_NO_ABSENCE_PROOF' })))
      .toBe(RECOVERY_UNVERIFIED_REASON_LABELS.AWS_NO_ABSENCE_PROOF);
  });

  it('resolves an EligibilityDeclined {"reasonCode"} shape to its human sentence', () => {
    const detailJson = JSON.stringify({ reasonCode: 'AUTONOMY_GRANT_INSUFFICIENT', matchedCount: 0 });
    expect(describeRecoveryEventDetail(detailJson))
      .toBe(APPROVAL_QUEUE_REASON_LABELS.AUTONOMY_GRANT_INSUFFICIENT);
  });

  it.each(Object.keys(APPROVAL_QUEUE_REASON_LABELS))('resolves reasonCode %s', (code) => {
    expect(describeRecoveryEventDetail(JSON.stringify({ reasonCode: code })))
      .toBe(APPROVAL_QUEUE_REASON_LABELS[code]);
  });

  it('returns an unrecognized reason code verbatim rather than dropping it', () => {
    expect(describeRecoveryEventDetail(JSON.stringify({ reasonCode: 'SOME_FUTURE_CODE' })))
      .toBe('SOME_FUTURE_CODE');
  });

  it("surfaces a ProviderRejected event's plain-text provider error verbatim", () => {
    expect(describeRecoveryEventDetail('An unexpected error occurred while purging the message.'))
      .toBe('An unexpected error occurred while purging the message.');
  });

  it('returns null when the detail genuinely records no reason', () => {
    expect(describeRecoveryEventDetail(JSON.stringify({ collisionCount: 1 }))).toBeNull();
  });
});

describe('describeApprovalQueueReason', () => {
  it('returns null for null input', () => {
    expect(describeApprovalQueueReason(null)).toBeNull();
  });

  it.each(Object.keys(APPROVAL_QUEUE_REASON_LABELS))('resolves the known code %s to its human sentence', (code) => {
    expect(describeApprovalQueueReason(code)).toBe(APPROVAL_QUEUE_REASON_LABELS[code]);
  });

  it('returns the raw code, not a fabricated label, for an unrecognized reason code', () => {
    expect(describeApprovalQueueReason('SOME_FUTURE_CODE')).toBe('SOME_FUTURE_CODE');
  });
});
