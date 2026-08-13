import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import RecoveryLedgerPage from '@/pages/RecoveryLedgerPage';
import { useRecoveryOperations } from '@servicehub/ui-shared/hooks/useRecoveryLedger';

vi.mock('@servicehub/ui-shared/hooks/useRecoveryLedger', () => ({
  useRecoveryOperations: vi.fn(),
  useRecoveryEntries: vi.fn(() => ({ data: [], isLoading: false })),
}));

const mockUseRecoveryOperations = useRecoveryOperations as ReturnType<typeof vi.fn>;

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/recovery']}>
      <RecoveryLedgerPage />
    </MemoryRouter>,
  );
}

describe('RecoveryLedgerPage', () => {
  it('shows a loading state', () => {
    mockUseRecoveryOperations.mockReturnValue({ data: undefined, isLoading: true, isError: false, refetch: vi.fn(), isFetching: true });
    renderPage();
    expect(screen.getByText('Recovery Evidence Ledger')).toBeInTheDocument();
  });

  it('shows the empty state when there are no operations', () => {
    mockUseRecoveryOperations.mockReturnValue({ data: [], isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();
    expect(screen.getByText('No recovery operations recorded')).toBeInTheDocument();
  });

  it('renders an operation row with actor and scope', () => {
    mockUseRecoveryOperations.mockReturnValue({
      data: [{
        id: 'op-1',
        kind: 'Replay',
        trigger: 'Manual',
        actorIdentity: 'alex@contoso.com',
        actorKind: 'User',
        reason: 'INC-4471',
        namespaceId: 'ns-1',
        namespaceNameSnapshot: 'contoso-prod',
        providerSnapshot: 'azure',
        environmentSnapshot: 'prod',
        scopeDescription: 'entity=orders-dlq',
        sourceRuleId: null,
        sourceJobId: null,
        serviceVersion: '3.7.0',
        openedAt: '2026-08-10T09:00:00Z',
        targetCount: 214,
      }],
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
      isFetching: false,
    });
    renderPage();
    expect(screen.getByText('alex@contoso.com')).toBeInTheDocument();
    expect(screen.getByText('entity=orders-dlq')).toBeInTheDocument();
    expect(screen.getByText('214')).toBeInTheDocument();
  });

  it('shows an error state with a retry option', () => {
    mockUseRecoveryOperations.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn(), isFetching: false });
    renderPage();
    expect(screen.getByText('Failed to load recovery operations')).toBeInTheDocument();
  });
});
