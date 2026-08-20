import { vi, describe, it, expect, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Header } from '@/components/layout/Header';

// Mock useNamespaces so Header renders without needing a real API
const mockUseNamespaces = vi.fn();
vi.mock('@servicehub/ui-shared/hooks/useNamespaces', () => ({
  useNamespaces: (...args: unknown[]) => mockUseNamespaces(...args),
  useNamespace: () => ({ data: undefined, isLoading: false }),
  useCreateNamespace: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useDeleteNamespace: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

vi.mock('@servicehub/ui-shared/hooks/useQueues', () => ({
  useNamespaceStats: () => [],
}));

function renderHeader(searchParams = '') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[`/?${searchParams}`]}>
        <Header />
      </MemoryRouter>
    </QueryClientProvider>
  );
}

const CONNECTED_NAMESPACE = {
  id: 'ns-1',
  name: 'contoso-prod',
  displayName: 'Contoso Production',
  cloudProvider: 'azure' as const,
  environment: 'prod',
};

describe('Header', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseNamespaces.mockReturnValue({ data: undefined, isLoading: false });
  });

  it('renders the ServiceHub brand', () => {
    renderHeader();
    expect(screen.getByText('Service')).toBeInTheDocument();
    expect(screen.getByText('Hub')).toBeInTheDocument();
  });

  it('shows "No namespace selected" when no namespace in URL', () => {
    renderHeader();
    expect(screen.getByText('No namespace selected')).toBeInTheDocument();
  });

  it('shows provider, namespace name, and environment when connected, and never hides the chip', () => {
    mockUseNamespaces.mockReturnValue({ data: [CONNECTED_NAMESPACE], isLoading: false });
    renderHeader('namespace=ns-1');

    const chip = screen.getByText('Contoso Production').closest('[data-tour="header-connection"]');
    expect(chip).not.toBeNull();
    // The chip must never carry a breakpoint-hide class — that's exactly the Gap 3 regression.
    expect(chip?.className ?? '').not.toMatch(/hidden lg:/);

    expect(screen.getByText('Contoso Production')).toBeInTheDocument();
    expect(screen.getByText('PROD')).toBeInTheDocument();
  });

  it('renders help link', () => {
    renderHeader();
    expect(screen.getByLabelText('Help')).toBeInTheDocument();
  });

  it('renders user menu button', () => {
    renderHeader();
    expect(screen.getByLabelText('User menu')).toBeInTheDocument();
  });

  it('renders the home link', () => {
    renderHeader();
    expect(screen.getByLabelText('ServiceHub Home')).toBeInTheDocument();
  });
});
