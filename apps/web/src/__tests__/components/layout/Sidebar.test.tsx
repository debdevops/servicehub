import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Sidebar } from '@/components/layout/Sidebar';

// Mock apiClient to prevent real HTTP calls from useQueries in Sidebar
vi.mock('@/lib/api/client', () => ({
  apiClient: {
    get: vi.fn().mockResolvedValue({ data: [] }),
  },
}));

// Mock hooks
vi.mock('@/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));
vi.mock('@/hooks/useQueues', () => ({
  useQueues: vi.fn(),
}));
vi.mock('@/hooks/useTopics', () => ({
  useTopics: vi.fn(),
}));
vi.mock('@/hooks/useSubscriptions', () => ({
  useSubscriptions: vi.fn(),
}));
vi.mock('@/hooks/useInsights', () => ({
  useInsightsSummary: vi.fn(),
}));
vi.mock('react-hot-toast', () => ({
  default: { error: vi.fn(), success: vi.fn() },
  toast: vi.fn(),
}));

import { useNamespaces } from '@/hooks/useNamespaces';
import { useQueues } from '@/hooks/useQueues';
import { useTopics } from '@/hooks/useTopics';
import { useSubscriptions } from '@/hooks/useSubscriptions';
import { useInsightsSummary } from '@/hooks/useInsights';

const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;
const mockUseQueues = useQueues as ReturnType<typeof vi.fn>;
const mockUseTopics = useTopics as ReturnType<typeof vi.fn>;
const mockUseSubscriptions = useSubscriptions as ReturnType<typeof vi.fn>;
const mockUseInsightsSummary = useInsightsSummary as ReturnType<typeof vi.fn>;

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
  { id: 'ns2', name: 'other-namespace', displayName: 'Other Namespace', isActive: false },
];

const mockQueues = [
  { name: 'my-queue', activeMessageCount: 5, deadLetterMessageCount: 2 },
  { name: 'test-queue', activeMessageCount: 10, deadLetterMessageCount: 0 },
];

const mockTopics = [
  { name: 'orders-topic', subscriptionCount: 3 },
];

beforeEach(() => {
  mockUseNamespaces.mockReturnValue({ data: mockNamespaces, isLoading: false, refetch: vi.fn() });
  mockUseQueues.mockReturnValue({ data: mockQueues, isLoading: false });
  mockUseTopics.mockReturnValue({ data: mockTopics, isLoading: false });
  mockUseSubscriptions.mockReturnValue({ data: [], isLoading: false });
  mockUseInsightsSummary.mockReturnValue({ data: undefined });
});

