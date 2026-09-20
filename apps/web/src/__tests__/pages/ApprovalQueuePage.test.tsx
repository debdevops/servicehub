import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import ApprovalQueuePage from '@/pages/ApprovalQueuePage';
import { useApprovalQueue, useSignatureTrustEvidenceBatch } from '@servicehub/ui-shared/hooks/useRecoveryLedger';
import { useReplayMessage } from '@servicehub/ui-shared/hooks/useMessages';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';

vi.mock('@servicehub/ui-shared/hooks/useRecoveryLedger', () => ({
  useApprovalQueue: vi.fn(),
  useSignatureTrustEvidenceBatch: vi.fn(),
}));

vi.mock('@servicehub/ui-shared/hooks/useMessages', () => ({
  useReplayMessage: vi.fn(),
}));

vi.mock('@servicehub/ui-shared/lib/demo/DemoContext', () => ({
  useDemoContext: vi.fn(),
}));

const mockUseApprovalQueue = useApprovalQueue as ReturnType<typeof vi.fn>;
const mockUseSignatureTrustEvidenceBatch = useSignatureTrustEvidenceBatch as ReturnType<typeof vi.fn>;
const mockUseReplayMessage = useReplayMessage as ReturnType<typeof vi.fn>;
const mockUseDemoContext = useDemoContext as ReturnType<typeof vi.fn>;

const entry = {
  entryId: 'e1',
  namespaceId: 'ns-1',
  namespaceName: 'orders-ns',
  provider: 'azure',
  environment: 'Production',
  entityName: 'orders-queue',
  subscriptionName: null,
  sequenceNumber: 42,
  failureCategory: 'Poison',
  ruleId: 1,
  ruleName: 'auto-replay-poison',
  reasonCode: 'AUTONOMY_GRANT_INSUFFICIENT',
  matchedCount: 3,
  declinedAt: '2026-08-30T00:00:00Z',
  signatureHash: 'sig-abc',
};

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/approval-queue']}>
      <ApprovalQueuePage />
    </MemoryRouter>,
  );
}

describe('ApprovalQueuePage', () => {
  beforeEach(() => {
    mockUseDemoContext.mockReturnValue({ isDemoMode: false, cloudProvider: null });
    mockUseSignatureTrustEvidenceBatch.mockReturnValue(new Map());
    mockUseReplayMessage.mockReturnValue({ mutateAsync: vi.fn().mockResolvedValue(undefined) });
  });

  it('shows a loading state', () => {
    mockUseApprovalQueue.mockReturnValue({ data: undefined, isLoading: true, isError: false, refetch: vi.fn(), isFetching: true });
    renderPage();
    expect(screen.getByText('Approval Queue')).toBeInTheDocument();
  });

  it('shows an error state with a retry option', () => {
    mockUseApprovalQueue.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn(), isFetching: false });
    renderPage();
    expect(screen.getByText('Failed to load the approval queue')).toBeInTheDocument();
  });

  it('shows the empty state when nothing is waiting', () => {
    mockUseApprovalQueue.mockReturnValue({ data: [], isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();
    expect(screen.getByText('Nothing waiting on approval')).toBeInTheDocument();
  });

  it('renders queue entries with cloud/env badges and reason text', () => {
    mockUseApprovalQueue.mockReturnValue({ data: [entry], isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();
    expect(screen.getByText('auto-replay-poison')).toBeInTheDocument();
    expect(screen.getByText('orders-queue')).toBeInTheDocument();
    expect(screen.getByText(/3 prior matches/)).toBeInTheDocument();
  });

  it('opens a proposal on Review & Approve, showing scope and the escalation reason', async () => {
    mockUseApprovalQueue.mockReturnValue({ data: [entry], isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    const { default: userEvent } = await import('@testing-library/user-event');
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByLabelText(`Select entry for ${entry.entityName}`));
    await user.click(screen.getByRole('button', { name: /Review & Approve \(1\)/ }));

    expect(screen.getByText('Proposal — replay 1 message')).toBeInTheDocument();
    expect(screen.getAllByText('This failure signature has not yet earned unattended (Standing/Unattended) trust.')).toHaveLength(2);
  });

  it('shows the demo-mode banner and disables approval actions', () => {
    mockUseDemoContext.mockReturnValue({ isDemoMode: true, cloudProvider: 'azure' });
    mockUseApprovalQueue.mockReturnValue({ data: [], isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();
    expect(screen.getByText(/Demo Mode/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Refresh/ })).toBeDisabled();
  });
});
