import { render, screen } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { CloudBridgePage } from '@/pages/CloudBridgePage';

vi.mock('@/hooks/useCloudBridge', () => ({
  useProviderStatus: vi.fn(),
  useCloudEntities: vi.fn(),
  useVisibilityStatus: vi.fn(),
}));

vi.mock('@/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));

// CloudBridgePage rolls up per-provider DLQ counts via the same namespace-stats
// endpoint the Header/Quick Access already warm — mock it to an empty response.
vi.mock('@/lib/api/client', () => ({
  apiClient: {
    get: vi.fn().mockResolvedValue({ data: { totalDlq: 0 } }),
  },
}));

import { useProviderStatus } from '@/hooks/useCloudBridge';
import { useNamespaces } from '@/hooks/useNamespaces';

const mockUseProviderStatus = useProviderStatus as ReturnType<typeof vi.fn>;
const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <CloudBridgePage />
      </QueryClientProvider>
    </MemoryRouter>
  );
}

describe('CloudBridgePage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseNamespaces.mockReturnValue({ data: [], isLoading: false });
  });

  it('renders page heading', () => {
    mockUseProviderStatus.mockReturnValue({ data: undefined, isLoading: true });
    renderPage();
    expect(screen.getByRole('heading', { level: 1, name: /cloud bridge/i })).toBeInTheDocument();
  });

  it('shows Provider Status section', () => {
    mockUseProviderStatus.mockReturnValue({ data: undefined, isLoading: true });
    renderPage();
    expect(screen.getByText(/provider status/i)).toBeInTheDocument();
  });

  it('shows loading spinner while status is loading', () => {
    mockUseProviderStatus.mockReturnValue({ data: undefined, isLoading: true });
    renderPage();
    expect(screen.getByText(/checking providers/i)).toBeInTheDocument();
  });

  it('shows Disabled badges when all providers are disabled', () => {
    mockUseProviderStatus.mockReturnValue({
      data: { Aws: false, Gcp: false },
      isLoading: false,
    });
    renderPage();
    const disabledBadges = screen.getAllByText(/disabled/i);
    expect(disabledBadges.length).toBeGreaterThanOrEqual(2);
  });

  it('shows Active badges when providers are enabled', () => {
    mockUseProviderStatus.mockReturnValue({
      data: { Aws: true, Gcp: true },
      isLoading: false,
    });
    renderPage();
    const activeBadges = screen.getAllByText(/active/i);
    expect(activeBadges.length).toBeGreaterThanOrEqual(2);
  });

  it('shows no-providers warning when all disabled', () => {
    mockUseProviderStatus.mockReturnValue({
      data: { Aws: false, Gcp: false },
      isLoading: false,
    });
    renderPage();
    expect(screen.getByText(/no cloud providers are currently enabled/i)).toBeInTheDocument();
  });

  it('shows namespace selector when providers are enabled', () => {
    mockUseProviderStatus.mockReturnValue({
      data: { Aws: true, Gcp: false },
      isLoading: false,
    });
    renderPage();
    expect(screen.getByLabelText(/namespace/i)).toBeInTheDocument();
  });

  it('shows "select a namespace" prompt when no namespace selected', () => {
    mockUseProviderStatus.mockReturnValue({
      data: { Aws: true, Gcp: false },
      isLoading: false,
    });
    renderPage();
    expect(screen.getByText(/select a namespace above/i)).toBeInTheDocument();
  });

  it('populates namespace options from hook data', () => {
    mockUseProviderStatus.mockReturnValue({
      data: { Aws: true, Gcp: false },
      isLoading: false,
    });
    mockUseNamespaces.mockReturnValue({
      data: [
        { id: 'ns-1', name: 'prod-bus', displayName: 'Production Bus' },
        { id: 'ns-2', name: 'dev-bus', displayName: null },
      ],
      isLoading: false,
    });
    renderPage();
    expect(screen.getByRole('option', { name: 'Production Bus' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'dev-bus' })).toBeInTheDocument();
  });
});