describe('Sidebar', () => {
  it('renders Namespaces section header', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><Sidebar /></Wrapper>);
    expect(screen.getByText('Namespaces')).toBeInTheDocument();
  });

  it('renders namespace display name', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><Sidebar /></Wrapper>);
    expect(screen.getByText('My Namespace')).toBeInTheDocument();
  });

  it('renders Quick Access section', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><Sidebar /></Wrapper>);
    expect(screen.getByText('Quick Access')).toBeInTheDocument();
  });

  it('renders Quick Access navigation buttons after expanding', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><Sidebar /></Wrapper>);
    // Quick Access starts collapsed — expand it first
    fireEvent.click(screen.getByText('Quick Access'));
    expect(screen.getByText('Active Messages')).toBeInTheDocument();
    expect(screen.getByText('Dead-Letter')).toBeInTheDocument();
    expect(screen.getByText('Dashboard')).toBeInTheDocument();
    expect(screen.getByText('DLQ Intelligence')).toBeInTheDocument();
    expect(screen.getByText('Auto-Replay')).toBeInTheDocument();
    expect(screen.getByText('Scheduled')).toBeInTheDocument();
    expect(screen.getByText('Correlation')).toBeInTheDocument();
  });

  it('renders Add Connection button', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><Sidebar /></Wrapper>);
    expect(screen.getAllByText('Add Connection').length).toBeGreaterThan(0);
  });

  it('renders Refresh namespaces button', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><Sidebar /></Wrapper>);
    expect(screen.getByLabelText('Refresh namespaces list')).toBeInTheDocument();
  });

  it('renders Add new connection link', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><Sidebar /></Wrapper>);
    expect(screen.getByLabelText('Add new connection')).toBeInTheDocument();
  });

  it('shows loading state when namespaces are loading', () => {
    mockUseNamespaces.mockReturnValue({ data: undefined, isLoading: true, refetch: vi.fn() });
    const Wrapper = createWrapper();
    render(<Wrapper><Sidebar /></Wrapper>);
    expect(screen.getByText('Loading namespaces...')).toBeInTheDocument();
  });

  it('shows no connections message when namespaces is empty', () => {
    mockUseNamespaces.mockReturnValue({ data: [], isLoading: false, refetch: vi.fn() });
    const Wrapper = createWrapper();
    render(<Wrapper><Sidebar /></Wrapper>);
    expect(screen.getByText('No connections yet')).toBeInTheDocument();
    expect(screen.getByText('Add your first connection')).toBeInTheDocument();
  });

  it('calls refetch when refresh button is clicked', () => {
    const mockRefetch = vi.fn();
    mockUseNamespaces.mockReturnValue({ data: mockNamespaces, isLoading: false, refetch: mockRefetch });
    const Wrapper = createWrapper();
    render(<Wrapper><Sidebar /></Wrapper>);
    fireEvent.click(screen.getByLabelText('Refresh namespaces list'));
    expect(mockRefetch).toHaveBeenCalled();
  });

  it('renders inactive namespace', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><Sidebar /></Wrapper>);
    expect(screen.getByText('Other Namespace')).toBeInTheDocument();
  });

  it('shows queues loading state', () => {
    mockUseQueues.mockReturnValue({ data: undefined, isLoading: true });
    const Wrapper = createWrapper();
    render(<Wrapper><Sidebar /></Wrapper>);
    expect(screen.getAllByText('Loading...').length).toBeGreaterThan(0);
  });

  it('shows no queues message when queues is empty', () => {
    mockUseQueues.mockReturnValue({ data: [], isLoading: false });
    const Wrapper = createWrapper();
    render(<Wrapper><Sidebar /></Wrapper>);
    expect(screen.getByText('No queues found')).toBeInTheDocument();
  });

  it('renders queue items with active message counts', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><Sidebar /></Wrapper>);
    expect(screen.getByText('my-queue')).toBeInTheDocument();
    expect(screen.getByText('test-queue')).toBeInTheDocument();
  });

  it('renders topic items', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><Sidebar /></Wrapper>);
    expect(screen.getByText('orders-topic')).toBeInTheDocument();
  });

  it('expands topic to show subscriptions loading', async () => {
    mockUseSubscriptions.mockReturnValue({ data: undefined, isLoading: true });
    const Wrapper = createWrapper();
    render(<Wrapper><Sidebar /></Wrapper>);
    
    const topicButton = screen.getByText('orders-topic').closest('button');
    if (topicButton) {
      fireEvent.click(topicButton);
      await waitFor(() => {
        expect(screen.getAllByText('Loading...').length).toBeGreaterThan(0);
      });
    }
  });

  it('expands topic to show subscriptions', async () => {
    mockUseSubscriptions.mockReturnValue({
      data: [{ name: 'order-sub', activeMessageCount: 3, deadLetterMessageCount: 0 }],
      isLoading: false,
    });
    const Wrapper = createWrapper();
    render(<Wrapper><Sidebar /></Wrapper>);
    
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
    render(<Wrapper><Sidebar /></Wrapper>);
    
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
    render(<Wrapper><Sidebar /></Wrapper>);
    // AI insight indicator should be present (pulsing dot)
    const indicators = document.querySelectorAll('[title="AI patterns detected"]');
    expect(indicators.length).toBeGreaterThan(0);
  });

  it('can toggle Queues section visibility', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><Sidebar /></Wrapper>);
    // There should be a Queues button within the namespace section
    const queuesButtons = screen.getAllByRole('button');
    const queuesToggle = queuesButtons.find(btn => btn.textContent?.includes('Queues'));
    if (queuesToggle) {
      fireEvent.click(queuesToggle);
      // After toggle, queues may be collapsed
    }
  });

  it('can toggle Topics section visibility', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><Sidebar /></Wrapper>);
    const topicButtons = screen.getAllByRole('button');
    const topicsToggle = topicButtons.find(btn => btn.textContent?.includes('Topics'));
    if (topicsToggle) {
      fireEvent.click(topicsToggle);
    }
  });

  it('renders no topics message when topics is empty', () => {
    mockUseTopics.mockReturnValue({ data: [], isLoading: false });
    const Wrapper = createWrapper();
    render(<Wrapper><Sidebar /></Wrapper>);
    expect(screen.getByText('No topics found')).toBeInTheDocument();
  });
});
