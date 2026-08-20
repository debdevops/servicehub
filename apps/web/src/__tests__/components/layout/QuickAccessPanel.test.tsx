import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { QuickAccessPanel } from '@/components/layout/QuickAccessPanel';

vi.mock('@servicehub/ui-shared/lib/api/client', () => ({
  apiClient: {
    get: vi.fn().mockResolvedValue({ data: [] }),
  },
}));

vi.mock('@servicehub/ui-shared/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));
vi.mock('react-hot-toast', () => ({
  default: { error: vi.fn(), success: vi.fn() },
  toast: vi.fn(),
}));

import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';

const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: React.ReactNode }) => (
    <MemoryRouter initialEntries={['/messages?namespace=ns1&queue=my-queue']}>
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    </MemoryRouter>
  );
}

const mockNamespaces = [
  { id: 'ns1', name: 'my-namespace', displayName: 'My Namespace', isActive: true },
];

beforeEach(() => {
  localStorage.clear();
  mockUseNamespaces.mockReturnValue({ data: mockNamespaces, isLoading: false, refetch: vi.fn() });
});

describe('QuickAccessPanel', () => {
  it('renders the panel title', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><QuickAccessPanel /></Wrapper>);
    expect(screen.getByText('Quick Access')).toBeInTheDocument();
  });

  it('renders navigation shortcuts without needing to expand anything', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><QuickAccessPanel /></Wrapper>);
    expect(screen.getByText('Active Messages')).toBeInTheDocument();
    expect(screen.getByText('Live Tail')).toBeInTheDocument();
    expect(screen.getByText('Dead-Letter')).toBeInTheDocument();
    expect(screen.getByText('Namespace Overview')).toBeInTheDocument();
    expect(screen.getByText('Fleet Health')).toBeInTheDocument();
    expect(screen.getByText('DLQ Intelligence')).toBeInTheDocument();
    expect(screen.getByText('Auto-Replay Rules')).toBeInTheDocument();
    expect(screen.getByText('Scheduled Messages')).toBeInTheDocument();
    expect(screen.getByText('Multi-Cloud Trace')).toBeInTheDocument();
    expect(screen.getByText('Cloud Bridge')).toBeInTheDocument();
    expect(screen.getByText('System Health')).toBeInTheDocument();
    expect(screen.getByText('Audit Trail')).toBeInTheDocument();
    expect(screen.getByText('Security & Privacy')).toBeInTheDocument();
    expect(screen.getByText('Help & Guide')).toBeInTheDocument();
  });

  it('places Live Tail between Active Messages and Dead-Letter', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><QuickAccessPanel /></Wrapper>);
    const labels = ['Active Messages', 'Live Tail', 'Dead-Letter', 'Scheduled Messages', 'Cloud Bridge'];
    for (let i = 0; i < labels.length - 1; i++) {
      const current = screen.getByText(labels[i]);
      const next = screen.getByText(labels[i + 1]);
      // DOCUMENT_POSITION_FOLLOWING (4) means `next` comes after `current` in the DOM.
      expect(current.compareDocumentPosition(next) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    }
  });

  it('groups shortcuts under section labels', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><QuickAccessPanel /></Wrapper>);
    expect(screen.getByText('Overview')).toBeInTheDocument();
    expect(screen.getByText('Browse across clouds')).toBeInTheDocument();
    expect(screen.getByText('Diagnose & automate')).toBeInTheDocument();
    expect(screen.getByText('Platform')).toBeInTheDocument();
    expect(screen.getByText('Support')).toBeInTheDocument();
  });

  it('collapses and re-expands via the header button', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><QuickAccessPanel /></Wrapper>);
    expect(screen.getByText('Active Messages')).toBeInTheDocument();

    fireEvent.click(screen.getByLabelText('Collapse Quick Access'));
    expect(screen.queryByText('Active Messages')).not.toBeInTheDocument();

    fireEvent.click(screen.getByLabelText('Expand Quick Access'));
    expect(screen.getByText('Active Messages')).toBeInTheDocument();
  });

  it('labels "browse across clouds" shortcuts "All Namespaces" (not "All Clouds") on a single-provider installation', () => {
    mockUseNamespaces.mockReturnValue({
      data: [{ id: 'ns1', name: 'my-namespace', isActive: true, cloudProvider: 'aws' }],
      isLoading: false,
      refetch: vi.fn(),
    });
    const Wrapper = createWrapper();
    render(<Wrapper><QuickAccessPanel /></Wrapper>);
    expect(screen.getAllByText('All Namespaces')).toHaveLength(2);
    expect(screen.queryByText('All Clouds')).not.toBeInTheDocument();
    expect(screen.getByText('Multi-Cloud Trace').closest('a')).toHaveAttribute(
      'title',
      'Needs at least two connected providers to trace a cross-cloud hop'
    );
  });

  it('labels "browse across clouds" shortcuts "All Clouds" once ≥2 providers are configured', () => {
    mockUseNamespaces.mockReturnValue({
      data: [
        { id: 'ns1', name: 'azure-ns', isActive: true, cloudProvider: 'azure' },
        { id: 'ns2', name: 'aws-ns', isActive: true, cloudProvider: 'aws' },
      ],
      isLoading: false,
      refetch: vi.fn(),
    });
    const Wrapper = createWrapper();
    render(<Wrapper><QuickAccessPanel /></Wrapper>);
    expect(screen.getAllByText('All Clouds')).toHaveLength(2);
    expect(screen.queryByText('All Namespaces')).not.toBeInTheDocument();
    expect(screen.getByText('Multi-Cloud Trace').closest('a')).not.toHaveAttribute('title');
  });

});
