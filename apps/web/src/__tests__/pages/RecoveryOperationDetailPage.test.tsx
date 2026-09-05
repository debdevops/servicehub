import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import RecoveryOperationDetailPage from '@/pages/RecoveryOperationDetailPage';
import { useRecoveryOperation, useWriteOffRecoveryEntry, useDownloadRecoveryExport, useRehearseRecoveryEntry } from '@servicehub/ui-shared/hooks/useRecoveryOperation';
import { useVerifyChain } from '@servicehub/ui-shared/hooks/useChainVerification';

vi.mock('@servicehub/ui-shared/hooks/useRecoveryOperation', () => ({
  useRecoveryOperation: vi.fn(),
  useWriteOffRecoveryEntry: vi.fn(),
  useDownloadRecoveryExport: vi.fn(),
  useRehearseRecoveryEntry: vi.fn(),
}));

vi.mock('@servicehub/ui-shared/hooks/useChainVerification', () => ({
  useVerifyChain: vi.fn(),
}));

const mockUseRecoveryOperation = useRecoveryOperation as ReturnType<typeof vi.fn>;
const mockUseWriteOff = useWriteOffRecoveryEntry as ReturnType<typeof vi.fn>;
const mockUseDownloadExport = useDownloadRecoveryExport as ReturnType<typeof vi.fn>;
const mockUseVerifyChain = useVerifyChain as ReturnType<typeof vi.fn>;
const mockUseRehearse = useRehearseRecoveryEntry as ReturnType<typeof vi.fn>;

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/recovery/op-1']}>
      <Routes>
        <Route path="/recovery/:operationId" element={<RecoveryOperationDetailPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

const baseEntry = {
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
  disposition: null,
  verificationResult: null,
  verificationConfidence: null,
  observationWindowEndsAt: null,
  closedAt: null,
};

describe('RecoveryOperationDetailPage', () => {
  beforeEach(() => {
    mockUseWriteOff.mockReturnValue({ mutate: vi.fn(), isPending: false });
    mockUseDownloadExport.mockReturnValue({ mutate: vi.fn(), isPending: false });
    mockUseVerifyChain.mockReturnValue({ mutate: vi.fn(), isPending: false });
    mockUseRehearse.mockReturnValue({ mutate: vi.fn(), isPending: false });
  });

  it('shows a not-found state when the operation does not exist', () => {
    mockUseRecoveryOperation.mockReturnValue({ data: undefined, isLoading: false, isError: true });
    renderPage();
    expect(screen.getByText('Recovery operation not found')).toBeInTheDocument();
  });

  it('renders the operation header, entries, and outcome counts', () => {
    mockUseRecoveryOperation.mockReturnValue({
      data: {
        operation: {
          id: 'op-1', kind: 'Replay', trigger: 'Manual', actorIdentity: 'alex@contoso.com',
          actorKind: 'User', reason: 'INC-4471', namespaceId: 'ns-1',
          namespaceNameSnapshot: 'contoso-prod', providerSnapshot: 'azure', environmentSnapshot: 'prod',
          scopeDescription: 'entity=orders-dlq', sourceRuleId: null, sourceJobId: null,
          serviceVersion: '3.7.0', openedAt: '2026-08-10T09:00:00Z', targetCount: 1, entryCount: 1,
        },
        entries: [{ ...baseEntry, state: 'Recovered' }],
        events: [
          { id: 'evt-1', ownerId: 'o', seq: 1, entryId: null, operationId: 'op-1', eventType: 'OperationOpened', occurredAt: '2026-08-10T09:00:00Z', actorIdentity: 'alex@contoso.com', actorKind: 'User', detailJson: null, prevHash: '0'.repeat(64), entryHash: 'h1', schemaVersion: 1 },
        ],
      },
      isLoading: false,
      isError: false,
    });

    renderPage();
    expect(screen.getByText(/Replay — entity=orders-dlq/)).toBeInTheDocument();
    expect(screen.getByText('alex@contoso.com')).toBeInTheDocument();
    expect(screen.getByText('1 Recovered')).toBeInTheDocument();
  });

  it('offers a write-off action only for non-terminal entries', () => {
    mockUseRecoveryOperation.mockReturnValue({
      data: {
        operation: {
          id: 'op-1', kind: 'Replay', trigger: 'Manual', actorIdentity: 'alex@contoso.com',
          actorKind: 'User', reason: null, namespaceId: 'ns-1',
          namespaceNameSnapshot: 'contoso-prod', providerSnapshot: 'azure', environmentSnapshot: 'prod',
          scopeDescription: 'entity=orders-dlq', sourceRuleId: null, sourceJobId: null,
          serviceVersion: '3.7.0', openedAt: '2026-08-10T09:00:00Z', targetCount: 2, entryCount: 2,
        },
        entries: [
          { ...baseEntry, id: 'entry-open', state: 'ExecutionUnknown' },
          { ...baseEntry, id: 'entry-closed', state: 'Recovered' },
        ],
        events: [],
      },
      isLoading: false,
      isError: false,
    });

    renderPage();
    const writeOffButtons = screen.getAllByText('Write off');
    expect(writeOffButtons).toHaveLength(1);

    fireEvent.click(writeOffButtons[0]);
    expect(screen.getByText('Write off entry')).toBeInTheDocument();
  });

  it('renders the recorded Unverified reason on the entry row and in the event-chain Detail column', () => {
    mockUseRecoveryOperation.mockReturnValue({
      data: {
        operation: {
          id: 'op-1', kind: 'Replay', trigger: 'Manual', actorIdentity: 'alex@contoso.com',
          actorKind: 'User', reason: null, namespaceId: 'ns-1',
          namespaceNameSnapshot: 'acme-prod (AWS)', providerSnapshot: 'aws', environmentSnapshot: 'prod',
          scopeDescription: 'entity=orders-dlq', sourceRuleId: null, sourceJobId: null,
          serviceVersion: '3.7.0', openedAt: '2026-08-10T09:00:00Z', targetCount: 1, entryCount: 1,
        },
        entries: [{ ...baseEntry, id: 'entry-unverified', state: 'Unverified', verificationResult: 'Unverified' }],
        events: [
          {
            id: 'evt-1', ownerId: 'o', seq: 1, entryId: 'entry-unverified', operationId: 'op-1',
            eventType: 'ObservationUnavailable', occurredAt: '2026-08-10T10:00:00Z',
            actorIdentity: 'RecoveryVerificationWorker', actorKind: 'System',
            detailJson: '{"reason":"AWS_NO_ABSENCE_PROOF"}', prevHash: '0'.repeat(64), entryHash: 'h1', schemaVersion: 1,
          },
        ],
      },
      isLoading: false,
      isError: false,
    });

    renderPage();

    expect(
      screen.getByText('AWS SQS has no non-destructive peek; ServiceHub cannot prove a message did not return.')
    ).toBeInTheDocument();

    fireEvent.click(screen.getByText(/Event chain/));
    const detailHeader = screen.getByRole('columnheader', { name: 'Detail' });
    expect(detailHeader).toBeInTheDocument();
  });

  // ── Rehearsal mode (roadmap W1.2 / exit statement 5) ──────────────────────
  //
  // The endpoint shipped with W1.2 and nothing in the SPA called it, so "an operator can rehearse
  // any recovery decision" meant "an operator with curl can". These pin the surface that closes it.

  function renderWithOneEntry() {
    mockUseRecoveryOperation.mockReturnValue({
      data: {
        operation: {
          id: 'op-1', kind: 'Replay', trigger: 'AutoRule', actorIdentity: 'Rule:7@orders',
          actorKind: 'Automation', reason: null, namespaceId: 'ns-1',
          namespaceNameSnapshot: 'contoso-dev', providerSnapshot: 'azure', environmentSnapshot: 'dev',
          scopeDescription: 'entity=orders-dlq', sourceRuleId: 7, sourceJobId: null,
          serviceVersion: '3.7.0', openedAt: '2026-08-10T09:00:00Z', targetCount: 1, entryCount: 1,
        },
        entries: [{ ...baseEntry, id: 'entry-1', state: 'Recovered' }],
        events: [],
      },
      isLoading: false,
      isError: false,
    });
    return renderPage();
  }

  it('offers a rehearse action on every entry, terminal or not', () => {
    // Unlike write-off, rehearsal is meaningful on a closed entry too — "would the gate allow this
    // today" is exactly the question an operator asks about something that already happened.
    renderWithOneEntry();
    expect(screen.getByText('Rehearse')).toBeInTheDocument();
    expect(screen.queryByText('Write off')).not.toBeInTheDocument();
  });

  it('renders the gate verdict, its plain-language reason, and that nothing was executed', () => {
    const mutate = vi.fn((_vars, opts) => opts.onSuccess({
      entryId: 'entry-1',
      actorKindEvaluated: 'Automation',
      verdict: 'Escalate',
      reasonCode: 'AUTONOMY_GRANT_INSUFFICIENT',
      matchedCount: 0,
      evaluatedAt: '2026-09-05T12:00:00Z',
    }));
    mockUseRehearse.mockReturnValue({ mutate, isPending: false });

    renderWithOneEntry();
    fireEvent.click(screen.getByText('Rehearse'));

    expect(mutate).toHaveBeenCalledWith({ entryId: 'entry-1' }, expect.anything());
    expect(screen.getByText(/Would escalate to a human/)).toBeInTheDocument();
    // The reason code is translated, never shown raw.
    expect(screen.getByText('This failure signature has not yet earned unattended (Standing/Unattended) trust.')).toBeInTheDocument();
    expect(screen.queryByText('AUTONOMY_GRANT_INSUFFICIENT')).not.toBeInTheDocument();
    // The single most important sentence on this surface: a rehearsal is not an execution.
    expect(screen.getByText(/Nothing was executed and nothing was written to either ledger/)).toBeInTheDocument();
  });

  it('reports the recurrence-cap match count when the gate carried one', () => {
    const mutate = vi.fn((_vars, opts) => opts.onSuccess({
      entryId: 'entry-1',
      actorKindEvaluated: 'Automation',
      verdict: 'Escalate',
      reasonCode: 'RECURRENCE_CAP_EXCEEDED',
      matchedCount: 3,
      evaluatedAt: '2026-09-05T12:00:00Z',
    }));
    mockUseRehearse.mockReturnValue({ mutate, isPending: false });

    renderWithOneEntry();
    fireEvent.click(screen.getByText('Rehearse'));

    expect(screen.getByText(/3 prior attempts on this message's lineage were matched/)).toBeInTheDocument();
  });
});
