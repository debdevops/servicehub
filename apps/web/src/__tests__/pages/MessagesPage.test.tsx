import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter, useLocation, useNavigate, useNavigationType } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MessagesPage } from '@/pages/MessagesPage';

// Mock hooks
vi.mock('@servicehub/ui-shared/hooks/useMessages', () => ({
  useMessages: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useInsights', () => ({
  useClientSideInsights: vi.fn(),
  useInsightsSummary: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useQueues', () => ({
  useQueues: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useSubscriptions', () => ({
  useSubscriptions: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useCloudBridge', () => ({
  useProviderCapabilities: vi.fn(),
}));

// Mock heavy components
vi.mock('@/components/messages', () => ({
  MessageList: ({ messages, onQueueTabChange, activeCounts, onSelectMessage, selectedId }: any) => (
    <div data-testid="message-list">
      <span>{messages.length} messages</span>
      <span data-testid="selected-id">{selectedId ?? 'none'}</span>
      <button onClick={() => onSelectMessage('msg-2')}>Select msg-2</button>
      <button onClick={() => onQueueTabChange('active')}>Active ({activeCounts.active})</button>
      <button onClick={() => onQueueTabChange('deadletter')}>Dead-Letter ({activeCounts.deadletter})</button>
      {messages.map((m: any) => (
        <span key={m.id} data-testid={`preview-${m.id}`}>{m.preview}</span>
      ))}
    </div>
  ),
  MessageDetailPanel: () => <div data-testid="message-detail-panel" />,
  LiveTailPanel: () => <div data-testid="live-tail-panel" />,
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

import { useMessages } from '@servicehub/ui-shared/hooks/useMessages';
import { useClientSideInsights, useInsightsSummary } from '@servicehub/ui-shared/hooks/useInsights';
import { useQueues } from '@servicehub/ui-shared/hooks/useQueues';
import { useSubscriptions } from '@servicehub/ui-shared/hooks/useSubscriptions';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useProviderCapabilities } from '@servicehub/ui-shared/hooks/useCloudBridge';

const mockUseMessages = useMessages as ReturnType<typeof vi.fn>;
const mockUseClientSideInsights = useClientSideInsights as ReturnType<typeof vi.fn>;
const mockUseInsightsSummary = useInsightsSummary as ReturnType<typeof vi.fn>;
const mockUseQueues = useQueues as ReturnType<typeof vi.fn>;
const mockUseSubscriptions = useSubscriptions as ReturnType<typeof vi.fn>;
const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;
const mockUseProviderCapabilities = useProviderCapabilities as ReturnType<typeof vi.fn>;

const mockNamespaces = [
  {
    id: 'ns1',
    name: 'my-namespace',
    displayName: 'My Namespace',
    isActive: true,
    cloudProvider: 'azure',
    environment: 'prod',
  },
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

// Exposes the live router URL to assertions, and a button that simulates the
// app's sidebar navigation to another queue while (realistically) carrying the
// existing query params along — including a stale ?message= deep link.
function LocationProbe() {
  const location = useLocation();
  const navigationType = useNavigationType();
  const navigate = useNavigate();
  return (
    <>
      <span data-testid="location-search">{location.search}</span>
      <span data-testid="navigation-type">{navigationType}</span>
      <button onClick={() => navigate('/messages?namespace=ns1&queue=other-queue&message=msg-1')}>
        go-other-queue
      </button>
    </>
  );
}

function createWrapper(initialPath = '/messages?namespace=ns1&queue=test-queue') {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: React.ReactNode }) => (
    <MemoryRouter initialEntries={[initialPath]}>
      <QueryClientProvider client={queryClient}>
        {children}
        <LocationProbe />
      </QueryClientProvider>
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
  mockUseProviderCapabilities.mockReturnValue({ data: undefined });
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

  it('shows an empty preview (not "Body unavailable") for a message with a genuinely empty body', () => {
    // Regresses a mislabel found while testing GCP Pub/Sub, whose peek maps a zero-length
    // Data payload to body: '' (not null) — e.g. a message intentionally published with no
    // body. The preview must not claim the body is "unavailable" (implying a size/throttle
    // error) merely because the empty string is falsy.
    mockUseMessages.mockReturnValue({
      data: {
        items: [{
          messageId: 'msg-empty',
          sequenceNumber: 1,
          enqueuedTime: new Date().toISOString(),
          body: '',
          contentType: 'application/json',
          deliveryCount: 1,
          applicationProperties: {},
        }],
        totalCount: 1,
      },
      isLoading: false, error: null, refetch: vi.fn(), isFetching: false, dataUpdatedAt: Date.now(),
    });
    const Wrapper = createWrapper();
    render(<Wrapper><MessagesPage /></Wrapper>);
    expect(screen.getByTestId('preview-msg-empty')).toHaveTextContent('');
    expect(screen.queryByText(/Body unavailable/)).not.toBeInTheDocument();
  });

  it('shows "Body unavailable" only when the body is genuinely absent (null)', () => {
    mockUseMessages.mockReturnValue({
      data: {
        items: [{
          messageId: 'msg-null-body',
          sequenceNumber: 1,
          enqueuedTime: new Date().toISOString(),
          body: null,
          contentType: 'application/json',
          deliveryCount: 1,
          applicationProperties: {},
        }],
        totalCount: 1,
      },
      isLoading: false, error: null, refetch: vi.fn(), isFetching: false, dataUpdatedAt: Date.now(),
    });
    const Wrapper = createWrapper();
    render(<Wrapper><MessagesPage /></Wrapper>);
    expect(screen.getByTestId('preview-msg-null-body')).toHaveTextContent('Body unavailable - may exceed size limit or API throttled');
  });

  it('has exactly one <h1> naming the current entity, with namespace and environment visible nearby', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessagesPage /></Wrapper>);

    const headings = screen.getAllByRole('heading', { level: 1 });
    expect(headings).toHaveLength(1);
    expect(headings[0]).toHaveTextContent('test-queue');

    // Namespace and environment must be readable without expanding any panel.
    expect(screen.getByText('My Namespace')).toBeInTheDocument();
    expect(screen.getByText('PROD')).toBeInTheDocument();
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

  it('warns that search/filters only apply to loaded messages when more messages are available', () => {
    mockUseMessages.mockReturnValue({
      data: { ...mockMessagesData, totalCount: 100 },
      isLoading: false,
      error: null,
      refetch: vi.fn(),
      isFetching: false,
      dataUpdatedAt: Date.now(),
    });
    const Wrapper = createWrapper();
    render(<Wrapper><MessagesPage /></Wrapper>);
    expect(screen.getByText('Load More')).toBeInTheDocument();
    expect(screen.getByTitle(/Search and filters only apply to the messages currently loaded/)).toBeInTheDocument();
  });

  it('does not show the more-messages warning when the full queue is already loaded', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessagesPage /></Wrapper>);
    expect(screen.queryByText('Load More')).not.toBeInTheDocument();
  });

  it('renders with topic subscription path', () => {
    const Wrapper = createWrapper('/messages?namespace=ns1&topic=orders&subscription=sub1');
    render(<Wrapper><MessagesPage /></Wrapper>);
    expect(screen.getByTestId('message-list')).toBeInTheDocument();
  });

  describe('Auto-refresh default (SupportsRepeatablePeek)', () => {
    // Release-gate regression: GCP's SupportsRepeatablePeek flipped to false (every
    // pull-then-release still counts as a delivery attempt toward MaxDeliveryAttempts) but the
    // auto-refresh default guard only ever checked cloudProvider === 'aws', so a GCP subscription
    // kept auto-polling every 30s by default — silently dead-lettering a watched message purely
    // from being displayed. The guard must key off the capability, not a hardcoded provider name.
    it('disables auto-refresh by default for a GCP namespace (SupportsRepeatablePeek: false)', () => {
      mockUseNamespaces.mockReturnValue({
        data: [{ id: 'ns-gcp', name: 'gcp-ns', isActive: true, cloudProvider: 'gcp', environment: 'dev' }],
      });
      mockUseProviderCapabilities.mockReturnValue({
        data: { Gcp: { supportsMessageCounts: false, supportsManualDeadLetter: false, supportsPurge: true, supportsScheduledMessages: false, supportsRepeatablePeek: false, notes: '' } },
      });
      const Wrapper = createWrapper('/messages?namespace=ns-gcp&topic=orders-topic&subscription=orders-sub');
      render(<Wrapper><MessagesPage /></Wrapper>);
      expect(screen.getByLabelText('Resume auto-refresh')).toBeInTheDocument();
    });

    it('still disables auto-refresh by default for an AWS namespace (SupportsRepeatablePeek: false)', () => {
      mockUseNamespaces.mockReturnValue({
        data: [{ id: 'ns-aws', name: 'aws-ns', isActive: true, cloudProvider: 'aws', environment: 'dev' }],
      });
      mockUseProviderCapabilities.mockReturnValue({
        data: { Aws: { supportsMessageCounts: true, supportsManualDeadLetter: true, supportsPurge: true, supportsScheduledMessages: false, supportsRepeatablePeek: false, notes: '' } },
      });
      const Wrapper = createWrapper('/messages?namespace=ns-aws&queue=test-queue');
      render(<Wrapper><MessagesPage /></Wrapper>);
      expect(screen.getByLabelText('Resume auto-refresh')).toBeInTheDocument();
    });

    it('leaves auto-refresh enabled by default for a provider that supports repeatable peek (Azure)', () => {
      mockUseProviderCapabilities.mockReturnValue({
        data: { Azure: { supportsMessageCounts: true, supportsManualDeadLetter: true, supportsPurge: false, supportsScheduledMessages: true, supportsRepeatablePeek: true, notes: '' } },
      });
      const Wrapper = createWrapper();
      render(<Wrapper><MessagesPage /></Wrapper>);
      expect(screen.getByLabelText('Pause auto-refresh')).toBeInTheDocument();
    });
  });

  describe('Message deep links', () => {
    it('restores selection from the ?message= URL parameter on load', () => {
      const Wrapper = createWrapper('/messages?namespace=ns1&queue=test-queue&message=msg-1');
      render(<Wrapper><MessagesPage /></Wrapper>);
      expect(screen.getByTestId('selected-id')).toHaveTextContent('msg-1');
    });

    it('shows no selection when the URL has no message parameter', () => {
      const Wrapper = createWrapper();
      render(<Wrapper><MessagesPage /></Wrapper>);
      expect(screen.getByTestId('selected-id')).toHaveTextContent('none');
    });

    it('updates selection when a message is chosen', () => {
      const Wrapper = createWrapper();
      render(<Wrapper><MessagesPage /></Wrapper>);
      fireEvent.click(screen.getByText('Select msg-2'));
      expect(screen.getByTestId('selected-id')).toHaveTextContent('msg-2');
    });

    it('mirrors the selection into the URL so the link is shareable', () => {
      const Wrapper = createWrapper();
      render(<Wrapper><MessagesPage /></Wrapper>);
      expect(screen.getByTestId('location-search')).not.toHaveTextContent('message=');
      fireEvent.click(screen.getByText('Select msg-2'));
      expect(screen.getByTestId('location-search')).toHaveTextContent('message=msg-2');
    });

    it('clears the selection when switching queue tabs', () => {
      const Wrapper = createWrapper('/messages?namespace=ns1&queue=test-queue&message=msg-1');
      render(<Wrapper><MessagesPage /></Wrapper>);
      expect(screen.getByTestId('selected-id')).toHaveTextContent('msg-1');
      fireEvent.click(screen.getByText(/Dead-Letter \(/));
      expect(screen.getByTestId('selected-id')).toHaveTextContent('none');
    });

    it('removes the message param from the URL when switching queue tabs', () => {
      const Wrapper = createWrapper('/messages?namespace=ns1&queue=test-queue&message=msg-1');
      render(<Wrapper><MessagesPage /></Wrapper>);
      expect(screen.getByTestId('location-search')).toHaveTextContent('message=msg-1');
      fireEvent.click(screen.getByText(/Dead-Letter \(/));
      expect(screen.getByTestId('location-search')).not.toHaveTextContent('message=');
    });

    it('pushes a history entry when a message is selected, so Back can return to the queue view', () => {
      const Wrapper = createWrapper();
      render(<Wrapper><MessagesPage /></Wrapper>);
      fireEvent.click(screen.getByText('Select msg-2'));
      expect(screen.getByTestId('navigation-type')).toHaveTextContent('PUSH');
    });

    it('replaces (does not push) when the selection is cleared by a tab switch', () => {
      const Wrapper = createWrapper('/messages?namespace=ns1&queue=test-queue&message=msg-1');
      render(<Wrapper><MessagesPage /></Wrapper>);
      fireEvent.click(screen.getByText(/Dead-Letter \(/));
      expect(screen.getByTestId('navigation-type')).toHaveTextContent('REPLACE');
    });

    it('clears selection and the stale message param when navigating to another entity', () => {
      const Wrapper = createWrapper('/messages?namespace=ns1&queue=test-queue&message=msg-1');
      render(<Wrapper><MessagesPage /></Wrapper>);
      expect(screen.getByTestId('selected-id')).toHaveTextContent('msg-1');
      // Sidebar-style navigation to a different queue carrying the old param along
      fireEvent.click(screen.getByText('go-other-queue'));
      expect(screen.getByTestId('selected-id')).toHaveTextContent('none');
      expect(screen.getByTestId('location-search')).toHaveTextContent('queue=other-queue');
      expect(screen.getByTestId('location-search')).not.toHaveTextContent('message=');
    });
  });
});
