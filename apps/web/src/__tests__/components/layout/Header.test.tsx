import { vi, describe, it, expect, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Header } from '@/components/layout/Header';

// Mock useNamespaces so Header renders without needing a real API
vi.mock('@/hooks/useNamespaces', () => ({
  useNamespaces: () => ({ data: undefined, isLoading: false }),
  useNamespace: () => ({ data: undefined, isLoading: false }),
  useCreateNamespace: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useDeleteNamespace: () => ({ mutateAsync: vi.fn(), isPending: false }),
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

describe('Header', () => {
  beforeEach(() => vi.clearAllMocks());

  it('renders the ServiceHub brand', () => {
    renderHeader();
    expect(screen.getByText('Service')).toBeInTheDocument();
    expect(screen.getByText('Hub')).toBeInTheDocument();
  });

  it('shows "No namespace selected" when no namespace in URL', () => {
    renderHeader();
    expect(screen.getByText('No namespace selected')).toBeInTheDocument();
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
