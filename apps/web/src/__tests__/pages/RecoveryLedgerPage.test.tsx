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
        entryCount: 214,
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

  it('shows the real entry count for an auto-replay rule tick, not its unknown-up-front target count of 0', () => {
    mockUseRecoveryOperations.mockReturnValue({
      data: [{
        id: 'op-2',
        kind: 'Replay',
        trigger: 'AutoRule',
        actorIdentity: 'Rule:8',
        actorKind: 'Automation',
        reason: 'Auto: DeserializationError',
        namespaceId: 'ns-1',
        namespaceNameSnapshot: 'Azure DEV',
        providerSnapshot: 'azure',
        environmentSnapshot: 'dev',
        scopeDescription: 'auto-replay rule 8',
        sourceRuleId: 8,
        sourceJobId: null,
        serviceVersion: '3.7.0',
        openedAt: '2026-08-20T11:00:00Z',
        targetCount: 0,
        entryCount: 5,
      }],
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
      isFetching: false,
    });
    renderPage();
    expect(screen.getByText('5')).toBeInTheDocument();
    expect(screen.queryByText('0')).not.toBeInTheDocument();
  });

  it('shows an error state with a retry option', () => {
    mockUseRecoveryOperations.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn(), isFetching: false });
    renderPage();
    expect(screen.getByText('Failed to load recovery operations')).toBeInTheDocument();
  });
});
