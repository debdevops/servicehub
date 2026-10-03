import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import RecoveryAgeingPage from '@/pages/RecoveryAgeingPage';
import { useRecoveryAgeing } from '@servicehub/ui-shared/hooks/useRecoveryAgeing';

vi.mock('@servicehub/ui-shared/hooks/useRecoveryAgeing', () => ({
  useRecoveryAgeing: vi.fn(),
}));

const mockUseRecoveryAgeing = useRecoveryAgeing as ReturnType<typeof vi.fn>;

function renderPage() {
  return render(
    <MemoryRouter>
      <RecoveryAgeingPage />
    </MemoryRouter>,
  );
}

describe('RecoveryAgeingPage', () => {
  it('shows the empty state when nothing is open', () => {
    mockUseRecoveryAgeing.mockReturnValue({ data: [], isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();
    expect(screen.getByText('No open recovery entries')).toBeInTheDocument();
  });

  it('renders an open entry with its age and flags it past the 7-day default threshold', () => {
    const begunAt = new Date(Date.now() - 10 * 86_400_000).toISOString();
    mockUseRecoveryAgeing.mockReturnValue({
      data: [{
        id: 'entry-1',
        operationId: 'op-1',
        targetEntity: 'orders-dlq',
        state: 'ExecutionUnknown',
        namespaceNameSnapshot: 'contoso-prod',
        begunAt,
      }],
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
      isFetching: false,
    });
    renderPage();
    expect(screen.getByText('orders-dlq')).toBeInTheDocument();
    expect(screen.getByText('ExecutionUnknown')).toBeInTheDocument();
    expect(screen.getByText('· flagged')).toBeInTheDocument();
  });

  it('does not flag a recently opened entry', () => {
    const begunAt = new Date(Date.now() - 1 * 86_400_000).toISOString();
    mockUseRecoveryAgeing.mockReturnValue({
      data: [{
        id: 'entry-2',
        operationId: 'op-1',
        targetEntity: 'payments-dlq',
        state: 'Observing',
        namespaceNameSnapshot: 'contoso-prod',
        begunAt,
      }],
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
      isFetching: false,
    });
    renderPage();
    expect(screen.queryByText('· flagged')).not.toBeInTheDocument();
  });
});
