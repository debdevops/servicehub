import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { DashboardPage } from '@/pages/DashboardPage';

vi.mock('@servicehub/ui-shared/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));

vi.mock('@servicehub/ui-shared/hooks/useQueues', () => ({
  useQueues: vi.fn(),
  useAllNamespacesQueues: vi.fn(),
  useNamespaceStats: vi.fn(),
}));

vi.mock('@servicehub/ui-shared/hooks/useCloudBridge', () => ({
  useProviderCapabilities: vi.fn(),
}));

vi.mock('@servicehub/ui-shared/hooks/useFleet', () => ({
  useFleetOverview: vi.fn(),
}));

vi.mock('@servicehub/ui-shared/hooks/useAudit', () => ({
  useAuditLogs: vi.fn(),
}));

vi.mock('@servicehub/ui-shared/hooks/useDlqOverview', () => ({
  useDlqOverview: vi.fn(),
}));

const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useQueues, useAllNamespacesQueues, useNamespaceStats } from '@servicehub/ui-shared/hooks/useQueues';
import { useProviderCapabilities } from '@servicehub/ui-shared/hooks/useCloudBridge';
import { useFleetOverview } from '@servicehub/ui-shared/hooks/useFleet';
import { useAuditLogs } from '@servicehub/ui-shared/hooks/useAudit';
import { useDlqOverview } from '@servicehub/ui-shared/hooks/useDlqOverview';

const mockUseProviderCapabilities = useProviderCapabilities as ReturnType<typeof vi.fn>;
const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;
const mockUseQueues = useQueues as ReturnType<typeof vi.fn>;
const mockUseAllNamespacesQueues = useAllNamespacesQueues as ReturnType<typeof vi.fn>;
const mockUseNamespaceStats = useNamespaceStats as ReturnType<typeof vi.fn>;
const mockUseFleetOverview = useFleetOverview as ReturnType<typeof vi.fn>;
const mockUseAuditLogs = useAuditLogs as ReturnType<typeof vi.fn>;
const mockUseDlqOverview = useDlqOverview as ReturnType<typeof vi.fn>;

const mockNamespace = {
  id: 'ns1',
  name: 'my-servicebus.servicebus.windows.net',
  displayName: 'My Namespace',
  isActive: true,
  environment: 'dev' as const,
  hasListenPermission: true,
  hasSendPermission: true,
  hasManagePermission: true,
  createdAt: '2024-01-01T00:00:00Z',
};

const mockQueues = [
  {
    name: 'queue-1',
    activeMessageCount: 5,
    deadLetterMessageCount: 2,
    scheduledMessageCount: 1,
    maxSizeInMegabytes: 1024,
    sizeInBytes: 0,
    status: 'Active',
  },
];

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: React.ReactNode }) => (
    <MemoryRouter initialEntries={['/dashboard']}>
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    </MemoryRouter>
  );
}

const capabilitiesMap = {
  Azure: { supportsMessageCounts: true },
  Aws: { supportsMessageCounts: true },
  Gcp: { supportsMessageCounts: false },
};

