import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ScheduledMessagesPage } from '@/pages/ScheduledMessagesPage';

// ── Mocks ──────────────────────────────────────────────────────────────────────

vi.mock('@/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));
vi.mock('@/hooks/useQueues', () => ({
  useQueues: vi.fn(),
  useAllNamespacesQueues: vi.fn(),
}));
vi.mock('@/hooks/useScheduledMessages', () => ({
  useScheduledMessages: vi.fn(),
  useCancelScheduledMessage: vi.fn(),
}));
vi.mock('@/hooks/useMessages', () => ({
  useSendMessage: vi.fn(),
}));
vi.mock('@/components/CopyButton', () => ({
  CopyButton: ({ text }: { text: string }) => (
    <button data-testid="copy-button" data-copy={text}>Copy</button>
  ),
}));
vi.mock('react-hot-toast', () => ({
  default: { success: vi.fn(), error: vi.fn() },
}));

import { useNamespaces } from '@/hooks/useNamespaces';
import { useQueues } from '@/hooks/useQueues';
import { useScheduledMessages, useCancelScheduledMessage } from '@/hooks/useScheduledMessages';
import { useSendMessage } from '@/hooks/useMessages';

const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;
const mockUseQueues = useQueues as ReturnType<typeof vi.fn>;
const mockUseScheduledMessages = useScheduledMessages as ReturnType<typeof vi.fn>;
const mockUseCancelScheduledMessage = useCancelScheduledMessage as ReturnType<typeof vi.fn>;
const mockUseSendMessage = useSendMessage as ReturnType<typeof vi.fn>;

// ── Fixtures ───────────────────────────────────────────────────────────────────

const mockNamespaces = [
  { id: 'ns-1', name: 'prod-namespace', displayName: 'Prod Namespace', environment: 'Prod' },
  { id: 'ns-2', name: 'dev-namespace', displayName: 'Dev Namespace', environment: 'Dev' },
];

const mockQueues = [
  { name: 'orders-queue', activeMessageCount: 5, scheduledMessageCount: 2, deadLetterMessageCount: 0 },
  { name: 'payments-queue', activeMessageCount: 3, scheduledMessageCount: 0, deadLetterMessageCount: 1 },
];

const futureTime = new Date(Date.now() + 2 * 60 * 60 * 1000).toISOString(); // +2h

const mockMessages = [
  {
    messageId: 'msg-aaa-111',
    sequenceNumber: 1001,
    body: '{"eventType":"OrderCreated","orderId":"42"}',
    scheduledEnqueueTime: futureTime,
    contentType: 'application/json',
  },
  {
    messageId: 'msg-bbb-222',
    sequenceNumber: 1002,
    body: '{"eventType":"PaymentProcessed"}',
    scheduledEnqueueTime: futureTime,
    contentType: 'application/json',
  },
];

// ── Helper ─────────────────────────────────────────────────────────────────────

function renderPage(search = '') {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[`/scheduled${search}`]}>
        <ScheduledMessagesPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe('ScheduledMessagesPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();

    mockUseNamespaces.mockReturnValue({ data: mockNamespaces });
    mockUseQueues.mockReturnValue({ data: mockQueues });
    mockUseScheduledMessages.mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
      isFetching: false,
    });
    mockUseCancelScheduledMessage.mockReturnValue({
      mutateAsync: vi.fn(),
      isPending: false,
    });
    mockUseSendMessage.mockReturnValue({
      mutateAsync: vi.fn(),
      isPending: false,
    });
  });

  // ── Initial / no-selection state ───────────────────────────────────────────

  it('renders header', () => {
    renderPage();
    expect(screen.getByText('Scheduled Messages')).toBeInTheDocument();
  });

  it('renders namespace selector with all namespaces', () => {
    renderPage();
    expect(screen.getByText('Prod Namespace')).toBeInTheDocument();
    expect(screen.getByText('Dev Namespace')).toBeInTheDocument();
  });

  it('shows placeholder when no namespace selected', () => {
    renderPage();
    expect(screen.getByText(/Select a namespace and queue/)).toBeInTheDocument();
  });

  it('shows Refresh button', () => {
    renderPage();
    expect(screen.getByRole('button', { name: /refresh/i })).toBeInTheDocument();
  });

  // ── Namespace/queue selection ──────────────────────────────────────────────

  it('populates queue dropdown when namespace is selected', () => {
    renderPage();
    const nsSelect = screen.getAllByRole('combobox')[0];
    fireEvent.change(nsSelect, { target: { value: 'ns-1' } });
    expect(screen.getByText('orders-queue (2 scheduled)')).toBeInTheDocument();
  });

  it('queue dropdown is disabled when no namespace selected', () => {
    renderPage();
    const queueSelect = screen.getAllByRole('combobox')[1];
    expect(queueSelect).toBeDisabled();
  });

  // ── Loading state ──────────────────────────────────────────────────────────

  it('shows loading spinner when fetching messages', () => {
    mockUseScheduledMessages.mockReturnValue({
      data: undefined,
      isLoading: true,
      isError: false,
      refetch: vi.fn(),
      isFetching: true,
    });
    renderPage('?namespace=ns-1&queue=orders-queue');
    expect(screen.getByText(/Loading scheduled messages/i)).toBeInTheDocument();
  });

  // ── Error state ────────────────────────────────────────────────────────────

  it('shows error state with Try Again button', () => {
    const refetch = vi.fn();
    mockUseScheduledMessages.mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: true,
      refetch,
      isFetching: false,
    });
    renderPage('?namespace=ns-1&queue=orders-queue');
    expect(screen.getByText('Failed to load scheduled messages')).toBeInTheDocument();
    const tryAgainBtn = screen.getByRole('button', { name: /try again/i });
    fireEvent.click(tryAgainBtn);
    expect(refetch).toHaveBeenCalled();
  });

  // ── Empty state ────────────────────────────────────────────────────────────

  it('shows empty state when no scheduled messages', () => {
    mockUseScheduledMessages.mockReturnValue({
      data: { items: [], totalCount: 0 },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
      isFetching: false,
    });
    renderPage('?namespace=ns-1&queue=orders-queue');
    expect(screen.getByText('No scheduled messages')).toBeInTheDocument();
  });

  // ── Message list ───────────────────────────────────────────────────────────

  it('renders table with message rows when data is loaded', () => {
    mockUseScheduledMessages.mockReturnValue({
      data: { items: mockMessages, totalCount: 2 },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
      isFetching: false,
    });
    renderPage('?namespace=ns-1&queue=orders-queue');
    expect(screen.getByText('Message ID')).toBeInTheDocument();
    expect(screen.getByText('Scheduled For')).toBeInTheDocument();
    expect(screen.getByText('Delivers In')).toBeInTheDocument();
    // Two rows
    expect(screen.getAllByRole('row').length).toBeGreaterThan(2);
  });

  it('shows truncated message ID with copy button', () => {
    mockUseScheduledMessages.mockReturnValue({
      data: { items: mockMessages, totalCount: 2 },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
      isFetching: false,
    });
    renderPage('?namespace=ns-1&queue=orders-queue');
    expect(screen.getAllByTestId('copy-button').length).toBeGreaterThan(0);
  });

  it('shows Reschedule and Cancel buttons per row', () => {
    mockUseScheduledMessages.mockReturnValue({
      data: { items: mockMessages, totalCount: 2 },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
      isFetching: false,
    });
    renderPage('?namespace=ns-1&queue=orders-queue');
    expect(screen.getAllByRole('button', { name: /reschedule/i }).length).toBe(2);
    expect(screen.getAllByRole('button', { name: /cancel/i }).length).toBe(2);
  });

  it('shows count badge with total scheduled messages', () => {
    mockUseScheduledMessages.mockReturnValue({
      data: { items: mockMessages, totalCount: 2 },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
      isFetching: false,
    });
    renderPage('?namespace=ns-1&queue=orders-queue');
    expect(screen.getByText('2 messages')).toBeInTheDocument();
  });

  // ── Cancel flow ────────────────────────────────────────────────────────────

  it('opens confirm dialog when Cancel is clicked', async () => {
    mockUseScheduledMessages.mockReturnValue({
      data: { items: [mockMessages[0]], totalCount: 1 },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
      isFetching: false,
    });
    renderPage('?namespace=ns-1&queue=orders-queue');
    const cancelBtn = screen.getByRole('button', { name: /cancel/i });
    fireEvent.click(cancelBtn);
    await waitFor(() =>
      expect(screen.getByText('Cancel Scheduled Message')).toBeInTheDocument(),
    );
  });

  it('closes confirm dialog when Keep It is clicked', async () => {
    mockUseScheduledMessages.mockReturnValue({
      data: { items: [mockMessages[0]], totalCount: 1 },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
      isFetching: false,
    });
    renderPage('?namespace=ns-1&queue=orders-queue');
    fireEvent.click(screen.getByRole('button', { name: /cancel/i }));
    await waitFor(() => screen.getByText('Cancel Scheduled Message'));
    fireEvent.click(screen.getByRole('button', { name: /keep it/i }));
    await waitFor(() =>
      expect(screen.queryByText('Cancel Scheduled Message')).not.toBeInTheDocument(),
    );
  });

  it('calls cancelScheduledMessage mutate when confirm is clicked', async () => {
    const mutateFn = vi.fn().mockResolvedValue(undefined);
    mockUseCancelScheduledMessage.mockReturnValue({ mutateAsync: mutateFn, isPending: false });
    mockUseScheduledMessages.mockReturnValue({
      data: { items: [mockMessages[0]], totalCount: 1 },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
      isFetching: false,
    });
    renderPage('?namespace=ns-1&queue=orders-queue');
    fireEvent.click(screen.getByRole('button', { name: /^cancel$/i }));
    await waitFor(() => screen.getByText('Cancel Scheduled Message'));
    fireEvent.click(screen.getByRole('button', { name: /yes, cancel message/i }));
    await waitFor(() => expect(mutateFn).toHaveBeenCalledWith({
      namespaceId: 'ns-1',
      queueName: 'orders-queue',
      sequenceNumber: 1001,
    }));
  });

  // ── Reschedule flow ────────────────────────────────────────────────────────

  it('opens reschedule modal when Reschedule is clicked', async () => {
    mockUseScheduledMessages.mockReturnValue({
      data: { items: [mockMessages[0]], totalCount: 1 },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
      isFetching: false,
    });
    renderPage('?namespace=ns-1&queue=orders-queue');
    fireEvent.click(screen.getByRole('button', { name: /reschedule/i }));
    await waitFor(() =>
      expect(screen.getByText('Reschedule Message')).toBeInTheDocument(),
    );
  });

  it('closes reschedule modal when Cancel is clicked inside modal', async () => {
    mockUseScheduledMessages.mockReturnValue({
      data: { items: [mockMessages[0]], totalCount: 1 },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
      isFetching: false,
    });
    renderPage('?namespace=ns-1&queue=orders-queue');
    fireEvent.click(screen.getByRole('button', { name: /reschedule/i }));
    await waitFor(() => screen.getByText('Reschedule Message'));
    // click the Cancel button inside the modal (not the row Cancel)
    const dialog = screen.getByRole('dialog', { name: /reschedule message/i });
    const modalCancelBtn = within(dialog).getByRole('button', { name: /^cancel$/i });
    fireEvent.click(modalCancelBtn);
    await waitFor(() =>
      expect(screen.queryByText('Reschedule Message')).not.toBeInTheDocument(),
    );
  });

  it('shows "Confirm Reschedule" button and datetime input in modal', async () => {
    mockUseScheduledMessages.mockReturnValue({
      data: { items: [mockMessages[0]], totalCount: 1 },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
      isFetching: false,
    });
    renderPage('?namespace=ns-1&queue=orders-queue');
    fireEvent.click(screen.getByRole('button', { name: /reschedule/i }));
    await waitFor(() => screen.getByText('Reschedule Message'));
    expect(screen.getByLabelText(/new delivery time/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /confirm reschedule/i })).toBeInTheDocument();
  });

  // ── Refresh button ─────────────────────────────────────────────────────────

  it('calls refetch when Refresh is clicked', () => {
    const refetch = vi.fn();
    mockUseScheduledMessages.mockReturnValue({
      data: { items: mockMessages, totalCount: 2 },
      isLoading: false,
      isError: false,
      refetch,
      isFetching: false,
    });
    renderPage('?namespace=ns-1&queue=orders-queue');
    fireEvent.click(screen.getByRole('button', { name: /refresh/i }));
    expect(refetch).toHaveBeenCalled();
  });

  it('disables Refresh button while fetching', () => {
    mockUseScheduledMessages.mockReturnValue({
      data: { items: mockMessages, totalCount: 2 },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
      isFetching: true,
    });
    renderPage('?namespace=ns-1&queue=orders-queue');
    expect(screen.getByRole('button', { name: /refresh/i })).toBeDisabled();
  });

  // ── Message body display ───────────────────────────────────────────────────

  it('displays message body preview in table', () => {
    mockUseScheduledMessages.mockReturnValue({
      data: { items: mockMessages, totalCount: 2 },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
      isFetching: false,
    });
    renderPage('?namespace=ns-1&queue=orders-queue');
    // body is truncated to 80 chars, just check it's rendered
    expect(screen.getByText(/OrderCreated/)).toBeInTheDocument();
  });
});
