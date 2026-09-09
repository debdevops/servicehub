import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import HomePage from '@/pages/HomePage';
import { useAttentionQueue } from '@servicehub/ui-shared/hooks/useAttentionQueue';
import { useOutcomeMetrics } from '@servicehub/ui-shared/hooks/useRecoveryLedger';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';

vi.mock('@servicehub/ui-shared/hooks/useAttentionQueue', () => ({
  useAttentionQueue: vi.fn(),
}));

vi.mock('@servicehub/ui-shared/hooks/useRecoveryLedger', () => ({
  useOutcomeMetrics: vi.fn(),
}));

vi.mock('@servicehub/ui-shared/lib/demo/DemoContext', () => ({
  useDemoContext: vi.fn(),
}));

const mockUseAttentionQueue = useAttentionQueue as ReturnType<typeof vi.fn>;
const mockUseOutcomeMetrics = useOutcomeMetrics as ReturnType<typeof vi.fn>;
const mockUseDemoContext = useDemoContext as ReturnType<typeof vi.fn>;

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/']}>
      <HomePage />
    </MemoryRouter>,
  );
}

describe('HomePage', () => {
  beforeEach(() => {
    mockUseDemoContext.mockReturnValue({ isDemoMode: false, cloudProvider: null });
    mockUseOutcomeMetrics.mockReturnValue({ data: undefined, isLoading: true, isError: false });
  });

  it('shows loading skeletons while the attention queue is fetching', () => {
    mockUseAttentionQueue.mockReturnValue({ data: undefined, isLoading: true, isError: false, refetch: vi.fn(), isFetching: true });
    renderPage();
    expect(screen.getByText('Home')).toBeInTheDocument();
  });

  it('shows an error state with a retry option', () => {
    mockUseAttentionQueue.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn(), isFetching: false });
    renderPage();
    expect(screen.getByText("Couldn't load the attention queue")).toBeInTheDocument();
  });

  it('shows the healthy empty state when nothing needs attention', () => {
    mockUseAttentionQueue.mockReturnValue({ data: { items: [], isEmpty: true }, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();
    expect(screen.getByText('Everything looks healthy')).toBeInTheDocument();
  });

  it('renders ranked attention cards from real queue data', () => {
    mockUseAttentionQueue.mockReturnValue({
      data: {
        isEmpty: false,
        items: [
          {
            signatureHash: 'sig-1',
            namespaceId: 'ns-1',
            namespaceName: 'orders-ns',
            displayName: 'Payment webhook timeout',
            lifecycleStatus: 'Active',
            severity: 'Critical',
            blastRadius: 12,
            isRecurring: true,
            pendingDecisionCount: 1,
            score: 0.9,
            recommendedAction: 'Replay after outage clears',
            lastSeenAt: new Date().toISOString(),
          },
        ],
      },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
      isFetching: false,
    });

    renderPage();

    expect(screen.getByText('Payment webhook timeout')).toBeInTheDocument();
    expect(screen.getByText('orders-ns')).toBeInTheDocument();
    expect(screen.getByText('Critical')).toBeInTheDocument();
    expect(screen.getByText('1 pending decision')).toBeInTheDocument();
    expect(screen.getByText('Recurring')).toBeInTheDocument();
    expect(screen.getByText('Replay after outage clears')).toBeInTheDocument();
  });

  it('shows the this-week outcomes tiles only once real recovery activity exists', () => {
    mockUseAttentionQueue.mockReturnValue({ data: { items: [], isEmpty: true }, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    mockUseOutcomeMetrics.mockReturnValue({
      data: {
        messagesRecovered: 42,
        messagesAbandoned: 3,
        medianSecondsToVerifiedRecovery: 125,
        autonomousRecoveries: 10,
        gateRefusals: 2,
      },
      isLoading: false,
      isError: false,
    });

    renderPage();

    expect(screen.getByText('This week')).toBeInTheDocument();
    expect(screen.getByText('42')).toBeInTheDocument();
    expect(screen.getByText('Recovered')).toBeInTheDocument();
  });

  it('renders nothing for this-week outcomes when the fleet has been quiet', () => {
    mockUseAttentionQueue.mockReturnValue({ data: { items: [], isEmpty: true }, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    mockUseOutcomeMetrics.mockReturnValue({
      data: { messagesRecovered: 0, messagesAbandoned: 0, medianSecondsToVerifiedRecovery: null, autonomousRecoveries: 0, gateRefusals: 0 },
      isLoading: false,
      isError: false,
    });

    renderPage();

    expect(screen.queryByText('This week')).not.toBeInTheDocument();
  });
});
