import { describe, it, expect } from 'vitest';
import {
  RECOVERY_STATE_EXPLANATIONS,
  RECOVERY_UNVERIFIED_REASON_LABELS,
  describeRecoveryDetailReason,
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
