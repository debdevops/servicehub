import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { NamespacesPanel } from '@/components/layout/NamespacesPanel';
import { DemoModeProvider } from '@servicehub/ui-shared/lib/demo/DemoContext';

vi.mock('@servicehub/ui-shared/lib/api/client', () => ({
  apiClient: {
    get: vi.fn().mockResolvedValue({ data: [] }),
  },
}));

vi.mock('@servicehub/ui-shared/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useQueues', () => ({
  useQueues: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useTopics', () => ({
  useTopics: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useSubscriptions', () => ({
  useSubscriptions: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useInsights', () => ({
  useInsightsSummary: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useCloudBridge', () => ({
  useProviderStatus: vi.fn(),
  useProviderCapabilities: vi.fn(),
}));
vi.mock('react-hot-toast', () => ({
  default: { error: vi.fn(), success: vi.fn() },
  toast: vi.fn(),
}));

import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useQueues } from '@servicehub/ui-shared/hooks/useQueues';
import { useTopics } from '@servicehub/ui-shared/hooks/useTopics';
import { useSubscriptions } from '@servicehub/ui-shared/hooks/useSubscriptions';
import { useInsightsSummary } from '@servicehub/ui-shared/hooks/useInsights';
import { useProviderStatus, useProviderCapabilities } from '@servicehub/ui-shared/hooks/useCloudBridge';

const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;
const mockUseQueues = useQueues as ReturnType<typeof vi.fn>;
const mockUseTopics = useTopics as ReturnType<typeof vi.fn>;
const mockUseSubscriptions = useSubscriptions as ReturnType<typeof vi.fn>;
const mockUseInsightsSummary = useInsightsSummary as ReturnType<typeof vi.fn>;
const mockUseProviderStatus = useProviderStatus as ReturnType<typeof vi.fn>;
const mockUseProviderCapabilities = useProviderCapabilities as ReturnType<typeof vi.fn>;

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
  { id: 'ns1', name: 'my-namespace', displayName: 'My Namespace', isActive: true, cloudProvider: 'azure', environment: 'dev' },
  { id: 'ns2', name: 'other-namespace', displayName: 'Other Namespace', isActive: false, cloudProvider: 'aws', environment: 'prod' },
];

const mockQueues = [
  { name: 'my-queue', activeMessageCount: 5, deadLetterMessageCount: 2 },
  { name: 'test-queue', activeMessageCount: 10, deadLetterMessageCount: 0 },
];

const mockTopics = [
  { name: 'orders-topic', subscriptionCount: 3 },
];

beforeEach(() => {
  localStorage.clear();
  mockUseNamespaces.mockReturnValue({ data: mockNamespaces, isLoading: false, refetch: vi.fn(), isRefetching: false });
  mockUseQueues.mockReturnValue({ data: mockQueues, isLoading: false });
  mockUseTopics.mockReturnValue({ data: mockTopics, isLoading: false });
  mockUseSubscriptions.mockReturnValue({ data: [], isLoading: false });
  mockUseInsightsSummary.mockReturnValue({ data: undefined });
  // Azure + AWS already have namespace cards (mockNamespaces); GCP has none and is
  // enabled — the default fixture exercises the "available-unconfigured" footer row.
  mockUseProviderStatus.mockReturnValue({ data: { Azure: true, Aws: true, Gcp: true } });
  mockUseProviderCapabilities.mockReturnValue({ data: undefined });
});

