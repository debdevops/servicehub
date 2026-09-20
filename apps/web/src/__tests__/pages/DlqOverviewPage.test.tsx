import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import DlqOverviewPage from '@/pages/DlqOverviewPage';

vi.mock('@servicehub/ui-shared/hooks/useDlqOverview', () => ({
  useDlqOverview: vi.fn(),
}));

vi.mock('@servicehub/ui-shared/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));

const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

import { useDlqOverview } from '@servicehub/ui-shared/hooks/useDlqOverview';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
const mockUseDlqOverview = useDlqOverview as ReturnType<typeof vi.fn>;
const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;

const sampleOverview = {
  generatedAt: '2026-09-11T19:44:00Z',
  windowDays: 7,
  totals: {
    totalDeadLettered: 320,
    changePercent: 18,
    namespacesWithDlq: 2,
    namespacesTotal: 3,
    namespacesWithDlqChangePercent: 25,
    affectedQueues: 12,
    affectedQueuesChangePercent: 9,
    affectedTopics: 8,
    affectedTopicsChangePercent: 14,
    oldestMessageDetectedAt: '2026-09-05T10:24:00Z',
    replayedCount: 45,
    replayedChangePercent: 12,
    archivedCount: 5,
    totalObserved: 400,
    recurringPatternCount: 2,
    needsInvestigationCount: 1,
  },
  providers: [
    {
      provider: 'aws',
      totalDeadLettered: 320,
      changePercent: 18,
      namespacesWithDlq: 2,
      namespacesTotal: 2,
      affectedQueues: 12,
      affectedTopics: 8,
      dailyTrend: Array.from({ length: 7 }, (_, i) => ({
        date: `2026-09-0${i + 1}T00:00:00Z`,
        count: i * 30,
      })),
      topReasons: [
        { category: 'processingError', count: 120, percent: 38 },
        { category: 'maxDelivery', count: 82, percent: 26 },
      ],
      namespaces: [
        {
          namespaceId: 'ns-devaws',
          namespaceName: 'DEVAWS',
          environment: 'dev',
          queuesWithDlq: 2,
          topicsWithDlq: 2,
          dlqCount: 200,
          oldestDetectedAt: '2026-09-05T10:24:00Z',
        },
        {
          namespaceId: 'ns-orders',
          namespaceName: 'OrdersNS',
          environment: 'uat',
          queuesWithDlq: 1,
          topicsWithDlq: 1,
          dlqCount: 120,
          oldestDetectedAt: '2026-09-08T06:00:00Z',
        },
      ],
    },
  ],
  recurringPatterns: [],
};

function renderPage() {
  return render(
    <MemoryRouter>
      <DlqOverviewPage />
    </MemoryRouter>,
  );
}

describe('DlqOverviewPage', () => {
  beforeEach(() => {
    mockNavigate.mockReset();
    mockUseNamespaces.mockReturnValue({ data: [] });
  });

  it('shows a loading state while the overview is fetching', () => {
    mockUseDlqOverview.mockReturnValue({ data: undefined, isLoading: true, isError: false, isFetching: true, refetch: vi.fn() });
    renderPage();
    expect(screen.getByText(/Loading DLQ overview/i)).toBeInTheDocument();
  });

  it('shows an error state with a retry action', () => {
    const refetch = vi.fn();
    mockUseDlqOverview.mockReturnValue({ data: undefined, isLoading: false, isError: true, isFetching: false, refetch });
    renderPage();
    expect(screen.getByText(/Failed to load the DLQ overview/i)).toBeInTheDocument();
    fireEvent.click(screen.getByText('Try Again'));
    expect(refetch).toHaveBeenCalled();
  });

  it('renders the KPI strip from the totals', () => {
    mockUseDlqOverview.mockReturnValue({ data: sampleOverview, isLoading: false, isError: false, isFetching: false, refetch: vi.fn() });
    renderPage();
    expect(screen.getByText('Total Dead-Lettered')).toBeInTheDocument();
    expect(screen.getByText('Namespaces with DLQ')).toBeInTheDocument();
    expect(screen.getByText('Affected Queues')).toBeInTheDocument();
    expect(screen.getByText('Affected Topics')).toBeInTheDocument();
    expect(screen.getByText('Oldest Message')).toBeInTheDocument();
    expect(screen.getAllByText('320').length).toBeGreaterThan(0);
    expect(screen.getByText('2 / 3')).toBeInTheDocument();
  });

  it('renders the page title, breadcrumb, and view toggle', () => {
    mockUseDlqOverview.mockReturnValue({ data: sampleOverview, isLoading: false, isError: false, isFetching: false, refetch: vi.fn() });
    renderPage();
    expect(screen.getByText('Dead-Letter Overview')).toBeInTheDocument();
    expect(screen.getByText('Fleet Overview')).toBeInTheDocument();
    expect(screen.getByText('Messages')).toBeInTheDocument();

    fireEvent.click(screen.getByText('Active'));
    expect(mockNavigate).toHaveBeenCalledWith('/messages-overview?tab=active');
  });

  it('renders a provider section with its reasons and namespace table', () => {
    mockUseDlqOverview.mockReturnValue({ data: sampleOverview, isLoading: false, isError: false, isFetching: false, refetch: vi.fn() });
    renderPage();
    expect(screen.getAllByText('Processing Error').length).toBeGreaterThan(0);
    expect(screen.getByText('DEVAWS')).toBeInTheDocument();
    expect(screen.getByText('OrdersNS')).toBeInTheDocument();
  });

  it('navigates a provider section "View All" into Messages Overview filtered by cloud', () => {
    mockUseDlqOverview.mockReturnValue({ data: sampleOverview, isLoading: false, isError: false, isFetching: false, refetch: vi.fn() });
    renderPage();
    fireEvent.click(screen.getByText('View All'));
    expect(mockNavigate).toHaveBeenCalledWith('/messages-overview?tab=deadletter&cloud=aws');
  });

  it('navigates to DLQ Message History for a namespace when View is clicked', () => {
    mockUseDlqOverview.mockReturnValue({ data: sampleOverview, isLoading: false, isError: false, isFetching: false, refetch: vi.fn() });
    renderPage();
    const row = screen.getByText('DEVAWS').closest('tr')!;
    fireEvent.click(within(row).getByText('View'));
    expect(mockNavigate).toHaveBeenCalledWith('/dlq-history?namespace=ns-devaws');
  });

  it('filters the namespace table by search text', () => {
    mockUseDlqOverview.mockReturnValue({ data: sampleOverview, isLoading: false, isError: false, isFetching: false, refetch: vi.fn() });
    renderPage();
    fireEvent.change(screen.getByPlaceholderText(/Search namespaces/i), { target: { value: 'orders' } });
    expect(screen.queryByText('DEVAWS')).not.toBeInTheDocument();
    expect(screen.getByText('OrdersNS')).toBeInTheDocument();
  });

  it('shows an empty state when no providers match the current filters', () => {
    mockUseDlqOverview.mockReturnValue({
      data: { ...sampleOverview, providers: [] },
      isLoading: false,
      isError: false,
      isFetching: false,
      refetch: vi.fn(),
    });
    renderPage();
    expect(screen.getByText(/No namespaces match the current filters/i)).toBeInTheDocument();
  });

  it('passes the entity, status, and replay-safety filters through to the query once revealed', () => {
    mockUseDlqOverview.mockReturnValue({ data: sampleOverview, isLoading: false, isError: false, isFetching: false, refetch: vi.fn() });
    renderPage();

    fireEvent.click(screen.getByLabelText('More filters'));
    fireEvent.change(screen.getByPlaceholderText(/Entity name/i), { target: { value: 'orders-queue' } });
    fireEvent.change(screen.getByLabelText('Filter by status'), { target: { value: 'archived' } });
    fireEvent.change(screen.getByLabelText('Filter by replay safety'), { target: { value: 'Unsafe' } });

    const calls = mockUseDlqOverview.mock.calls;
    const lastCall = calls[calls.length - 1][0];
    expect(lastCall).toMatchObject({ entityName: 'orders-queue', status: 'archived', replaySafety: 'Unsafe' });
  });
});
