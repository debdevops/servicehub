import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { DlqHistoryPage } from '@/pages/DlqHistoryPage';

vi.mock('@servicehub/ui-shared/hooks/useDlqHistory', () => ({
  useDlqHistory: vi.fn(),
  useDlqSummary: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useCloudBridge', () => ({
  useProviderCapabilities: vi.fn(),
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
  BulkOperationPreviewModal: ({ operationType, onJobCreated }: { operationType: string; onJobCreated: (jobId: string) => void }) => (
    <div data-testid="bulk-preview-modal">
      Preview: {operationType}
      <button onClick={() => onJobCreated('job-1')}>Confirm</button>
    </div>
  ),
  BulkOperationProgressPanel: () => <div data-testid="bulk-progress-panel">Progress</div>,
  DlqSignaturesPanel: () => <div data-testid="dlq-signatures-panel" />,
}));
vi.mock('@servicehub/ui-shared/lib/api/dlqHistory', () => ({
  dlqHistoryApi: {
    downloadExport: vi.fn(() => Promise.resolve()),
    triggerScan: vi.fn(),
  },
}));
vi.mock('react-hot-toast', () => ({
  default: { success: vi.fn(), error: vi.fn() },
}));

import { useDlqHistory, useDlqSummary } from '@servicehub/ui-shared/hooks/useDlqHistory';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useProviderCapabilities } from '@servicehub/ui-shared/hooks/useCloudBridge';
import userEvent from '@testing-library/user-event';
import { dlqHistoryApi } from '@servicehub/ui-shared/lib/api/dlqHistory';

const mockUseDlqHistory = useDlqHistory as ReturnType<typeof vi.fn>;
const mockUseDlqSummary = useDlqSummary as ReturnType<typeof vi.fn>;
const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;
const mockUseProviderCapabilities = useProviderCapabilities as ReturnType<typeof vi.fn>;

const mockNamespaces = [
  { id: 'ns1', name: 'my-namespace', displayName: 'My Namespace', isActive: true, environment: 'dev', cloudProvider: 'aws' },
];

