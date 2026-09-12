import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MessagesOverviewPage } from '@/pages/MessagesOverviewPage';

vi.mock('@servicehub/ui-shared/hooks/useNamespaces', () => ({ useNamespaces: vi.fn() }));
vi.mock('@servicehub/ui-shared/hooks/useQueues', () => ({ useQueues: vi.fn(), useAllNamespacesQueues: vi.fn() }));
vi.mock('@servicehub/ui-shared/hooks/useTopics', () => ({ useTopics: vi.fn() }));
vi.mock('@servicehub/ui-shared/hooks/useSubscriptions', () => ({ useSubscriptions: vi.fn() }));
vi.mock('@servicehub/ui-shared/hooks/useCloudBridge', () => ({ useProviderCapabilities: vi.fn() }));
vi.mock('@servicehub/ui-shared/hooks/useDlqOverview', () => ({ useDlqOverview: vi.fn() }));

const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useQueues, useAllNamespacesQueues } from '@servicehub/ui-shared/hooks/useQueues';
import { useTopics } from '@servicehub/ui-shared/hooks/useTopics';
import { useSubscriptions } from '@servicehub/ui-shared/hooks/useSubscriptions';
import { useProviderCapabilities } from '@servicehub/ui-shared/hooks/useCloudBridge';
import { useDlqOverview } from '@servicehub/ui-shared/hooks/useDlqOverview';

const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;
const mockUseQueues = useQueues as ReturnType<typeof vi.fn>;
const mockUseAllNamespacesQueues = useAllNamespacesQueues as ReturnType<typeof vi.fn>;
const mockUseTopics = useTopics as ReturnType<typeof vi.fn>;
const mockUseSubscriptions = useSubscriptions as ReturnType<typeof vi.fn>;
const mockUseProviderCapabilities = useProviderCapabilities as ReturnType<typeof vi.fn>;
const mockUseDlqOverview = useDlqOverview as ReturnType<typeof vi.fn>;

const azureNs = {
  id: 'ns-azure',
  name: 'sb-dev.servicebus.windows.net',
  displayName: 'Dev SB',
  isActive: true,
  environment: 'dev' as const,
  cloudProvider: 'azure' as const,
  hasListenPermission: true,
  hasSendPermission: true,
  hasManagePermission: true,
  createdAt: '2026-01-01T00:00:00Z',
};

const awsNs = {
  ...azureNs,
  id: 'ns-aws',
  name: 'sqs.ap-south-1.amazonaws.com',
  displayName: 'DevAWS',
  cloudProvider: 'aws' as const,
};

const azureQueues = [
  { name: 'orders', activeMessageCount: 4, deadLetterMessageCount: 1, scheduledMessageCount: 0, sizeInBytes: 0, status: 'Active' },
];

const awsQueues = [
  { name: 'sqs-orders', activeMessageCount: 2, deadLetterMessageCount: 3, scheduledMessageCount: 0, sizeInBytes: 0, status: 'Active', deadLetterTargetQueue: 'sqs-orders-dlq' },
  { name: 'sqs-orders-dlq', activeMessageCount: 3, deadLetterMessageCount: 0, scheduledMessageCount: 0, sizeInBytes: 0, status: 'Active' },
];

function renderPage(initialEntry = '/messages-overview') {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <QueryClientProvider client={queryClient}>
        <MessagesOverviewPage />
      </QueryClientProvider>
    </MemoryRouter>,
  );
}