describe('DashboardPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseProviderCapabilities.mockReturnValue({ data: capabilitiesMap });
    mockUseNamespaces.mockReturnValue({
      data: [mockNamespace],
      isLoading: false,
      isFetching: false,
      refetch: vi.fn(),
    });
    mockUseQueues.mockReturnValue({ data: mockQueues, isLoading: false, isError: false });
    mockUseNamespaceStats.mockReturnValue([{ data: undefined, isLoading: false, isError: false }]);
    mockUseAllNamespacesQueues.mockReturnValue([
      {
        namespaceId: 'ns1',
        queues: mockQueues,
        totalActive: 5,
        totalDlq: 2,
        totalScheduled: 1,
        totalQueues: 1,
        isLoading: false,
        isError: false,
      },
    ]);
    mockUseFleetOverview.mockReturnValue({ data: undefined, isLoading: false });
    mockUseAuditLogs.mockReturnValue({ data: undefined, isLoading: false });
    mockUseDlqOverview.mockReturnValue({ data: undefined, isLoading: false });
  });

  it('renders page title', () => {
    render(<DashboardPage />, { wrapper: createWrapper() });
    expect(screen.getByText('Namespace Overview')).toBeInTheDocument();
  });

  it('shows empty state with Connect button when no namespaces', () => {
    mockUseNamespaces.mockReturnValue({
      data: [],
      isLoading: false,
      isFetching: false,
      refetch: vi.fn(),
    });
    render(<DashboardPage />, { wrapper: createWrapper() });
    expect(screen.getByText('No namespaces connected yet')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /connect a namespace/i })).toBeInTheDocument();
    // Provider-neutral: this empty state is reachable before any provider is chosen, so it must
    // not name Azure's product (Service Bus) — AWS/GCP users see it too.
    expect(screen.queryByText(/service bus/i)).not.toBeInTheDocument();
  });

  it('Connect button navigates to /app/connect', () => {
    mockUseNamespaces.mockReturnValue({
      data: [],
      isLoading: false,
      isFetching: false,
      refetch: vi.fn(),
    });
    render(<DashboardPage />, { wrapper: createWrapper() });
    fireEvent.click(screen.getByRole('button', { name: /connect a namespace/i }));
    expect(mockNavigate).toHaveBeenCalledWith('/connect');
  });

  it('shows full-page error (not the empty state) when namespaces fetch fails with no cached data', () => {
    mockUseNamespaces.mockReturnValue({
      data: undefined,
      isLoading: false,
      isFetching: false,
      isError: true,
      refetch: vi.fn(),
    });
    render(<DashboardPage />, { wrapper: createWrapper() });
    expect(screen.getByText('Unable to reach the API server')).toBeInTheDocument();
    expect(screen.queryByText('No namespaces connected yet')).not.toBeInTheDocument();
  });

  it('shows stale-data warning but keeps dashboard visible when a refresh fails with cached data', async () => {
    mockUseNamespaces.mockReturnValue({
      data: [mockNamespace],
      isLoading: false,
      isFetching: false,
      isError: true,
      refetch: vi.fn(),
    });
    render(<DashboardPage />, { wrapper: createWrapper() });
    expect(screen.getByText(/unable to refresh namespaces/i)).toBeInTheDocument();
    expect(await screen.findByText('My Namespace')).toBeInTheDocument();
  });

  it('renders one NamespaceCard per namespace (displayName visible)', async () => {
    render(<DashboardPage />, { wrapper: createWrapper() });
    expect(await screen.findByText('My Namespace')).toBeInTheDocument();
  });

  it('renders multiple NamespaceCards for multiple namespaces', async () => {
    const ns2 = { ...mockNamespace, id: 'ns2', displayName: 'Second Namespace', name: 'second.servicebus.windows.net' };
    mockUseNamespaces.mockReturnValue({
      data: [mockNamespace, ns2],
      isLoading: false,
      isFetching: false,
      refetch: vi.fn(),
    });
    mockUseAllNamespacesQueues.mockReturnValue([
      { namespaceId: 'ns1', queues: mockQueues, totalActive: 5, totalDlq: 2, totalScheduled: 1, totalQueues: 1, isLoading: false, isError: false },
      { namespaceId: 'ns2', queues: mockQueues, totalActive: 3, totalDlq: 0, totalScheduled: 0, totalQueues: 1, isLoading: false, isError: false },
    ]);
    render(<DashboardPage />, { wrapper: createWrapper() });
    expect(await screen.findByText('My Namespace')).toBeInTheDocument();
    expect(await screen.findByText('Second Namespace')).toBeInTheDocument();
  });

  it('shows loading skeletons while namespaces are loading', () => {
    mockUseNamespaces.mockReturnValue({
      data: undefined,
      isLoading: true,
      isFetching: true,
      refetch: vi.fn(),
    });
    const { container } = render(<DashboardPage />, { wrapper: createWrapper() });
    expect(screen.queryByText('My Namespace')).not.toBeInTheDocument();
    expect(container.querySelector('.animate-pulse')).toBeTruthy();
  });

  it('shows Healthy status when DLQ count is within threshold', async () => {
    render(<DashboardPage />, { wrapper: createWrapper() });
    expect(await screen.findByText('Healthy')).toBeInTheDocument();
  });

  it('shows DLQ spike banner when DLQ count exceeds threshold', async () => {
    mockUseQueues.mockReturnValue({
      data: [{ ...mockQueues[0], deadLetterMessageCount: 15 }],
      isLoading: false,
      isError: false,
    });
    mockUseAllNamespacesQueues.mockReturnValue([
      {
        namespaceId: 'ns1',
        queues: [{ ...mockQueues[0], deadLetterMessageCount: 15 }],
        totalActive: 5,
        totalDlq: 15,
        totalScheduled: 1,
        totalQueues: 1,
        isLoading: false,
        isError: false,
      },
    ]);
    render(<DashboardPage />, { wrapper: createWrapper() });
    expect(await screen.findByText(/DLQ: 15 messages need attention/i)).toBeInTheDocument();
  });

  it('Browse Queues button navigates straight to the first queue', async () => {
    render(<DashboardPage />, { wrapper: createWrapper() });
    fireEvent.click(await screen.findByRole('button', { name: /browse queues/i }));
    expect(mockNavigate).toHaveBeenCalledWith('/messages?namespace=ns1&queue=queue-1&queueType=active');
  });

  it('View DLQ History button navigates to dlq-history page', async () => {
    render(<DashboardPage />, { wrapper: createWrapper() });
    fireEvent.click(await screen.findByRole('button', { name: /view dlq history/i }));
    expect(mockNavigate).toHaveBeenCalledWith('/dlq-history?namespace=ns1');
  });

  it('shows DEV badge for Dev environment', async () => {
    render(<DashboardPage />, { wrapper: createWrapper() });
    expect(await screen.findByText('DEV')).toBeInTheDocument();
  });

  it('shows PROD badge for Prod environment', async () => {
    mockUseNamespaces.mockReturnValue({
      data: [{ ...mockNamespace, environment: 'prod' }],
      isLoading: false,
      isFetching: false,
      refetch: vi.fn(),
    });
    mockUseAllNamespacesQueues.mockReturnValue([
      { namespaceId: 'ns1', queues: mockQueues, totalActive: 5, totalDlq: 2, totalScheduled: 1, totalQueues: 1, isLoading: false, isError: false },
    ]);
    render(<DashboardPage />, { wrapper: createWrapper() });
    expect(await screen.findByText('PROD')).toBeInTheDocument();
  });

  it('shows UAT badge for Uat environment', async () => {
    mockUseNamespaces.mockReturnValue({
      data: [{ ...mockNamespace, environment: 'uat' }],
      isLoading: false,
      isFetching: false,
      refetch: vi.fn(),
    });
    mockUseAllNamespacesQueues.mockReturnValue([
      { namespaceId: 'ns1', queues: mockQueues, totalActive: 5, totalDlq: 2, totalScheduled: 1, totalQueues: 1, isLoading: false, isError: false },
    ]);
    render(<DashboardPage />, { wrapper: createWrapper() });
    expect(await screen.findByText('UAT')).toBeInTheDocument();
  });

  it('shows subscription count from namespace stats', async () => {
    mockUseNamespaceStats.mockReturnValue([
      {
        data: {
          totalQueues: 1,
          totalTopics: 2,
          totalSubscriptions: 4,
          totalActive: 5,
          totalDlq: 2,
          totalScheduled: 1,
        },
        isLoading: false,
        isError: false,
      },
    ]);
    render(<DashboardPage />, { wrapper: createWrapper() });
    expect(await screen.findByText('Subs')).toBeInTheDocument();
    expect(await screen.findByText('4')).toBeInTheDocument();
  });

  it('shows fallback badge when environment is undefined', async () => {
    const { environment: _env, ...nsNoEnv } = mockNamespace;
    mockUseNamespaces.mockReturnValue({
      data: [nsNoEnv],
      isLoading: false,
      isFetching: false,
      refetch: vi.fn(),
    });
    mockUseAllNamespacesQueues.mockReturnValue([
      { namespaceId: 'ns1', queues: mockQueues, totalActive: 5, totalDlq: 2, totalScheduled: 1, totalQueues: 1, isLoading: false, isError: false },
    ]);
    render(<DashboardPage />, { wrapper: createWrapper() });
    expect(await screen.findByText('—')).toBeInTheDocument();
  });

  // Full E2E pass, 2026-09-12: the top "Dead Letter" aggregate summed each namespace's live
  // provider query, so once every namespace's live request had settled (succeeded OR failed),
  // `isLoading` went false and the sum of the failed namespaces' 0s displayed as a confirmed
  // "0" — reproduced live with 30+ unreachable namespaces after a `/api/v1/namespaces/stats/batch`
  // timeout. The persisted ledger (`useDlqOverview`) already knows the real total regardless of
  // live connectivity — same fix already applied to the Dead-Letter tab's KPI tiles.
  it('sources the "Dead Letter" aggregate from the DB-backed overview, not live per-namespace stats', async () => {
    mockUseNamespaces.mockReturnValue({
      data: [mockNamespace, { ...mockNamespace, id: 'ns2' }],
      isLoading: false,
      isFetching: false,
      refetch: vi.fn(),
    });
    // Both namespaces' live stats are 0 — as they would be while a request is still failing.
    mockUseAllNamespacesQueues.mockReturnValue([
      { namespaceId: 'ns1', queues: [], totalActive: 0, totalDlq: 0, totalScheduled: 0, totalQueues: 0, isLoading: false, isError: true },
      { namespaceId: 'ns2', queues: [], totalActive: 0, totalDlq: 0, totalScheduled: 0, totalQueues: 0, isLoading: false, isError: true },
    ]);
    // The persisted DLQ ledger knows the real total regardless of live connectivity.
    mockUseDlqOverview.mockReturnValue({
      data: { totals: { totalDeadLettered: 14210, namespacesWithDlq: 35, namespacesTotal: 36 } },
      isLoading: false,
    });

    render(<DashboardPage />, { wrapper: createWrapper() });

    expect(await screen.findByText('Dead Letter')).toBeInTheDocument();
    // The misleading-zero regression this guards against: the live aggregate for these two
    // namespaces is 0, and that must not be what "Dead Letter" shows.
    expect(screen.getByText('14,210')).toBeInTheDocument();
  });

  it('shows a loading placeholder, not a bare zero, while the DB-backed dead-letter overview is on its first fetch', async () => {
    mockUseNamespaces.mockReturnValue({
      data: [mockNamespace, { ...mockNamespace, id: 'ns2' }],
      isLoading: false,
      isFetching: false,
      refetch: vi.fn(),
    });
    mockUseAllNamespacesQueues.mockReturnValue([
      { namespaceId: 'ns1', queues: [], totalActive: 5, totalDlq: 0, totalScheduled: 0, totalQueues: 1, isLoading: false, isError: false },
      { namespaceId: 'ns2', queues: [], totalActive: 3, totalDlq: 0, totalScheduled: 0, totalQueues: 1, isLoading: false, isError: false },
    ]);
    mockUseDlqOverview.mockReturnValue({ data: undefined, isLoading: true });

    render(<DashboardPage />, { wrapper: createWrapper() });

    const deadLetterLabel = await screen.findByText('Dead Letter');
    const cell = deadLetterLabel.closest('div')!.parentElement as HTMLElement;
    expect(within(cell).getByText('…')).toBeInTheDocument();
  });

  // F3 — "Refresh" used to refetch only the namespace list while resetting the "Live · just now"
  // badge, so every number on the page could stay stale behind a badge claiming it was fresh.
  describe('Refresh refetches every metric it claims to refresh', () => {
    it('refetches namespaces and invalidates the queue, stats, and trend queries', async () => {
      const refetch = vi.fn();
      mockUseNamespaces.mockReturnValue({
        data: [mockNamespace],
        isLoading: false,
        isFetching: false,
        refetch,
      });
      const invalidateSpy = vi.spyOn(QueryClient.prototype, 'invalidateQueries');

      render(<DashboardPage />, { wrapper: createWrapper() });
      fireEvent.click(await screen.findByRole('button', { name: /refresh/i }));

      expect(refetch).toHaveBeenCalledTimes(1);
      const invalidatedKeys = invalidateSpy.mock.calls.map(([arg]) => arg?.queryKey?.[0]);
      expect(invalidatedKeys).toEqual(
        expect.arrayContaining(['queues', 'namespace-stats', 'dlq-trend']),
      );
      invalidateSpy.mockRestore();
    });

    it('invalidates only active queries so background caches are not refetched needlessly', async () => {
      const invalidateSpy = vi.spyOn(QueryClient.prototype, 'invalidateQueries');
      render(<DashboardPage />, { wrapper: createWrapper() });
      fireEvent.click(await screen.findByRole('button', { name: /refresh/i }));

      for (const [arg] of invalidateSpy.mock.calls) {
        expect(arg?.refetchType).toBe('active');
      }
      invalidateSpy.mockRestore();
    });
  });

  // F4 — GCP Pub/Sub has no message-count API, so a "0" there means "unknown", not "empty".
  describe('provider message-count capability', () => {
    const gcpNamespace = { ...mockNamespace, id: 'ns-gcp', cloudProvider: 'gcp' as const };

    function renderWithProviderNamespace(namespace: typeof mockNamespace) {
      mockUseNamespaces.mockReturnValue({
        data: [namespace],
        isLoading: false,
        isFetching: false,
        refetch: vi.fn(),
      });
      mockUseNamespaceStats.mockReturnValue([
        {
          data: {
            totalQueues: 3,
            totalTopics: 2,
            totalSubscriptions: 4,
            totalActive: 0,
            totalDlq: 0,
            totalScheduled: 0,
          },
          isLoading: false,
          isError: false,
        },
      ]);
      return render(<DashboardPage />, { wrapper: createWrapper() });
    }

    // Reads a value from the namespace card's own stat grid. Scoped deliberately: the
    // page-level aggregate bar reuses several of the same labels ("Active", "DLQ").
    function statCellValue(container: HTMLElement, label: string): string | null {
      const grid = container.querySelector('[class*="sm:grid-cols-6"]') as HTMLElement;
      return within(grid).getByText(label).parentElement?.querySelector('p:last-child')?.textContent ?? null;
    }

    it('renders a dash instead of 0 for GCP message counts', async () => {
      const { container } = renderWithProviderNamespace(gcpNamespace);

      expect(await screen.findByText('Sched')).toBeInTheDocument();
      // Active, DLQ, and Sched are all unknown on GCP — none of them may read "0".
      expect(statCellValue(container, 'Active')).toBe('—');
      expect(statCellValue(container, 'DLQ')).toBe('—');
      expect(statCellValue(container, 'Sched')).toBe('—');
    });

    it('still renders real entity counts for GCP, which Pub/Sub does report', async () => {
      const { container } = renderWithProviderNamespace(gcpNamespace);

      expect(await screen.findByText('Queues')).toBeInTheDocument();
      expect(statCellValue(container, 'Queues')).toBe('3');
      expect(statCellValue(container, 'Topics')).toBe('2');
      expect(statCellValue(container, 'Subs')).toBe('4');
    });

    it('does not assert a health grade or "Healthy" status from counts GCP never reports', async () => {
      renderWithProviderNamespace(gcpNamespace);

      expect(await screen.findByText('Message counts unavailable for this provider')).toBeInTheDocument();
      expect(screen.queryByText('Healthy')).not.toBeInTheDocument();
      expect(screen.queryByLabelText(/Health grade/)).not.toBeInTheDocument();
    });

    it('renders numeric counts for a provider that does support them', async () => {
      renderWithProviderNamespace({ ...mockNamespace, cloudProvider: 'azure' } as typeof mockNamespace);

      expect(await screen.findByText('Healthy')).toBeInTheDocument();
      expect(screen.getAllByText('0').length).toBeGreaterThan(0);
    });
  });

  describe('fleet-level panels', () => {
    it('header exposes Fleet Overview and Add Namespace actions', () => {
      render(<DashboardPage />, { wrapper: createWrapper() });
      // "Fleet Overview" appears twice: once in the header, once in Quick Actions.
      expect(screen.getAllByRole('button', { name: /fleet overview/i }).length).toBeGreaterThanOrEqual(2);
      expect(screen.getByRole('button', { name: /add namespace/i })).toBeInTheDocument();
    });

    it('Add Namespace navigates to /connect', () => {
      render(<DashboardPage />, { wrapper: createWrapper() });
      fireEvent.click(screen.getByRole('button', { name: /add namespace/i }));
      expect(mockNavigate).toHaveBeenCalledWith('/connect');
    });

    it('Fleet Overview header button navigates to /fleet', () => {
      render(<DashboardPage />, { wrapper: createWrapper() });
      const [headerButton] = screen.getAllByRole('button', { name: /fleet overview/i });
      fireEvent.click(headerButton);
      expect(mockNavigate).toHaveBeenCalledWith('/fleet');
    });

    it('shows an empty state for Recent Namespace Events when the audit trail has no entries', async () => {
      render(<DashboardPage />, { wrapper: createWrapper() });
      expect(await screen.findByText('Recent Namespace Events')).toBeInTheDocument();
      expect(await screen.findByText('No recent activity recorded yet.')).toBeInTheDocument();
    });

    it('renders recent audit entries when present', async () => {
      mockUseAuditLogs.mockReturnValue({
        data: {
          items: [
            {
              id: 'a1',
              timestamp: new Date().toISOString(),
              userIdentity: 'alex@contoso.com',
              action: 'Namespace.Create',
              outcome: 'Success',
              namespaceId: 'ns1',
              namespaceName: 'My Namespace',
              entityName: null,
              cloudProvider: 'azure',
              environment: 'dev',
              resourceName: null,
              sequenceNumber: null,
              detailsJson: null,
              errorDetails: null,
              clientIp: null,
              userAgent: null,
              correlationId: null,
              httpMethod: null,
              httpPath: null,
            },
          ],
          totalCount: 1,
          page: 1,
          pageSize: 8,
          hasNextPage: false,
          hasPreviousPage: false,
        },
        isLoading: false,
      });
      render(<DashboardPage />, { wrapper: createWrapper() });
      expect(await screen.findByText(/Namespace Create/i)).toBeInTheDocument();
    });

    it('shows Top Failure Categories from the fleet overview rollup', async () => {
      mockUseFleetOverview.mockReturnValue({
        data: {
          generatedAt: new Date().toISOString(),
          windowHours: 24,
          namespaceCount: 1,
          totalActive: 5,
          totalNewInWindow: 0,
          totalResolvedInWindow: 0,
          namespaces: [],
          topCategories: { ProcessingError: 12, Transient: 4 },
          dailyTrend: [],
        },
        isLoading: false,
      });
      render(<DashboardPage />, { wrapper: createWrapper() });
      expect(await screen.findByText('Top Failure Categories')).toBeInTheDocument();
      expect(await screen.findByText('ProcessingError')).toBeInTheDocument();
    });

    it('renders a Provider Distribution panel summarizing the fleet by cloud', async () => {
      render(<DashboardPage />, { wrapper: createWrapper() });
      expect(await screen.findByText('Provider Distribution')).toBeInTheDocument();
    });

    it('surfaces a namespace as "Needs Attention" when Fleet Health marks it critical, even with no local DLQ spike', async () => {
      mockUseFleetOverview.mockReturnValue({
        data: {
          generatedAt: new Date().toISOString(),
          windowHours: 24,
          namespaceCount: 1,
          totalActive: 0,
          totalNewInWindow: 0,
          totalResolvedInWindow: 0,
          namespaces: [
            {
              namespaceId: 'ns1',
              namespaceName: 'My Namespace',
              provider: 'Azure',
              environment: 'Dev',
              activeCount: 60,
              newInWindow: 12,
              resolvedInWindow: 0,
              totalCount: 60,
              topEntity: null,
              topEntityCount: 0,
              topCategory: 'ProcessingError',
              oldestActiveDetectedAt: null,
              severity: 'critical',
              coverage: 'scanned',
              coverageNote: null,
            },
          ],
          topCategories: {},
          dailyTrend: [],
        },
        isLoading: false,
      });
      render(<DashboardPage />, { wrapper: createWrapper() });
      expect(await screen.findByText(/Needs Attention/i)).toBeInTheDocument();
    });
  });
});
