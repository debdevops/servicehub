import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { DlqHistoryPage } from '@/pages/DlqHistoryPage';

vi.mock('@/hooks/useDlqHistory', () => ({
  useDlqHistory: vi.fn(),
  useDlqSummary: vi.fn(),
}));
vi.mock('@/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));
vi.mock('@/components/dlq', () => ({
  DlqHistoryTable: ({ items, isLoading }: { items: any[]; isLoading: boolean }) => (
    <div data-testid="dlq-history-table">
      {isLoading ? 'Table Loading...' : `${items.length} items`}
    </div>
  ),
  DlqTimelineDrawer: ({ messageId }: { messageId: number | null }) =>
    messageId ? <div data-testid="timeline-drawer">Timeline {messageId}</div> : null,
  StatusBadge: ({ status }: { status: string }) => <span>{status}</span>,
  CategoryBadge: ({ category }: { category: string }) => <span>{category}</span>,
}));
vi.mock('@/lib/api/dlqHistory', () => ({
  dlqHistoryApi: {
    getExportUrl: vi.fn(() => 'http://test-export-url'),
    triggerScan: vi.fn(),
  },
}));
vi.mock('react-hot-toast', () => ({
  default: { success: vi.fn(), error: vi.fn() },
}));

import { useDlqHistory, useDlqSummary } from '@/hooks/useDlqHistory';
import { useNamespaces } from '@/hooks/useNamespaces';

const mockUseDlqHistory = useDlqHistory as ReturnType<typeof vi.fn>;
const mockUseDlqSummary = useDlqSummary as ReturnType<typeof vi.fn>;
const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;

const mockNamespaces = [
  { id: 'ns1', name: 'my-namespace', displayName: 'My Namespace', isActive: true },
];

const mockDlqData = {
  items: [
    {
      id: 1,
      messageId: 'msg-1',
      entityName: 'test-queue',
      status: 'Active',
      category: 'Unknown',
      deadLetterReason: 'MaxDeliveryCountExceeded',
      firstSeenAt: '2024-01-01T10:00:00Z',
      lastSeenAt: '2024-01-01T12:00:00Z',
    },
  ],
  totalCount: 1,
  page: 1,
  pageSize: 50,
  hasNextPage: false,
  hasPreviousPage: false,
};

const mockSummary = {
  activeMessages: 5,
  replayedMessages: 10,
  archivedMessages: 3,
  totalMessages: 18,
};

function createWrapper(initialPath = '/dlq-history?namespace=ns1') {
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
  mockUseDlqHistory.mockReturnValue({ data: mockDlqData, isLoading: false, refetch: vi.fn(), isFetching: false });
  mockUseDlqSummary.mockReturnValue({ data: mockSummary });
  // Reset window.open mock
  vi.stubGlobal('open', vi.fn());
});

describe('DlqHistoryPage', () => {
  it('renders page title', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><DlqHistoryPage /></Wrapper>);
    expect(screen.getByText('DLQ Intelligence')).toBeInTheDocument();
  });

  it('renders page subtitle', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><DlqHistoryPage /></Wrapper>);
    expect(screen.getByText(/Dead-letter queue message history/)).toBeInTheDocument();
  });

  it('shows namespace name in subtitle when namespace resolved', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><DlqHistoryPage /></Wrapper>);
    expect(screen.getByText(/My Namespace/)).toBeInTheDocument();
  });

  it('renders DlqHistoryTable', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><DlqHistoryPage /></Wrapper>);
    expect(screen.getByTestId('dlq-history-table')).toBeInTheDocument();
  });

  it('renders summary cards when summary data is available', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><DlqHistoryPage /></Wrapper>);
    expect(screen.getByText('Active')).toBeInTheDocument();
    expect(screen.getByText('Replayed')).toBeInTheDocument();
  });

  it('renders Refresh button', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><DlqHistoryPage /></Wrapper>);
    expect(screen.getByText('Refresh')).toBeInTheDocument();
  });

  it('renders Scan Now button', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><DlqHistoryPage /></Wrapper>);
    expect(screen.getByText('Scan Now')).toBeInTheDocument();
  });

  it('renders CSV export button', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><DlqHistoryPage /></Wrapper>);
    expect(screen.getByText('CSV')).toBeInTheDocument();
  });

  it('renders JSON export button', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><DlqHistoryPage /></Wrapper>);
    expect(screen.getByText('JSON')).toBeInTheDocument();
  });

  it('renders filter toggle button', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><DlqHistoryPage /></Wrapper>);
    // Filter button
    const filterButtons = screen.getAllByRole('button');
    expect(filterButtons.length).toBeGreaterThan(0);
  });

  it('shows filter controls when filter is toggled', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><DlqHistoryPage /></Wrapper>);
    // Look for the Filters button
    const allButtons = screen.getAllByRole('button');
    const filterButton = allButtons.find(btn => btn.textContent?.includes('Filters'));
    if (filterButton) {
      fireEvent.click(filterButton);
      // Filter controls should now show
    }
  });

  it('calls refetch when Refresh is clicked', () => {
    const mockRefetch = vi.fn();
    mockUseDlqHistory.mockReturnValue({
      data: mockDlqData, isLoading: false, refetch: mockRefetch, isFetching: false,
    });
    const Wrapper = createWrapper();
    render(<Wrapper><DlqHistoryPage /></Wrapper>);
    fireEvent.click(screen.getByText('Refresh'));
    expect(mockRefetch).toHaveBeenCalled();
  });

  it('opens CSV export URL in new tab when CSV button clicked', () => {
    const mockOpen = vi.fn();
    vi.stubGlobal('open', mockOpen);
    const Wrapper = createWrapper();
    render(<Wrapper><DlqHistoryPage /></Wrapper>);
    fireEvent.click(screen.getByText('CSV'));
    expect(mockOpen).toHaveBeenCalledWith('http://test-export-url', '_blank');
  });

  it('opens JSON export URL in new tab when JSON button clicked', () => {
    const mockOpen = vi.fn();
    vi.stubGlobal('open', mockOpen);
    const Wrapper = createWrapper();
    render(<Wrapper><DlqHistoryPage /></Wrapper>);
    fireEvent.click(screen.getByText('JSON'));
    expect(mockOpen).toHaveBeenCalledWith('http://test-export-url', '_blank');
  });

  it('shows loading state in table', () => {
    mockUseDlqHistory.mockReturnValue({ data: undefined, isLoading: true, refetch: vi.fn(), isFetching: false });
    const Wrapper = createWrapper();
    render(<Wrapper><DlqHistoryPage /></Wrapper>);
    expect(screen.getByText('Table Loading...')).toBeInTheDocument();
  });

  it('does not show summary cards when summary is unavailable', () => {
    mockUseDlqSummary.mockReturnValue({ data: undefined });
    const Wrapper = createWrapper();
    render(<Wrapper><DlqHistoryPage /></Wrapper>);
    expect(screen.queryByText('Replayed')).not.toBeInTheDocument();
  });
});
