import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MessagesOverviewPage } from '@/pages/MessagesOverviewPage';

vi.mock('@/hooks/useNamespaces', () => ({ useNamespaces: vi.fn() }));
vi.mock('@/hooks/useQueues', () => ({ useQueues: vi.fn() }));
vi.mock('@/hooks/useTopics', () => ({ useTopics: vi.fn() }));
vi.mock('@/hooks/useSubscriptions', () => ({ useSubscriptions: vi.fn() }));

const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

import { useNamespaces } from '@/hooks/useNamespaces';
import { useQueues } from '@/hooks/useQueues';
import { useTopics } from '@/hooks/useTopics';
import { useSubscriptions } from '@/hooks/useSubscriptions';

const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;
const mockUseQueues = useQueues as ReturnType<typeof vi.fn>;
const mockUseTopics = useTopics as ReturnType<typeof vi.fn>;
const mockUseSubscriptions = useSubscriptions as ReturnType<typeof vi.fn>;

const azureNs = {
  id: 'ns-azure',
  name: 'sb-dev.servicebus.windows.net',
  displayName: 'Dev SB',
  isActive: true,
  environment: 'dev' as const,
  cloudProvider: 'azure' as const,
  hasListenPermission: true,
  hasSendPermission: true,
  hasManagePermission: true,
  createdAt: '2026-01-01T00:00:00Z',
};

const awsNs = {
  ...azureNs,
  id: 'ns-aws',
  name: 'sqs.ap-south-1.amazonaws.com',
  displayName: 'DevAWS',
  cloudProvider: 'aws' as const,
};

const azureQueues = [
  { name: 'orders', activeMessageCount: 4, deadLetterMessageCount: 1, scheduledMessageCount: 0, sizeInBytes: 0, status: 'Active' },
];

const awsQueues = [
  { name: 'sqs-orders', activeMessageCount: 2, deadLetterMessageCount: 3, scheduledMessageCount: 0, sizeInBytes: 0, status: 'Active', deadLetterTargetQueue: 'sqs-orders-dlq' },
  { name: 'sqs-orders-dlq', activeMessageCount: 3, deadLetterMessageCount: 0, scheduledMessageCount: 0, sizeInBytes: 0, status: 'Active' },
];

function renderPage(initialEntry = '/messages-overview') {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <QueryClientProvider client={queryClient}>
        <MessagesOverviewPage />
      </QueryClientProvider>
    </MemoryRouter>,
  );
}

describe('MessagesOverviewPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseNamespaces.mockReturnValue({ data: [azureNs, awsNs], isLoading: false });
    mockUseQueues.mockImplementation((id: string) => ({
      data: id === 'ns-aws' ? awsQueues : azureQueues,
      isLoading: false,
      isError: false,
    }));
    mockUseTopics.mockReturnValue({ data: [], isLoading: false });
    mockUseSubscriptions.mockReturnValue({ data: [], isLoading: false });
  });

  it('renders a section per namespace across providers', () => {
    renderPage();
    expect(screen.getByText('Dev SB')).toBeInTheDocument();
    expect(screen.getByText('DevAWS')).toBeInTheDocument();
    expect(screen.getByText('Azure')).toBeInTheDocument();
    expect(screen.getByText('AWS')).toBeInTheDocument();
  });

  it('hides AWS companion DLQ queues as standalone widgets', () => {
    renderPage();
    expect(screen.getByText('sqs-orders')).toBeInTheDocument();
    expect(screen.queryByText('sqs-orders-dlq')).not.toBeInTheDocument();
  });

  it('navigates to the queue messages view on widget click (active tab)', () => {
    renderPage();
    fireEvent.click(screen.getByText('orders'));
    expect(mockNavigate).toHaveBeenCalledWith(
      '/messages?namespace=ns-azure&queue=orders&queueType=active',
    );
  });

  it('navigates with deadletter queueType when the dead-letter tab is selected', () => {
    renderPage('/messages-overview?tab=deadletter');
    fireEvent.click(screen.getByText('sqs-orders'));
    expect(mockNavigate).toHaveBeenCalledWith(
      '/messages?namespace=ns-aws&queue=sqs-orders&queueType=deadletter',
    );
  });

  it('shows the dead-letter header when tab=deadletter', () => {
    renderPage('/messages-overview?tab=deadletter');
    expect(screen.getByText('Dead-Letter Overview')).toBeInTheDocument();
  });

  it('shows connect CTA when no namespaces exist', () => {
    mockUseNamespaces.mockReturnValue({ data: [], isLoading: false });
    renderPage();
    expect(screen.getByText('No namespaces connected')).toBeInTheDocument();
  });

  it('sorts queues busiest-first and caps the grid height for many queues', () => {
    const manyQueues = Array.from({ length: 30 }, (_, i) => ({
      name: `q-${i}`,
      activeMessageCount: i,
      deadLetterMessageCount: 0,
      scheduledMessageCount: 0,
      sizeInBytes: 0,
      status: 'Active',
    }));
    mockUseNamespaces.mockReturnValue({ data: [azureNs], isLoading: false });
    mockUseQueues.mockReturnValue({ data: manyQueues, isLoading: false, isError: false });

    const { container } = renderPage();

    // Busiest queue (q-29) renders before the quietest (q-0)
    const labels = Array.from(container.querySelectorAll('section button span')).map(
      (el) => el.textContent,
    );
    expect(labels.indexOf('q-29')).toBeGreaterThan(-1);
    expect(labels.indexOf('q-29')).toBeLessThan(labels.indexOf('q-0'));
    // Grid scrolls inside a capped container instead of growing the page
    expect(container.querySelector('.max-h-56.overflow-y-auto')).toBeTruthy();
    expect(screen.getByText('Queues (30)')).toBeInTheDocument();
  });

  it('search filters entities and hides namespaces without matches', () => {
    renderPage();
    fireEvent.change(screen.getByLabelText('Search entities'), { target: { value: 'sqs' } });
    expect(screen.getByText('sqs-orders')).toBeInTheDocument();
    // Azure namespace has no matching entities → its whole section disappears
    expect(screen.queryByText('Dev SB')).not.toBeInTheDocument();
    expect(screen.queryByText('orders')).not.toBeInTheDocument();
  });

  it('sections collapse and expand from the header', () => {
    mockUseNamespaces.mockReturnValue({ data: [azureNs], isLoading: false });
    renderPage();
    expect(screen.getByText('orders')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Dev SB/ }));
    expect(screen.queryByText('orders')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Dev SB/ }));
    expect(screen.getByText('orders')).toBeInTheDocument();
  });
});