describe('NamespacesPanel', () => {
  it('renders the panel title', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><NamespacesPanel /></Wrapper>);
    expect(screen.getByText('Namespaces / Connections')).toBeInTheDocument();
  });

  it('renders namespace display names', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><NamespacesPanel /></Wrapper>);
    expect(screen.getByText('My Namespace')).toBeInTheDocument();
    expect(screen.getByText('Other Namespace')).toBeInTheDocument();
  });

  it('renders Add Connection button', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><NamespacesPanel /></Wrapper>);
    expect(screen.getAllByText('Add Connection').length).toBeGreaterThan(0);
  });

  it('renders Refresh namespaces button', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><NamespacesPanel /></Wrapper>);
    expect(screen.getByLabelText('Refresh namespaces list')).toBeInTheDocument();
  });

  it('renders Add new connection link', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><NamespacesPanel /></Wrapper>);
    expect(screen.getByLabelText('Add new connection')).toBeInTheDocument();
  });

  it('shows loading state when namespaces are loading', () => {
    mockUseNamespaces.mockReturnValue({ data: undefined, isLoading: true, refetch: vi.fn(), isRefetching: false });
    const Wrapper = createWrapper();
    render(<Wrapper><NamespacesPanel /></Wrapper>);
    expect(screen.getByText('Loading namespaces...')).toBeInTheDocument();
  });

  it('shows no connections message when namespaces is empty', () => {
    mockUseNamespaces.mockReturnValue({ data: [], isLoading: false, refetch: vi.fn(), isRefetching: false });
    const Wrapper = createWrapper();
    render(<Wrapper><NamespacesPanel /></Wrapper>);
    expect(screen.getByText('No connections yet')).toBeInTheDocument();
    expect(screen.getByText('Add your first connection')).toBeInTheDocument();
  });

  it('calls refetch when refresh button is clicked', () => {
    const mockRefetch = vi.fn();
    mockUseNamespaces.mockReturnValue({ data: mockNamespaces, isLoading: false, refetch: mockRefetch, isRefetching: false });
    const Wrapper = createWrapper();
    render(<Wrapper><NamespacesPanel /></Wrapper>);
    fireEvent.click(screen.getByLabelText('Refresh namespaces list'));
    expect(mockRefetch).toHaveBeenCalled();
  });

  it('shows queues loading state', () => {
    mockUseQueues.mockReturnValue({ data: undefined, isLoading: true });
    const Wrapper = createWrapper();
    render(<Wrapper><NamespacesPanel /></Wrapper>);
    expect(screen.getAllByText('Loading...').length).toBeGreaterThan(0);
  });

  it('shows no queues message when queues is empty', () => {
    mockUseQueues.mockReturnValue({ data: [], isLoading: false });
    const Wrapper = createWrapper();
    render(<Wrapper><NamespacesPanel /></Wrapper>);
    expect(screen.getByText('No queues found')).toBeInTheDocument();
  });

  it('renders queue items with active message counts', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><NamespacesPanel /></Wrapper>);
    expect(screen.getByText('my-queue')).toBeInTheDocument();
    expect(screen.getByText('test-queue')).toBeInTheDocument();
  });

  it('renders topic items', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><NamespacesPanel /></Wrapper>);
    expect(screen.getByText('orders-topic')).toBeInTheDocument();
  });

  it('expands topic to show subscriptions', async () => {
    mockUseSubscriptions.mockReturnValue({
      data: [{ name: 'order-sub', activeMessageCount: 3, deadLetterMessageCount: 0 }],
      isLoading: false,
    });
    const Wrapper = createWrapper();
    render(<Wrapper><NamespacesPanel /></Wrapper>);

    const topicButton = screen.getByText('orders-topic').closest('button');
    if (topicButton) {
      fireEvent.click(topicButton);
      await waitFor(() => {
        expect(screen.getByText('order-sub')).toBeInTheDocument();
      });
    }
  });

  it('shows no subscriptions message on empty list', async () => {
    mockUseSubscriptions.mockReturnValue({ data: [], isLoading: false });
    const Wrapper = createWrapper();
    render(<Wrapper><NamespacesPanel /></Wrapper>);

    const topicButton = screen.getByText('orders-topic').closest('button');
    if (topicButton) {
      fireEvent.click(topicButton);
      await waitFor(() => {
        expect(screen.getByText('No subscriptions')).toBeInTheDocument();
      });
    }
  });

  it('shows AI insight indicator on queue when insights active', () => {
    mockUseInsightsSummary.mockReturnValue({ data: { activeCount: 2 } });
    const Wrapper = createWrapper();
    render(<Wrapper><NamespacesPanel /></Wrapper>);
    const indicators = document.querySelectorAll('[title="AI patterns detected"]');
    expect(indicators.length).toBeGreaterThan(0);
  });

  it('renders no topics message when topics is empty', () => {
    mockUseTopics.mockReturnValue({ data: [], isLoading: false });
    const Wrapper = createWrapper();
    render(<Wrapper><NamespacesPanel /></Wrapper>);
    expect(screen.getByText('No topics found')).toBeInTheDocument();
  });

  it('collapses and re-expands via the header button', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><NamespacesPanel /></Wrapper>);
    expect(screen.getByText('My Namespace')).toBeInTheDocument();

    fireEvent.click(screen.getByLabelText('Collapse Namespaces / Connections'));
    expect(screen.queryByText('My Namespace')).not.toBeInTheDocument();

    fireEvent.click(screen.getByLabelText('Expand Namespaces / Connections'));
    expect(screen.getByText('My Namespace')).toBeInTheDocument();
  });

  it('shows a "Connection issue" pill instead of "Connected" when the last test failed', () => {
    mockUseNamespaces.mockReturnValue({
      data: [{ ...mockNamespaces[0], lastConnectionTestSucceeded: false }, mockNamespaces[1]],
      isLoading: false,
      refetch: vi.fn(),
      isRefetching: false,
    });
    const Wrapper = createWrapper();
    render(<Wrapper><NamespacesPanel /></Wrapper>);
    expect(screen.getByText('Connection issue')).toBeInTheDocument();
    expect(screen.queryByText('Connected')).not.toBeInTheDocument();
  });

  it('still shows "Connected" when the last test succeeded or was never run', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><NamespacesPanel /></Wrapper>);
    expect(screen.getByText('Connected')).toBeInTheDocument();
  });

  it('lists an enabled, zero-namespace provider (GCP) as "Not configured", not silently omitted', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><NamespacesPanel /></Wrapper>);
    expect(screen.getByText('GCP')).toBeInTheDocument();
    expect(screen.getByText('Not configured')).toBeInTheDocument();
    expect(screen.getByText('+ Connect')).toBeInTheDocument();
  });

  it('lists a disabled provider as "Not available on this server", never as empty data', () => {
    mockUseProviderStatus.mockReturnValue({ data: { Azure: true, Aws: true, Gcp: false } });
    const Wrapper = createWrapper();
    render(<Wrapper><NamespacesPanel /></Wrapper>);
    expect(screen.getByText('Not available on this server')).toBeInTheDocument();
    expect(screen.getByText('Ask an operator')).toBeInTheDocument();
  });

  it('does not list "Other providers" for a provider that already has a namespace card', () => {
    // Both Azure and AWS have namespace cards from mockNamespaces; only GCP (0
    // namespaces) should appear in the footer.
    const Wrapper = createWrapper();
    render(<Wrapper><NamespacesPanel /></Wrapper>);
    expect(screen.getByText('Other providers')).toBeInTheDocument();
    expect(screen.getAllByText('GCP')).toHaveLength(1);
  });

  it('does not render the "Other providers" footer in Demo Mode', () => {
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    render(
      <MemoryRouter initialEntries={['/demo/azure/messages']}>
        <QueryClientProvider client={queryClient}>
          <DemoModeProvider cloudProvider="azure">
            <NamespacesPanel />
          </DemoModeProvider>
        </QueryClientProvider>
      </MemoryRouter>
    );
    expect(screen.queryByText('Other providers')).not.toBeInTheDocument();
    expect(screen.queryByText('Not configured')).not.toBeInTheDocument();
  });
});
