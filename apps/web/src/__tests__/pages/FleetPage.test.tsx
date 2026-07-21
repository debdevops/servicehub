import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import FleetPage from '@/pages/FleetPage';

vi.mock('@/hooks/useFleet', () => ({
  useFleetOverview: vi.fn(),
}));

const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

import { useFleetOverview } from '@/hooks/useFleet';
const mockUseFleetOverview = useFleetOverview as ReturnType<typeof vi.fn>;

const sampleOverview = {
  generatedAt: '2026-07-21T06:00:00Z',
  windowHours: 24,
  namespaceCount: 2,
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
      severity: 'Critical' as const,
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
      severity: 'Healthy' as const,
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
  beforeEach(() => vi.clearAllMocks());

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
    // "at risk" tile = 1 (only the critical namespace)
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
});
