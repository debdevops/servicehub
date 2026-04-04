import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MessagesPage } from '@/pages/MessagesPage';

// Mock hooks
vi.mock('@/hooks/useMessages', () => ({
  useMessages: vi.fn(),
}));
vi.mock('@/hooks/useInsights', () => ({
  useClientSideInsights: vi.fn(),
  useInsightsSummary: vi.fn(),
}));
vi.mock('@/hooks/useQueues', () => ({
  useQueues: vi.fn(),
}));
vi.mock('@/hooks/useSubscriptions', () => ({
  useSubscriptions: vi.fn(),
}));
vi.mock('@/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));

// Mock heavy components
vi.mock('@/components/messages', () => ({
  MessageList: ({ messages, onQueueTabChange, activeCounts }: any) => (
    <div data-testid="message-list">
      <span>{messages.length} messages</span>
      <button onClick={() => onQueueTabChange('active')}>Active ({activeCounts.active})</button>
      <button onClick={() => onQueueTabChange('deadletter')}>Dead-Letter ({activeCounts.deadletter})</button>
    </div>
  ),
  MessageDetailPanel: () => <div data-testid="message-detail-panel" />,
}));
vi.mock('@/components/messages/MessageListSkeleton', () => ({
  MessageListSkeleton: () => <div data-testid="message-list-skeleton" />,
}));
vi.mock('@/components/ai', () => ({
  AIFindingsDropdown: () => <div data-testid="ai-findings-dropdown" />,
}));
vi.mock('@/components/fab', () => ({
  MessageFAB: () => <div data-testid="message-fab" />,
}));
vi.mock('react-hot-toast', () => ({
  default: { success: vi.fn(), error: vi.fn() },
}));

import { useMessages } from '@/hooks/useMessages';
import { useClientSideInsights, useInsightsSummary } from '@/hooks/useInsights';
import { useQueues } from '@/hooks/useQueues';
import { useSubscriptions } from '@/hooks/useSubscriptions';
import { useNamespaces } from '@/hooks/useNamespaces';

const mockUseMessages = useMessages as ReturnType<typeof vi.fn>;
const mockUseClientSideInsights = useClientSideInsights as ReturnType<typeof vi.fn>;
const mockUseInsightsSummary = useInsightsSummary as ReturnType<typeof vi.fn>;
const mockUseQueues = useQueues as ReturnType<typeof vi.fn>;
const mockUseSubscriptions = useSubscriptions as ReturnType<typeof vi.fn>;
const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;

const mockNamespaces = [
  { id: 'ns1', name: 'my-namespace', displayName: 'My Namespace', isActive: true },
];

const mockMessagesData = {
  items: [
    {
      messageId: 'msg-1',
      sequenceNumber: 1,
      enqueuedTime: new Date().toISOString(),
      body: '{"eventType":"OrderCreated","orderId":"123"}',
      contentType: 'application/json',
      deliveryCount: 1,
      applicationProperties: {},
    },
    {
      messageId: 'msg-2',
      sequenceNumber: 2,
      enqueuedTime: new Date().toISOString(),
      body: '{"eventType":"PaymentProcessed"}',
      contentType: 'application/json',
      deliveryCount: 3,
      applicationProperties: {},
    },
  ],
  totalCount: 2,
};

function createWrapper(initialPath = '/messages?namespace=ns1&queue=test-queue') {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: React.ReactNode }) => (
    <MemoryRouter initialEntries={[initialPath]}>
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    </MemoryRouter>
  );
}

beforeEach(() => {
  mockUseNamespaces.mockReturnValue({ data: mockNamespaces });
  mockUseMessages.mockReturnValue({
    data: mockMessagesData,
    isLoading: false,
    error: null,
    refetch: vi.fn(),
    isFetching: false,
    dataUpdatedAt: Date.now(),
  });
  mockUseClientSideInsights.mockReturnValue({ data: [] });
  mockUseInsightsSummary.mockReturnValue({ data: { activeCount: 0 } });
  mockUseQueues.mockReturnValue({
    data: [{ name: 'test-queue', activeMessageCount: 10, deadLetterMessageCount: 2 }],
    refetch: vi.fn(),
  });
  mockUseSubscriptions.mockReturnValue({ data: [], refetch: vi.fn() });
});

