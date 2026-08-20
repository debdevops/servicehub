import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import FleetPage from '@/pages/FleetPage';

vi.mock('@servicehub/ui-shared/hooks/useFleet', () => ({
  useFleetOverview: vi.fn(),
}));

vi.mock('@servicehub/ui-shared/hooks/useHealth', () => ({
  useHealthReport: vi.fn(),
}));

vi.mock('@servicehub/ui-shared/hooks/useQueues', () => ({
  useAllNamespacesQueues: vi.fn(),
}));

vi.mock('@servicehub/ui-shared/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));

const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

import { useFleetOverview } from '@servicehub/ui-shared/hooks/useFleet';
import { useHealthReport } from '@servicehub/ui-shared/hooks/useHealth';
import { useAllNamespacesQueues } from '@servicehub/ui-shared/hooks/useQueues';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
const mockUseFleetOverview = useFleetOverview as ReturnType<typeof vi.fn>;
const mockUseHealthReport = useHealthReport as ReturnType<typeof vi.fn>;
const mockUseAllNamespacesQueues = useAllNamespacesQueues as ReturnType<typeof vi.fn>;
const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;

const sampleOverview = {
  generatedAt: '2026-07-21T06:00:00Z',
  windowHours: 24,
  namespaceCount: 3,
  totalActive: 12,
  totalNewInWindow: 4,
  totalResolvedInWindow: 1,
  topCategories: { PoisonMessage: 8, Transient: 4 },
  dailyTrend: Array.from({ length: 7 }, (_, i) => ({
    date: `2026-07-${15 + i}T00:00:00Z`,
    newMessages: i,
    resolvedMessages: 0,
  })),
  namespaces: [
    {
      namespaceId: 'ns-critical',
      namespaceName: 'orders-prod',
      provider: 'Azure',
      environment: 'Prod',
      activeCount: 60,
      newInWindow: 4,
      resolvedInWindow: 1,
      totalCount: 100,
      topEntity: 'orders',
      topEntityCount: 40,
      topCategory: 'PoisonMessage',
      oldestActiveDetectedAt: '2026-07-20T06:00:00Z',
      severity: 'critical' as const,
      coverage: 'scanned' as const,
      coverageNote: null,
    },
    {
      namespaceId: 'ns-healthy',
      namespaceName: 'reporting-dev',
      provider: 'Aws',
      environment: 'Dev',
      activeCount: 0,
      newInWindow: 0,
      resolvedInWindow: 0,
      totalCount: 0,
      topEntity: null,
      topEntityCount: 0,
      topCategory: null,
      oldestActiveDetectedAt: null,
      severity: 'healthy' as const,
      coverage: 'scanned' as const,
      coverageNote: null,
    },
    {
      namespaceId: 'ns-dev-active',
      namespaceName: 'events-dev',
      provider: 'Gcp',
      environment: 'Dev',
      activeCount: 5,
      newInWindow: 2,
      resolvedInWindow: 0,
      totalCount: 5,
      topEntity: 'events',
      topEntityCount: 5,
      topCategory: 'Transient',
      oldestActiveDetectedAt: '2026-07-21T01:00:00Z',
      severity: 'warning' as const,
      coverage: 'scanned' as const,
      coverageNote: null,
    },
  ],
};

const unmonitoredOverview = {
  ...sampleOverview,
  namespaces: [
    {
      namespaceId: 'ns-unmonitored',
      namespaceName: 'acme-aws-prod',
      provider: 'Aws',
      environment: 'Prod',
      activeCount: 0,
      newInWindow: 0,
      resolvedInWindow: 0,
      totalCount: 0,
      topEntity: null,
      topEntityCount: 0,
      topCategory: null,
      oldestActiveDetectedAt: null,
      severity: 'unknown' as const,
      coverage: 'notMonitored' as const,
      coverageNote: 'AWS SQS has no non-destructive peek.',
    },
  ],
};

function renderPage() {
  return render(
    <MemoryRouter>
      <FleetPage />
    </MemoryRouter>
  );
}

describe('FleetPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseHealthReport.mockReturnValue({ data: undefined });
    mockUseAllNamespacesQueues.mockReturnValue([]);
    mockUseNamespaces.mockReturnValue({ data: [] });
  });

  it('shows a loading state', () => {
    mockUseFleetOverview.mockReturnValue({ data: undefined, isLoading: true, isError: false, refetch: vi.fn(), isFetching: true });
    renderPage();
    expect(screen.getByText(/loading fleet overview/i)).toBeInTheDocument();
  });

  it('renders summary tiles and namespace rows', () => {
    mockUseFleetOverview.mockReturnValue({ data: sampleOverview, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();

    expect(screen.getByText('Fleet Operations')).toBeInTheDocument();
    expect(screen.getByText('12')).toBeInTheDocument(); // total active
    expect(screen.getByText('orders-prod')).toBeInTheDocument();
    expect(screen.getByText('reporting-dev')).toBeInTheDocument();
    expect(screen.getByText('events-dev')).toBeInTheDocument();
    // "at risk" tile = 2 (critical + warning namespaces)
    expect(screen.getByText(/Namespaces at risk/i)).toBeInTheDocument();
  });

  it('navigates to DLQ history when a namespace row is clicked', () => {
    mockUseFleetOverview.mockReturnValue({ data: sampleOverview, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();

    fireEvent.click(screen.getByText('orders-prod'));
    expect(mockNavigate).toHaveBeenCalledWith('/dlq-history?namespace=ns-critical');
  });

  it('shows an error state', () => {
    mockUseFleetOverview.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn(), isFetching: false });
    renderPage();
    expect(screen.getByText(/failed to load the fleet overview/i)).toBeInTheDocument();
  });

  it('renders provider connectivity badges from the health report', () => {
    mockUseFleetOverview.mockReturnValue({ data: sampleOverview, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    mockUseNamespaces.mockReturnValue({
      data: [
        { id: 'ns-azure', name: 'orders-prod', isActive: true, createdAt: '2026-01-01', cloudProvider: 'azure' },
        { id: 'ns-aws', name: 'reporting-dev', isActive: true, createdAt: '2026-01-01', cloudProvider: 'aws' },
      ],
    });
    mockUseHealthReport.mockReturnValue({
      data: {
        entries: {
          servicebus: { status: 'Healthy', description: 'OK' },
          'aws-connectivity': { status: 'Degraded', description: 'Slow' },
        },
      },
    });
    renderPage();

    expect(screen.getByText(/provider connectivity/i)).toBeInTheDocument();
    expect(screen.getByTitle('OK')).toBeInTheDocument();
    expect(screen.getByTitle('Slow')).toBeInTheDocument();
  });

  it('never renders "Healthy" (emerald) styling for a provider with 0 namespaces (flag on, unconnected)', () => {
    mockUseFleetOverview.mockReturnValue({ data: sampleOverview, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    // Both checks report Healthy ("No namespaces configured" is still a Healthy result
    // from AwsHealthCheck) but this operator has 0 namespaces for either — must not
    // read as connected/emerald just because the raw health-check status says Healthy.
    mockUseNamespaces.mockReturnValue({ data: [] });
    mockUseHealthReport.mockReturnValue({
      data: {
        entries: {
          servicebus: { status: 'Healthy', description: 'OK' },
          'aws-connectivity': { status: 'Healthy', description: 'No AWS namespaces configured.' },
        },
      },
    });
    renderPage();

    const strip = screen.getByText(/provider connectivity/i).closest('div') as HTMLElement;
    expect(within(strip).queryAllByText(/Azure|AWS/, { selector: 'span' }).length).toBeGreaterThan(0);
    expect(strip.querySelectorAll('.bg-emerald-50').length).toBe(0);
  });

  it('renders a provider with no health-check entry as unavailable instead of silently omitting it', () => {
    mockUseFleetOverview.mockReturnValue({ data: sampleOverview, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    mockUseNamespaces.mockReturnValue({ data: [] });
    mockUseHealthReport.mockReturnValue({
      data: {
        entries: {
          servicebus: { status: 'Healthy', description: 'OK' },
          // gcp-connectivity absent entirely — flag is off on this server.
        },
      },
    });
    renderPage();

    const strip = screen.getByText(/provider connectivity/i).closest('div') as HTMLElement;
    const gcpPill = within(strip).getByText('GCP').closest('span');
    expect(gcpPill).toHaveAttribute('title', 'Not available on this server');
  });

  it('filters namespace rows by provider', () => {
    mockUseFleetOverview.mockReturnValue({ data: sampleOverview, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: 'AWS' }));

    expect(screen.getByText('reporting-dev')).toBeInTheDocument();
    expect(screen.queryByText('orders-prod')).not.toBeInTheDocument();
    expect(screen.queryByText('events-dev')).not.toBeInTheDocument();
  });

  it('filters namespace rows by search text', () => {
    mockUseFleetOverview.mockReturnValue({ data: sampleOverview, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();

    fireEvent.change(screen.getByPlaceholderText(/search namespaces/i), { target: { value: 'events' } });

    expect(screen.getByText('events-dev')).toBeInTheDocument();
    expect(screen.queryByText('orders-prod')).not.toBeInTheDocument();
  });

  it('shows a bulk-actions deep link for at-risk, non-prod namespaces only', () => {
    mockUseFleetOverview.mockReturnValue({ data: sampleOverview, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();

    const bulkLinks = screen.getAllByTitle('Open bulk replay/purge for this namespace');
    expect(bulkLinks).toHaveLength(1); // only events-dev: non-prod with active DLQ messages

    fireEvent.click(bulkLinks[0]);
    expect(mockNavigate).toHaveBeenCalledWith('/dlq-history?namespace=ns-dev-active&openBulk=true');
  });

  it('shows per-namespace resolved count and top entity', () => {
    mockUseFleetOverview.mockReturnValue({ data: sampleOverview, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();

    expect(screen.getByText('orders (40)')).toBeInTheDocument();
    expect(screen.getByText('events (5)')).toBeInTheDocument();

    const ordersRow = screen.getByText('orders-prod').closest('tr');
    expect(ordersRow).not.toBeNull();
    expect(within(ordersRow as HTMLElement).getByText('1')).toBeInTheDocument(); // resolvedInWindow

    const healthyRow = screen.getByText('reporting-dev').closest('tr');
    expect(healthyRow).not.toBeNull();
    // resolvedInWindow (0) and topEntity (null) both render as '—' for this namespace
    expect(within(healthyRow as HTMLElement).getAllByText('—').length).toBeGreaterThanOrEqual(2);
  });

  it('shows the all-time total DLQ count per namespace', () => {
    mockUseFleetOverview.mockReturnValue({ data: sampleOverview, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();

    const headers = screen.getAllByRole('columnheader');
    const totalColumnIndex = headers.findIndex((h) => h.textContent === 'Total');
    expect(totalColumnIndex).toBeGreaterThan(-1);

    const ordersRow = screen.getByText('orders-prod').closest('tr') as HTMLElement;
    expect(within(ordersRow).getAllByRole('cell')[totalColumnIndex]).toHaveTextContent('100');

    const healthyRow = screen.getByText('reporting-dev').closest('tr') as HTMLElement;
    expect(within(healthyRow).getAllByRole('cell')[totalColumnIndex]).toHaveTextContent('0');
  });

  it('links back to the per-namespace dashboard', () => {
    mockUseFleetOverview.mockReturnValue({ data: sampleOverview, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();

    expect(screen.getByRole('link', { name: /per-namespace details/i })).toHaveAttribute('href', '/dashboard');
  });

  it('never shows a Healthy dot for an unmonitored namespace with zero known dead-letters', () => {
    mockUseFleetOverview.mockReturnValue({ data: unmonitoredOverview, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();

    const row = screen.getByText('acme-aws-prod').closest('tr') as HTMLElement;
    expect(within(row).queryByTitle('Healthy')).not.toBeInTheDocument();
    expect(within(row).getByTitle('AWS SQS has no non-destructive peek.')).toBeInTheDocument();
  });
});
