import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { VerificationResultNote } from '@/components/recovery/VerificationResultNote';
import { RECOVERY_LIMITATION_SENTENCE, type RecoveryLedgerEntry } from '@servicehub/ui-shared/lib/api/recovery';

function makeEntry(overrides: Partial<RecoveryLedgerEntry> = {}): RecoveryLedgerEntry {
  return {
    id: 'entry-1',
    operationId: 'op-1',
    dlqMessageId: 1,
    namespaceId: 'ns-1',
    namespaceNameSnapshot: 'contoso-prod',
    providerSnapshot: 'azure',
    environmentSnapshot: 'prod',
    entityNameSnapshot: 'orders-dlq',
    entityTypeSnapshot: 'Queue',
    topicNameSnapshot: null,
    bodyHash: 'sha256-abc',
    failureCategorySnapshot: null,
    deadLetterReasonSnapshot: null,
    signatureHashSnapshot: null,
    targetEntity: 'orders-dlq',
    begunAt: '2026-08-10T09:00:00Z',
    markerApplied: true,
    state: 'Recovered',
    disposition: 'Recovered',
    verificationResult: 'Recovered',
    verificationConfidence: null,
    observationWindowEndsAt: '2026-08-11T09:00:00Z',
    closedAt: '2026-08-11T09:00:01Z',
    ...overrides,
  };
}

// Roadmap §13.4: a conformance requirement, not a nice-to-have — every component rendering a
// verification result must show the limitation sentence, verbatim, every time. This asserts it
// for every RecoveryEntryState the component can be given, not just the "happy" ones.
describe('VerificationResultNote — honesty conformance', () => {
  const states: RecoveryLedgerEntry['state'][] = [
    'Executing', 'Observing', 'ExecutionFailed', 'ExecutionUnknown',
    'Recovered', 'Returned', 'Discarded', 'Unverified', 'WrittenOff', 'Expired',
  ];

  it.each(states)('renders the limitation sentence verbatim for state=%s', (state) => {
    render(<VerificationResultNote entry={makeEntry({ state })} />);
    expect(screen.getByText(RECOVERY_LIMITATION_SENTENCE)).toBeInTheDocument();
  });

  it('never renders "Unverified" without the limitation sentence right beside it', () => {
    render(<VerificationResultNote entry={makeEntry({ state: 'Unverified', verificationResult: 'Unverified' })} />);
    expect(screen.getByText('Unverified')).toBeInTheDocument();
    expect(screen.getByText(RECOVERY_LIMITATION_SENTENCE)).toBeInTheDocument();
  });

  it('labels a heuristic (body-hash) match distinctly from an exact marker match', () => {
    render(<VerificationResultNote entry={makeEntry({ state: 'Returned', verificationConfidence: 'Heuristic' })} />);
    expect(screen.getByText(/Heuristic match/)).toBeInTheDocument();
  });

  it('labels an exact marker match distinctly', () => {
    render(<VerificationResultNote entry={makeEntry({ state: 'Returned', verificationConfidence: 'Exact' })} />);
    expect(screen.getByText(/Exact match/)).toBeInTheDocument();
  });

  it('forwards the Unverified reason to the state badge', () => {
    render(
      <VerificationResultNote
        entry={makeEntry({ state: 'Unverified', verificationResult: 'Unverified' })}
        reasonText="AWS SQS has no non-destructive peek; ServiceHub cannot prove a message did not return."
      />
    );
    expect(
      screen.getByText('AWS SQS has no non-destructive peek; ServiceHub cannot prove a message did not return.')
    ).toBeInTheDocument();
  });
});