describe('MessagesPage', () => {
  it('renders MessageList when messages are loaded', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessagesPage /></Wrapper>);
    expect(screen.getByTestId('message-list')).toBeInTheDocument();
  });

  it('renders message count in list', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessagesPage /></Wrapper>);
    expect(screen.getByText('2 messages')).toBeInTheDocument();
  });

  it('shows loading skeleton during loading', () => {
    mockUseMessages.mockReturnValue({
      data: undefined, isLoading: true, error: null, refetch: vi.fn(), isFetching: false, dataUpdatedAt: 0,
    });
    const Wrapper = createWrapper();
    render(<Wrapper><MessagesPage /></Wrapper>);
    expect(screen.getByTestId('message-list-skeleton')).toBeInTheDocument();
    expect(screen.getByText('Loading messages...')).toBeInTheDocument();
  });

  it('shows error state when loading fails', () => {
    mockUseMessages.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: new Error('Connection refused'),
      refetch: vi.fn(),
      isFetching: false,
      dataUpdatedAt: 0,
    });
    const Wrapper = createWrapper();
    render(<Wrapper><MessagesPage /></Wrapper>);
    expect(screen.getByText('Failed to load messages')).toBeInTheDocument();
    expect(screen.getByText('Connection refused')).toBeInTheDocument();
  });

  it('shows empty state when no namespace selected', () => {
    const Wrapper = createWrapper('/messages');
    render(<Wrapper><MessagesPage /></Wrapper>);
    expect(screen.getByText('No entity selected')).toBeInTheDocument();
  });

  it('renders search input', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessagesPage /></Wrapper>);
    expect(screen.getByPlaceholderText(/Search messages/)).toBeInTheDocument();
  });

  it('renders Filter button', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessagesPage /></Wrapper>);
    expect(screen.getByText('Filter')).toBeInTheDocument();
  });

  it('renders AI Findings button', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessagesPage /></Wrapper>);
    expect(screen.getByText(/AI Findings/)).toBeInTheDocument();
  });

  it('shows AI findings count', () => {
    mockUseClientSideInsights.mockReturnValue({
      data: [{ id: 'i1', evidence: { affectedMessageIds: ['msg-1'], exampleMessageIds: [] } }],
    });
    const Wrapper = createWrapper();
    render(<Wrapper><MessagesPage /></Wrapper>);
    expect(screen.getByText(/AI Findings: 1/)).toBeInTheDocument();
  });

  it('renders queue tabs (Active / Dead-Letter)', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessagesPage /></Wrapper>);
    expect(screen.getByText(/Active/)).toBeInTheDocument();
    expect(screen.getByText(/Dead.Letter/i)).toBeInTheDocument();
  });

  it('opens filter panel when Filter button is clicked', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessagesPage /></Wrapper>);
    fireEvent.click(screen.getByText('Filter'));
    expect(screen.getByText('All Messages')).toBeInTheDocument();
    expect(screen.getByText('Success')).toBeInTheDocument();
    expect(screen.getByText('Warning')).toBeInTheDocument();
    expect(screen.getByText('Dead-Letter')).toBeInTheDocument();
  });

  it('renders active message count badge in tabs', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessagesPage /></Wrapper>);
    // Active tab should show count from queues data (in the mocked MessageList)
    expect(screen.getByText('Active (10)')).toBeInTheDocument();
  });

  it('renders dead-letter count badge in tabs', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessagesPage /></Wrapper>);
    expect(screen.getByText('Dead-Letter (2)')).toBeInTheDocument();
  });

  it('shows connection error hint for network errors', () => {
    mockUseMessages.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: new Error('Network connection timeout'),
      refetch: vi.fn(),
      isFetching: false,
      dataUpdatedAt: 0,
    });
    const Wrapper = createWrapper();
    render(<Wrapper><MessagesPage /></Wrapper>);
    expect(screen.getByText(/API server/)).toBeInTheDocument();
  });

  it('has Try Again button in error state', () => {
    mockUseMessages.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: new Error('Failure'),
      refetch: vi.fn(),
      isFetching: false,
      dataUpdatedAt: 0,
    });
    const Wrapper = createWrapper();
    render(<Wrapper><MessagesPage /></Wrapper>);
    expect(screen.getByText('Try Again')).toBeInTheDocument();
  });

  it('can clear search input', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessagesPage /></Wrapper>);
    const input = screen.getByPlaceholderText(/Search messages/);
    fireEvent.change(input, { target: { value: 'test' } });
    expect((input as HTMLInputElement).value).toBe('test');
    // Clear button appears
    const clearBtn = document.querySelector('button[class*="absolute right-3"]');
    if (clearBtn) {
      fireEvent.click(clearBtn);
      expect((input as HTMLInputElement).value).toBe('');
    }
  });

  it('renders with topic subscription path', () => {
    const Wrapper = createWrapper('/messages?namespace=ns1&topic=orders&subscription=sub1');
    render(<Wrapper><MessagesPage /></Wrapper>);
    expect(screen.getByTestId('message-list')).toBeInTheDocument();
  });
});