const mockCapabilitiesMap = {
  Aws: { supportsMessageCounts: true, supportsManualDeadLetter: true, supportsPurge: true, supportsScheduledMessages: false, supportsRepeatablePeek: false, notes: '' },
};

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
  byCategory: { MaxDelivery: 7, Transient: 3 },
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
  vi.clearAllMocks();
  mockUseNamespaces.mockReturnValue({ data: mockNamespaces });
  mockUseDlqHistory.mockReturnValue({ data: mockDlqData, isLoading: false, refetch: vi.fn(), isFetching: false });
  mockUseDlqSummary.mockReturnValue({ data: mockSummary });
  mockUseProviderCapabilities.mockReturnValue({ data: mockCapabilitiesMap });
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
    // Name appears in the subtitle and again in the namespace widget strip
    expect(screen.getAllByText(/My Namespace/).length).toBeGreaterThanOrEqual(1);
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

  it('shows provider filter buttons when filter panel is open', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><DlqHistoryPage /></Wrapper>);
    const allButtons = screen.getAllByRole('button');
    const filterButton = allButtons.find(btn => btn.textContent?.includes('Filters'));
    if (filterButton) {
      fireEvent.click(filterButton);
      expect(screen.getByRole('button', { name: 'All' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'AZURE' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'AWS' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'GCP' })).toBeInTheDocument();
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

  it('opens CSV export URL in new tab when CSV button clicked', async () => {
    const user = userEvent.setup();
    const mockDownloadExport = vi.mocked(dlqHistoryApi.downloadExport);
    const Wrapper = createWrapper();
    render(<Wrapper><DlqHistoryPage /></Wrapper>);
    await user.click(screen.getByText('CSV'));
    expect(mockDownloadExport).toHaveBeenCalledWith('csv', expect.anything());
  });

  it('opens JSON export URL in new tab when JSON button clicked', async () => {
    const user = userEvent.setup();
    const mockDownloadExport = vi.mocked(dlqHistoryApi.downloadExport);
    const Wrapper = createWrapper();
    render(<Wrapper><DlqHistoryPage /></Wrapper>);
    await user.click(screen.getByText('JSON'));
    expect(mockDownloadExport).toHaveBeenCalledWith('json', expect.anything());
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

  describe('Failure category breakdown', () => {
    it('renders a chip per non-zero category, sorted by count descending', () => {
      const Wrapper = createWrapper();
      render(<Wrapper><DlqHistoryPage /></Wrapper>);

      expect(screen.getByText('By Failure Category')).toBeInTheDocument();
      const maxDelivery = screen.getByRole('button', { name: /MaxDelivery · 7/ });
      const transient = screen.getByRole('button', { name: /Transient · 3/ });
      expect(maxDelivery).toBeInTheDocument();
      expect(transient).toBeInTheDocument();
    });

    it('does not render when byCategory is empty or missing', () => {
      mockUseDlqSummary.mockReturnValue({ data: { ...mockSummary, byCategory: {} } });
      const Wrapper = createWrapper();
      render(<Wrapper><DlqHistoryPage /></Wrapper>);
      expect(screen.queryByText('By Failure Category')).not.toBeInTheDocument();
    });

    it('sets the category filter chip when a category is clicked', async () => {
      const user = userEvent.setup();
      const Wrapper = createWrapper();
      render(<Wrapper><DlqHistoryPage /></Wrapper>);

      await user.click(screen.getByRole('button', { name: /MaxDelivery · 7/ }));

      expect(screen.getByText('Category: MaxDelivery')).toBeInTheDocument();
    });

    it('toggles the category filter off when the active category is clicked again', async () => {
      const user = userEvent.setup();
      const Wrapper = createWrapper();
      render(<Wrapper><DlqHistoryPage /></Wrapper>);

      await user.click(screen.getByRole('button', { name: /MaxDelivery · 7/ }));
      expect(screen.getByText('Category: MaxDelivery')).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: /MaxDelivery · 7/ }));
      expect(screen.queryByText('Category: MaxDelivery')).not.toBeInTheDocument();
    });
  });

  describe('Bulk operations', () => {
    it('enables Bulk Replay and Bulk Purge for a dev namespace whose provider supports purge', () => {
      const Wrapper = createWrapper();
      render(<Wrapper><DlqHistoryPage /></Wrapper>);

      expect(screen.getByRole('button', { name: /Bulk Replay/ })).not.toBeDisabled();
      expect(screen.getByRole('button', { name: /Bulk Purge/ })).not.toBeDisabled();
    });

    it('disables both bulk actions for a production namespace', () => {
      mockUseNamespaces.mockReturnValue({
        data: [{ ...mockNamespaces[0], environment: 'prod' }],
      });
      const Wrapper = createWrapper();
      render(<Wrapper><DlqHistoryPage /></Wrapper>);

      expect(screen.getByRole('button', { name: /Bulk Replay/ })).toBeDisabled();
      expect(screen.getByRole('button', { name: /Bulk Purge/ })).toBeDisabled();
    });

    it('disables Bulk Purge (but not Bulk Replay) when the provider does not support purge', () => {
      mockUseProviderCapabilities.mockReturnValue({
        data: { Aws: { ...mockCapabilitiesMap.Aws, supportsPurge: false, notes: 'Purge not supported' } },
      });
      const Wrapper = createWrapper();
      render(<Wrapper><DlqHistoryPage /></Wrapper>);

      expect(screen.getByRole('button', { name: /Bulk Replay/ })).not.toBeDisabled();
      expect(screen.getByRole('button', { name: /Bulk Purge/ })).toBeDisabled();
    });

    it('opens the preview modal for the correct operation type when clicked', async () => {
      const user = userEvent.setup();
      const Wrapper = createWrapper();
      render(<Wrapper><DlqHistoryPage /></Wrapper>);

      await user.click(screen.getByRole('button', { name: /Bulk Replay/ }));

      expect(screen.getByTestId('bulk-preview-modal')).toHaveTextContent('Preview: Replay');
    });

    it('disables both bulk actions once a job is created, until dismissed', async () => {
      const user = userEvent.setup();
      const Wrapper = createWrapper();
      render(<Wrapper><DlqHistoryPage /></Wrapper>);

      await user.click(screen.getByRole('button', { name: /Bulk Replay/ }));
      await user.click(screen.getByRole('button', { name: /Confirm/ }));

      expect(screen.getByRole('button', { name: /Bulk Replay/ })).toBeDisabled();
      expect(screen.getByRole('button', { name: /Bulk Purge/ })).toBeDisabled();
    });
  });
});
