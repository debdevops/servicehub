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

    expect(screen.getByText('Fleet Overview')).toBeInTheDocument();
    expect(screen.getByText('12')).toBeInTheDocument(); // total active dead-letters
    expect(screen.getByText('orders-prod')).toBeInTheDocument();
    expect(screen.getByText('reporting-dev')).toBeInTheDocument();
    expect(screen.getByText('events-dev')).toBeInTheDocument();
    // "at risk" count (critical + warning) shown alongside the namespace count tile
    expect(screen.getByText(/2 at risk/i)).toBeInTheDocument();
  });

  it('navigates to DLQ history when a row\'s View button is clicked', () => {
    mockUseFleetOverview.mockReturnValue({ data: sampleOverview, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();

    const row = screen.getByText('orders-prod').closest('tr') as HTMLElement;
    fireEvent.click(within(row).getByRole('button', { name: 'View' }));
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

    fireEvent.click(screen.getByRole('button', { name: /^AWS \(1\)$/ }));

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

  it('opens the row menu and offers bulk replay/purge only for at-risk, non-prod namespaces', () => {
    mockUseFleetOverview.mockReturnValue({ data: sampleOverview, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();

    const row = screen.getByText('events-dev').closest('tr') as HTMLElement;
    fireEvent.click(within(row).getByLabelText('More actions for events-dev'));

    const bulkItem = screen.getByRole('menuitem', { name: 'Open bulk replay/purge' });
    expect(bulkItem).not.toBeDisabled();
    fireEvent.click(bulkItem);
    expect(mockNavigate).toHaveBeenCalledWith('/dlq-history?namespace=ns-dev-active&openBulk=true');
  });

  it('disables bulk replay/purge in the row menu for a prod namespace', () => {
    mockUseFleetOverview.mockReturnValue({ data: sampleOverview, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();

    const row = screen.getByText('orders-prod').closest('tr') as HTMLElement;
    fireEvent.click(within(row).getByLabelText('More actions for orders-prod'));

    expect(screen.getByRole('menuitem', { name: 'Open bulk replay/purge' })).toBeDisabled();
  });

  it('expands a namespace row to show new/resolved/total/top-category detail', () => {
    mockUseFleetOverview.mockReturnValue({ data: sampleOverview, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();

    expect(screen.queryByText('orders (40)')).not.toBeInTheDocument();
    const row = screen.getByText('orders-prod').closest('tr') as HTMLElement;
    fireEvent.click(within(row).getByLabelText('Expand details'));

    expect(screen.getByText('orders (40)')).toBeInTheDocument();
    expect(screen.getByText('100')).toBeInTheDocument(); // total (all-time)
  });

  it('shows the namespace health distribution counts', () => {
    mockUseFleetOverview.mockReturnValue({ data: sampleOverview, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();

    expect(screen.getByText('Namespace health')).toBeInTheDocument();
    expect(screen.getAllByText('Critical').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Needs Attention').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Healthy').length).toBeGreaterThan(0);
  });

  it('links back to the namespace overview page', () => {
    mockUseFleetOverview.mockReturnValue({ data: sampleOverview, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();

    expect(screen.getByRole('link', { name: /namespace overview/i })).toHaveAttribute('href', '/dashboard');
  });

  it('never shows a Healthy badge for an unmonitored namespace with zero known dead-letters', () => {
    mockUseFleetOverview.mockReturnValue({ data: unmonitoredOverview, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();

    const row = screen.getByText('acme-aws-prod').closest('tr') as HTMLElement;
    expect(within(row).getByText('Not monitored')).toBeInTheDocument();
    expect(within(row).getByTitle('AWS SQS has no non-destructive peek.')).toBeInTheDocument();
  });

  it('exports selected namespaces to CSV', () => {
    mockUseFleetOverview.mockReturnValue({ data: sampleOverview, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    const createObjectURL = vi.fn().mockReturnValue('blob:mock');
    const revokeObjectURL = vi.fn();
    vi.stubGlobal('URL', { ...URL, createObjectURL, revokeObjectURL });
    renderPage();

    const row = screen.getByText('orders-prod').closest('tr') as HTMLElement;
    fireEvent.click(within(row).getByLabelText('Select orders-prod'));

    expect(screen.getByText('1 selected')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /bulk actions/i }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Export selected (CSV)' }));

    expect(createObjectURL).toHaveBeenCalled();
    vi.unstubAllGlobals();
  });
});