describe('MessagesOverviewPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseNamespaces.mockReturnValue({ data: [azureNs, awsNs], isLoading: false, isFetching: false, dataUpdatedAt: 0 });
    mockUseQueues.mockImplementation((id: string) => ({
      data: id === 'ns-aws' ? awsQueues : azureQueues,
      isLoading: false,
      isError: false,
    }));
    mockUseAllNamespacesQueues.mockReturnValue([]);
    mockUseTopics.mockReturnValue({ data: [], isLoading: false });
    mockUseSubscriptions.mockReturnValue({ data: [], isLoading: false });
    mockUseProviderCapabilities.mockReturnValue({ data: undefined });
    mockUseDlqOverview.mockReturnValue({ data: undefined });
  });

  it('renders a section per namespace across providers', () => {
    renderPage();
    expect(screen.getByRole('heading', { name: 'Dev SB' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'DevAWS' })).toBeInTheDocument();
  });

  it('hides AWS companion DLQ queues as standalone widgets', () => {
    renderPage();
    expect(screen.getByText('sqs-orders')).toBeInTheDocument();
    expect(screen.queryByText('sqs-orders-dlq')).not.toBeInTheDocument();
  });

  it('navigates to the queue messages view on "View Messages" click (active tab)', () => {
    renderPage();
    const row = screen.getByText('orders').closest('tr') as HTMLElement;
    fireEvent.click(within(row).getByRole('button', { name: 'View Messages' }));
    expect(mockNavigate).toHaveBeenCalledWith(
      '/messages?namespace=ns-azure&queue=orders&queueType=active',
    );
  });

  it('navigates with deadletter queueType when the dead-letter tab is selected', () => {
    renderPage('/messages-overview?tab=deadletter');
    const row = screen.getByText('sqs-orders').closest('tr') as HTMLElement;
    fireEvent.click(within(row).getByRole('button', { name: 'View Messages' }));
    expect(mockNavigate).toHaveBeenCalledWith(
      '/messages?namespace=ns-aws&queue=sqs-orders&queueType=deadletter',
    );
  });

  it('shows the dead-letter header when tab=deadletter', () => {
    renderPage('/messages-overview?tab=deadletter');
    expect(screen.getByText('Dead-Letter Overview')).toBeInTheDocument();
  });

  it('shows connect CTA when no namespaces exist', () => {
    mockUseNamespaces.mockReturnValue({ data: [], isLoading: false, isFetching: false, dataUpdatedAt: 0 });
    renderPage();
    expect(screen.getByText('No namespaces connected')).toBeInTheDocument();
  });

  it('sorts queues busiest-first and caps the grid height for many queues', () => {
    const manyQueues = Array.from({ length: 30 }, (_, i) => ({
      name: `q-${i}`,
      activeMessageCount: i,
      deadLetterMessageCount: 0,
      scheduledMessageCount: 0,
      sizeInBytes: 0,
      status: 'Active',
    }));
    mockUseNamespaces.mockReturnValue({ data: [azureNs], isLoading: false, isFetching: false, dataUpdatedAt: 0 });
    mockUseQueues.mockReturnValue({ data: manyQueues, isLoading: false, isError: false });

    const { container } = renderPage();

    // Busiest queue (q-29) renders before the quietest (q-0)
    const labels = Array.from(container.querySelectorAll('table tbody tr td:first-child span')).map(
      (el) => el.textContent,
    );
    expect(labels.indexOf('q-29')).toBeGreaterThan(-1);
    expect(labels.indexOf('q-29')).toBeLessThan(labels.indexOf('q-0'));
    // Grid scrolls inside a capped container instead of growing the page
    expect(container.querySelector('.max-h-64.overflow-y-auto')).toBeTruthy();
    expect(screen.getByText('Queues (30)')).toBeInTheDocument();
  });

  it('search filters entities and hides namespaces without matches', () => {
    renderPage();
    fireEvent.change(screen.getByLabelText('Search entities'), { target: { value: 'sqs' } });
    expect(screen.getByText('sqs-orders')).toBeInTheDocument();
    // Azure namespace has no matching entities → its whole section disappears
    expect(screen.queryByRole('heading', { name: 'Dev SB' })).not.toBeInTheDocument();
    expect(screen.queryByText('orders')).not.toBeInTheDocument();
  });

  it('shows every entity in a namespace when the search matches the namespace name itself', () => {
    renderPage();
    fireEvent.change(screen.getByLabelText('Search entities'), { target: { value: 'dev sb' } });
    expect(screen.getByRole('heading', { name: 'Dev SB' })).toBeInTheDocument();
    expect(screen.getByText('orders')).toBeInTheDocument();
  });

  it('sections collapse and expand from the header', () => {
    mockUseNamespaces.mockReturnValue({ data: [azureNs], isLoading: false, isFetching: false, dataUpdatedAt: 0 });
    renderPage();
    expect(screen.getByText('orders')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Dev SB/ }));
    expect(screen.queryByText('orders')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Dev SB/ }));
    expect(screen.getByText('orders')).toBeInTheDocument();
  });

  it('filters sections by cloud provider', () => {
    renderPage();
    fireEvent.change(screen.getByLabelText('Filter by cloud'), { target: { value: 'aws' } });
    expect(screen.getByRole('heading', { name: 'DevAWS' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Dev SB' })).not.toBeInTheDocument();
  });

  it('filters sections down to a single selected namespace', () => {
    renderPage();
    fireEvent.change(screen.getByLabelText('Filter by namespace'), { target: { value: 'ns-azure' } });
    expect(screen.getByRole('heading', { name: 'Dev SB' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'DevAWS' })).not.toBeInTheDocument();
  });

  it('hides queues when the entity type filter is set to Topics only', () => {
    mockUseNamespaces.mockReturnValue({ data: [azureNs], isLoading: false, isFetching: false, dataUpdatedAt: 0 });
    mockUseTopics.mockReturnValue({ data: [{ name: 'orders-topic', subscriptionCount: 1, sizeInBytes: 0, status: 'Active' }], isLoading: false });
    renderPage();
    fireEvent.change(screen.getByLabelText('Filter by entity type'), { target: { value: 'topics' } });
    expect(screen.queryByText('orders')).not.toBeInTheDocument();
    expect(screen.getByText(/orders-topic/)).toBeInTheDocument();
  });

  it('clears all active filters', () => {
    renderPage();
    fireEvent.change(screen.getByLabelText('Search entities'), { target: { value: 'sqs' } });
    expect(screen.getByRole('button', { name: /clear filters/i })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /clear filters/i }));
    expect(screen.getByRole('heading', { name: 'Dev SB' })).toBeInTheDocument();
  });

  it('shows aggregate key metrics for the active tab', () => {
    mockUseAllNamespacesQueues.mockReturnValue([
      { namespaceId: 'ns-azure', totalActive: 4, totalDlq: 1, totalScheduled: 0, totalQueues: 1, totalTopics: 0, totalSubscriptions: 0, isLoading: false, isError: false },
      { namespaceId: 'ns-aws', totalActive: 2, totalDlq: 3, totalScheduled: 0, totalQueues: 2, totalTopics: 0, totalSubscriptions: 0, isLoading: false, isError: false },
    ]);
    renderPage();
    expect(screen.getByText('Total Active Messages')).toBeInTheDocument();
    expect(screen.getByText('6')).toBeInTheDocument(); // 4 + 2
    expect(screen.getByText('2 / 2')).toBeInTheDocument(); // namespaces with messages
  });

  // Flood-scale finding: at fleet scale, live per-namespace provider queries (useAllNamespacesQueues)
  // can still be loading/failing for some namespaces when the KPI tiles render — contributing 0 for
  // those namespaces even though their dead-letter history is fully known in the persisted ledger
  // (the same ledger DLQ Intelligence/Incident Center/Fleet Overview already read correctly). The
  // dead-letter tab's headline KPIs must come from the DB-backed /api/v1/dlq/overview (useDlqOverview),
  // not the live aggregate, specifically so this can never silently collapse to a misleading zero.
  it('sources the dead-letter tab KPIs from the DB-backed overview, not live per-namespace stats', () => {
    // Live stats report 0 for every namespace (as they would while still loading, or if the
    // namespace's live connection is degraded) — this must NOT be what the KPI tiles show.
    mockUseAllNamespacesQueues.mockReturnValue([
      { namespaceId: 'ns-azure', totalActive: 0, totalDlq: 0, totalScheduled: 0, totalQueues: 1, totalTopics: 0, totalSubscriptions: 0, isLoading: true, isError: false },
      { namespaceId: 'ns-aws', totalActive: 0, totalDlq: 0, totalScheduled: 0, totalQueues: 2, totalTopics: 0, totalSubscriptions: 0, isLoading: true, isError: false },
    ]);
    // The persisted DLQ ledger knows the real total regardless of live connectivity.
    mockUseDlqOverview.mockReturnValue({
      data: { totals: { totalDeadLettered: 14210, namespacesWithDlq: 35, namespacesTotal: 36 } },
    });

    renderPage('/messages-overview?tab=deadletter');

    expect(screen.getByText('Total Dead-Letter Messages')).toBeInTheDocument();
    // The misleading-zero regression this guards against: the live aggregate for these two
    // namespaces is 0, and that must not be what "Total Dead-Letter Messages" shows — it must
    // show the ledger's real total instead.
    expect(screen.getByText('14,210')).toBeInTheDocument();
    expect(screen.getByText('35 / 2')).toBeInTheDocument(); // numerator from the ledger, denominator from the page's own namespace list
  });

  // Flood-scale finding, session 2: even after sourcing the KPIs from the DB-backed overview,
  // the tiles rendered a bare "0" (indistinguishable from a real zero) for however long that
  // request's first fetch took — which under fleet-scale live-namespace fan-out (this same
  // page's own Queues/Topics counts) can be many seconds. A tile must show a loading placeholder
  // during that window, not a number that looks confirmed.
  it('shows a loading placeholder, not a bare zero, while the DB-backed overview is on its first fetch', () => {
    mockUseAllNamespacesQueues.mockReturnValue([
      { namespaceId: 'ns-azure', totalActive: 0, totalDlq: 0, totalScheduled: 0, totalQueues: 1, totalTopics: 0, totalSubscriptions: 0, isLoading: true, isError: false },
      { namespaceId: 'ns-aws', totalActive: 0, totalDlq: 0, totalScheduled: 0, totalQueues: 2, totalTopics: 0, totalSubscriptions: 0, isLoading: true, isError: false },
    ]);
    mockUseDlqOverview.mockReturnValue({ data: undefined, isLoading: true });

    renderPage('/messages-overview?tab=deadletter');

    const totalTile = screen.getByText('Total Dead-Letter Messages').closest('div.flex') as HTMLElement;
    const namespacesTile = screen.getByText('Namespaces with Messages').closest('div.flex') as HTMLElement;
    expect(within(totalTile).queryByText('0')).not.toBeInTheDocument();
    expect(within(namespacesTile).queryByText(/0 \/ /)).not.toBeInTheDocument();
    expect(screen.getAllByLabelText('Loading')).toHaveLength(2); // Total Dead-Letter Messages + Namespaces with Messages
  });

  it('falls back to the live aggregate for the dead-letter KPIs while the DB-backed overview has not loaded yet', () => {
    mockUseAllNamespacesQueues.mockReturnValue([
      { namespaceId: 'ns-azure', totalActive: 0, totalDlq: 1, totalScheduled: 0, totalQueues: 1, totalTopics: 0, totalSubscriptions: 0, isLoading: false, isError: false },
      { namespaceId: 'ns-aws', totalActive: 0, totalDlq: 3, totalScheduled: 0, totalQueues: 2, totalTopics: 0, totalSubscriptions: 0, isLoading: false, isError: false },
    ]);
    mockUseDlqOverview.mockReturnValue({ data: undefined });

    renderPage('/messages-overview?tab=deadletter');

    expect(screen.getByText('4')).toBeInTheDocument(); // 1 + 3, the live fallback
  });
});
