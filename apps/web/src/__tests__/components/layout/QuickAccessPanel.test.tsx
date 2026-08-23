import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
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

function createWrapper(initialEntries: string[] = ['/messages?namespace=ns1&queue=my-queue']) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: React.ReactNode }) => (
    <MemoryRouter initialEntries={initialEntries}>
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

  // ── F1 regression: Live Tail / DLQ Intelligence must preserve the operator's
  // current namespace, not silently substitute whichever namespace happens to be
  // `isActive` (a connection-enabled flag every namespace has, unrelated to selection).
  describe('namespace context preservation (F1)', () => {
    const multiCloudNamespaces = [
      { id: 'azure-dev', name: 'azure-dev', isActive: true, cloudProvider: 'azure' },
      { id: 'aws-dev', name: 'aws-dev', isActive: true, cloudProvider: 'aws' },
      { id: 'gcp-dev', name: 'gcp-dev', isActive: true, cloudProvider: 'gcp' },
    ];

    beforeEach(() => {
      mockUseNamespaces.mockReturnValue({ data: multiCloudNamespaces, isLoading: false, refetch: vi.fn() });
    });

    it('keeps Live Tail and DLQ Intelligence on AWS when the operator is currently on the AWS namespace', () => {
      const Wrapper = createWrapper(['/messages?namespace=aws-dev&queue=orders']);
      render(<Wrapper><QuickAccessPanel /></Wrapper>);
      expect(screen.getByText('Live Tail').closest('a')).toHaveAttribute('href', expect.stringContaining('namespace=aws-dev'));
      expect(screen.getByText('DLQ Intelligence').closest('a')).toHaveAttribute('href', expect.stringContaining('namespace=aws-dev'));
    });

    it('keeps Live Tail and DLQ Intelligence on GCP when the operator is currently on the GCP namespace', () => {
      const Wrapper = createWrapper(['/messages?namespace=gcp-dev&queue=orders']);
      render(<Wrapper><QuickAccessPanel /></Wrapper>);
      expect(screen.getByText('Live Tail').closest('a')).toHaveAttribute('href', expect.stringContaining('namespace=gcp-dev'));
      expect(screen.getByText('DLQ Intelligence').closest('a')).toHaveAttribute('href', expect.stringContaining('namespace=gcp-dev'));
    });

    it('keeps Live Tail and DLQ Intelligence on Azure when the operator is currently on the Azure namespace', () => {
      const Wrapper = createWrapper(['/messages?namespace=azure-dev&queue=payments']);
      render(<Wrapper><QuickAccessPanel /></Wrapper>);
      expect(screen.getByText('Live Tail').closest('a')).toHaveAttribute('href', expect.stringContaining('namespace=azure-dev'));
      expect(screen.getByText('DLQ Intelligence').closest('a')).toHaveAttribute('href', expect.stringContaining('namespace=azure-dev'));
    });

    it('follows a namespace switch instead of sticking to whichever namespace was current on first render', () => {
      const Wrapper = createWrapper(['/messages?namespace=aws-dev&queue=orders']);
      const { rerender } = render(<Wrapper><QuickAccessPanel /></Wrapper>);
      expect(screen.getByText('Live Tail').closest('a')).toHaveAttribute('href', expect.stringContaining('namespace=aws-dev'));

      rerender(
        <MemoryRouter initialEntries={['/messages?namespace=gcp-dev&queue=orders']}>
          <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
            <QuickAccessPanel />
          </QueryClientProvider>
        </MemoryRouter>
      );
      expect(screen.getByText('Live Tail').closest('a')).toHaveAttribute('href', expect.stringContaining('namespace=gcp-dev'));
    });

    it('omits the namespace param when the current URL has no namespace selected', () => {
      const Wrapper = createWrapper(['/dashboard']);
      render(<Wrapper><QuickAccessPanel /></Wrapper>);
      expect(screen.getByText('Live Tail').closest('a')).toHaveAttribute('href', expect.not.stringContaining('namespace='));
      expect(screen.getByText('DLQ Intelligence').closest('a')).toHaveAttribute('href', expect.not.stringContaining('namespace='));
    });
  });

  // ── F3 regression: entries that share a pathname (Active Messages / Dead-Letter)
  // or previously used a static className must actually highlight when active.
  describe('active-state highlighting (F3)', () => {
    afterEach(() => {
      window.history.pushState({}, '', '/');
    });

    it('highlights only Active Messages, not Dead-Letter, on the active-messages tab', () => {
      // MemoryRouter tracks its own in-memory location and never touches jsdom's
      // window.location — but the component reads window.location.search directly
      // (same pattern as NamespacesPanel.tsx), so the test URL must be synced too.
      window.history.pushState({}, '', '/messages-overview?tab=active');
      const Wrapper = createWrapper(['/messages-overview?tab=active']);
      render(<Wrapper><QuickAccessPanel /></Wrapper>);
      expect(screen.getByText('Active Messages').closest('a')).toHaveClass('bg-sky-50');
      expect(screen.getByText('Dead-Letter').closest('a')).not.toHaveClass('bg-red-50');
    });

    it('highlights only Dead-Letter, not Active Messages, on the deadletter tab', () => {
      window.history.pushState({}, '', '/messages-overview?tab=deadletter');
      const Wrapper = createWrapper(['/messages-overview?tab=deadletter']);
      render(<Wrapper><QuickAccessPanel /></Wrapper>);
      expect(screen.getByText('Dead-Letter').closest('a')).toHaveClass('bg-red-50');
      expect(screen.getByText('Active Messages').closest('a')).not.toHaveClass('bg-sky-50');
    });

    it('highlights DLQ Intelligence when on /dlq-history', () => {
      const Wrapper = createWrapper(['/dlq-history']);
      render(<Wrapper><QuickAccessPanel /></Wrapper>);
      expect(screen.getByText('DLQ Intelligence').closest('a')).toHaveClass('bg-purple-50');
    });

    it('highlights Auto-Replay Rules when on /rules', () => {
      const Wrapper = createWrapper(['/rules']);
      render(<Wrapper><QuickAccessPanel /></Wrapper>);
      expect(screen.getByText('Auto-Replay Rules').closest('a')).toHaveClass('bg-amber-50');
    });

    it('highlights System Health when on /health', () => {
      const Wrapper = createWrapper(['/health']);
      render(<Wrapper><QuickAccessPanel /></Wrapper>);
      expect(screen.getByText('System Health').closest('a')).toHaveClass('bg-emerald-50');
    });

    it('highlights Help & Guide when on /help', () => {
      const Wrapper = createWrapper(['/help']);
      render(<Wrapper><QuickAccessPanel /></Wrapper>);
      expect(screen.getByText('Help & Guide').closest('a')).toHaveClass('bg-primary-50');
    });

    it('does not regress the already-working entries — Namespace Overview still highlights on /dashboard', () => {
      const Wrapper = createWrapper(['/dashboard']);
      render(<Wrapper><QuickAccessPanel /></Wrapper>);
      expect(screen.getByText('Namespace Overview').closest('a')).toHaveClass('bg-indigo-50');
      expect(screen.getByText('DLQ Intelligence').closest('a')).not.toHaveClass('bg-purple-50');
    });
  });

});
