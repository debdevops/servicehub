import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AuditPage } from '@/pages/AuditPage';
import { useAuditLogs, useAuditSummary } from '@servicehub/ui-shared/hooks/useAudit';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { auditApi } from '@servicehub/ui-shared/lib/api/audit';

vi.mock('@servicehub/ui-shared/hooks/useAudit', () => ({
  useAuditLogs: vi.fn(),
  useAuditSummary: vi.fn(),
}));

vi.mock('@servicehub/ui-shared/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));

vi.mock('@servicehub/ui-shared/lib/api/audit', () => ({
  auditApi: {
    downloadExport: vi.fn(() => Promise.resolve()),
    getLogs: vi.fn(),
    getSummary: vi.fn(),
  },
}));

vi.mock('react-hot-toast', () => ({
  default: { success: vi.fn(), error: vi.fn() },
}));

const mockUseAuditLogs = useAuditLogs as ReturnType<typeof vi.fn>;
const mockUseAuditSummary = useAuditSummary as ReturnType<typeof vi.fn>;
const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;

const mockNamespaces = [
  { id: 'ns1', name: 'my-namespace', displayName: 'My Namespace', isActive: true },
];

const mockAuditData = {
  items: [
    {
      id: 'audit-1',
      timestamp: '2024-01-01T10:00:00Z',
      userIdentity: 'test@user.com',
      action: 'Messages.Replay',
      outcome: 'Success',
      namespaceId: 'ns1',
      namespaceName: 'my-namespace',
      entityName: 'orders-queue',
      cloudProvider: 'azure',
      environment: 'Prod',
      resourceName: 'msg-1',
      sequenceNumber: 42,
      detailsJson: '{"count": 5}',
      errorDetails: null,
      clientIp: '127.0.0.1',
      userAgent: 'Chrome',
      correlationId: 'corr-1',
      httpMethod: 'POST',
      httpPath: '/api/v1/messages/replay',
    },
  ],
  totalCount: 1,
  page: 1,
  pageSize: 50,
  hasNextPage: false,
  hasPreviousPage: false,
};

const mockAuditDataTwoItems = {
  ...mockAuditData,
  items: [
    mockAuditData.items[0],
    {
      ...mockAuditData.items[0],
      id: 'audit-2',
      userIdentity: 'second@user.com',
      action: 'Namespace.Register',
      entityName: 'second-ns',
      resourceName: 'msg-2',
      correlationId: 'corr-2',
      httpPath: '/api/v2/namespaces/register',
    },
  ],
  totalCount: 2,
};

const mockSummary = {
  totalEvents: 10,
  successCount: 8,
  failureCount: 2,
  partialCount: 0,
  activeUsers: 2,
  successRate: 80.0,
};

function createWrapper(initialPath = '/audit?namespace=ns1') {
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
  mockUseAuditLogs.mockReturnValue({ data: mockAuditData, isLoading: false, refetch: vi.fn(), isFetching: false });
  mockUseAuditSummary.mockReturnValue({ data: mockSummary });
});

describe('AuditPage', () => {
  it('does not filter by namespace when the URL has no namespace param, even though a namespace is isActive', () => {
    // isActive means "not deleted," not "currently selected" — every registered namespace
    // has isActive: true, so defaulting to namespaces.find(isActive) would silently scope
    // this page (whose own subtitle promises "all critical operations") to one arbitrary
    // namespace and hide every other namespace's audit events.
    const Wrapper = createWrapper('/audit');
    render(<Wrapper><AuditPage /></Wrapper>);

    expect(mockUseAuditLogs).toHaveBeenCalled();
    const params = mockUseAuditLogs.mock.calls[0][0];
    expect(params.namespaceId).toBeUndefined();
  });

  it('renders page title and subtitle', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><AuditPage /></Wrapper>);
    expect(screen.getByText('Audit Trail')).toBeInTheDocument();
    expect(screen.getByText('Persistent record of all critical operations and access events')).toBeInTheDocument();
  });

  it('renders summary stat cards', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><AuditPage /></Wrapper>);
    expect(screen.getByText('Total Events')).toBeInTheDocument();
    expect(screen.getByText('Success Rate')).toBeInTheDocument();
    expect(screen.getByText('Failures')).toBeInTheDocument();
    expect(screen.getByText('Active Users')).toBeInTheDocument();
    expect(screen.getByText('80%')).toBeInTheDocument();
  });

  it('renders audit log item in the table', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><AuditPage /></Wrapper>);
    expect(screen.getByText('test@user.com')).toBeInTheDocument();
    expect(screen.getByText('Messages.Replay')).toBeInTheDocument();
    expect(screen.getByText('orders-queue')).toBeInTheDocument();
  });

  it('toggles filters panel when Filters button is clicked', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><AuditPage /></Wrapper>);

    expect(screen.queryByRole('combobox')).toBeNull();

    const filterBtn = screen.getByRole('button', { name: /Filters/i });
    fireEvent.click(filterBtn);

    // Filter controls should be visible
    expect(screen.getByText('All Outcomes')).toBeInTheDocument();
  });

  it('calls export API when download is clicked', async () => {
    const Wrapper = createWrapper();
    render(<Wrapper><AuditPage /></Wrapper>);

    const exportBtn = screen.getByRole('button', { name: /^Export$/ });
    fireEvent.click(exportBtn); // click to open menu

    const csvBtn = screen.getByText('Export as CSV');
    fireEvent.click(csvBtn);

    expect(auditApi.downloadExport).toHaveBeenCalledWith('csv', expect.any(Object));
  });

  it('opens details drawer when clicking on a row', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><AuditPage /></Wrapper>);

    const row = screen.getByText('Messages.Replay');
    fireEvent.click(row);

    // Details drawer title should be present
    expect(screen.getByText('Audit Entry Detail')).toBeInTheDocument();
    expect(screen.getByText('Chrome')).toBeInTheDocument();
    expect(screen.getByText('127.0.0.1')).toBeInTheDocument();
  });

  it('details drawer has dialog semantics and closes on Escape', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><AuditPage /></Wrapper>);

    fireEvent.click(screen.getByText('Messages.Replay'));

    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveAttribute('aria-modal', 'true');
    expect(dialog).toHaveAttribute('aria-labelledby', 'audit-detail-drawer-title');

    fireEvent.keyDown(window, { key: 'Escape' });
    expect(screen.queryByText('Audit Entry Detail')).not.toBeInTheDocument();
  });

  it('defaults to Last 7 Days when the URL has no explicit from/to', () => {
    const Wrapper = createWrapper('/audit');
    render(<Wrapper><AuditPage /></Wrapper>);

    fireEvent.click(screen.getByRole('button', { name: /Filters/i }));
    const last7 = screen.getByRole('button', { name: 'Last 7 Days' });
    expect(last7.className).toMatch(/bg-violet-600/);

    const calls = mockUseAuditLogs.mock.calls;
    const params = calls[calls.length - 1][0];
    expect(params.from).toBeDefined();
    expect(params.to).toBeDefined();
    const spanHours = (new Date(params.to).getTime() - new Date(params.from).getTime()) / 3_600_000;
    expect(spanHours).toBeCloseTo(168, 0);
  });

  it('preserves explicit from/to URL params instead of applying the Last 7 Days default', () => {
    const Wrapper = createWrapper(
      '/audit?namespace=ns1&from=2026-01-01T00%3A00%3A00.000Z&to=2026-01-02T00%3A00%3A00.000Z'
    );
    render(<Wrapper><AuditPage /></Wrapper>);

    const params = mockUseAuditLogs.mock.calls[0][0];
    expect(params.from).toBe('2026-01-01T00:00:00.000Z');
    expect(params.to).toBe('2026-01-02T00:00:00.000Z');
  });

  it('renders a Details column as the last column with a View action per row', () => {
    mockUseAuditLogs.mockReturnValue({ data: mockAuditDataTwoItems, isLoading: false, refetch: vi.fn(), isFetching: false });
    const Wrapper = createWrapper();
    render(<Wrapper><AuditPage /></Wrapper>);

    const headers = screen.getAllByRole('columnheader');
    expect(headers[headers.length - 1]).toHaveTextContent('Details');
    // Outcome stays its own column, immediately before Details.
    expect(headers[headers.length - 2]).toHaveTextContent('Outcome');

    const viewButtons = screen.getAllByRole('button', { name: 'View' });
    expect(viewButtons).toHaveLength(2);
  });

  it('clicking View opens the detail drawer for that exact row, not another one', () => {
    mockUseAuditLogs.mockReturnValue({ data: mockAuditDataTwoItems, isLoading: false, refetch: vi.fn(), isFetching: false });
    const Wrapper = createWrapper();
    render(<Wrapper><AuditPage /></Wrapper>);

    const viewButtons = screen.getAllByRole('button', { name: 'View' });
    fireEvent.click(viewButtons[1]);

    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText('second@user.com')).toBeInTheDocument();
    expect(within(dialog).getByText('POST /api/v2/namespaces/register')).toBeInTheDocument();
    expect(within(dialog).getByText('corr-2')).toBeInTheDocument();
  });

  it('View action works alongside pagination and filtered results', () => {
    mockUseAuditLogs.mockReturnValue({
      data: { ...mockAuditDataTwoItems, hasPreviousPage: false, hasNextPage: true, totalCount: 52 },
      isLoading: false,
      refetch: vi.fn(),
      isFetching: false,
    });
    const Wrapper = createWrapper();
    render(<Wrapper><AuditPage /></Wrapper>);

    const nextPageBtn = screen.getAllByRole('button').find(b => b.querySelector('.lucide-chevron-right'))!;
    fireEvent.click(nextPageBtn);

    const params = mockUseAuditLogs.mock.calls[mockUseAuditLogs.mock.calls.length - 1][0];
    expect(params.page).toBe(2);

    const viewButtons = screen.getAllByRole('button', { name: 'View' });
    fireEvent.click(viewButtons[0]);
    expect(within(screen.getByRole('dialog')).getByText('test@user.com')).toBeInTheDocument();
  });

  // Regression: a "Recent Changes Before Failure" deep link (?from=&to=) legitimately finds zero
  // audit events most of the time — that just means nothing was changed in the namespace during
  // that narrow window. The page used to show the same "No audit logs found / Try adjusting your
  // filters" copy as a broken/misconfigured filter, which read as an error rather than an
  // expected, reassuring result.
  it('shows a reassuring empty state (not a generic filter error) for a deep-linked failure window with no events', () => {
    mockUseAuditLogs.mockReturnValue({
      data: { ...mockAuditData, items: [], totalCount: 0 },
      isLoading: false,
      refetch: vi.fn(),
      isFetching: false,
    });
    const Wrapper = createWrapper(
      '/audit?namespace=ns1&from=2026-09-04T12%3A07%3A43.234Z&to=2026-09-05T12%3A07%3A43.234572%2B00%3A00'
    );
    render(<Wrapper><AuditPage /></Wrapper>);

    expect(screen.getByText('No configuration changes in this window')).toBeInTheDocument();
    expect(screen.queryByText('No audit logs found')).not.toBeInTheDocument();
    expect(screen.getByText(/isn't a sign of missing data/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'See all audit events' }));
    const calls = mockUseAuditLogs.mock.calls;
    const params = calls[calls.length - 1][0];
    // Falls back to the page's own "Last 7 Days" default rather than an unbounded, undated
    // query — an old deep link shouldn't be the only way to end up viewing all-time audit data.
    expect(params.from).toBeDefined();
    expect(params.to).toBeDefined();
    const spanHours = (new Date(params.to).getTime() - new Date(params.from).getTime()) / 3_600_000;
    expect(spanHours).toBeCloseTo(168, 0);
    expect(screen.getByRole('button', { name: 'Last 7 Days' }).className).toMatch(/bg-violet-600/);
  });

  it('clearing a deep-linked date range via the filter chip also falls back to Last 7 Days, not all-time', () => {
    const Wrapper = createWrapper(
      '/audit?namespace=ns1&from=2026-09-04T12%3A07%3A43.234Z&to=2026-09-05T12%3A07%3A43.234572%2B00%3A00'
    );
    render(<Wrapper><AuditPage /></Wrapper>);

    fireEvent.click(screen.getByRole('button', { name: 'Clear custom date range' }));
    const calls = mockUseAuditLogs.mock.calls;
    const params = calls[calls.length - 1][0];
    expect(params.from).toBeDefined();
    expect(params.to).toBeDefined();
    const spanHours = (new Date(params.to).getTime() - new Date(params.from).getTime()) / 3_600_000;
    expect(spanHours).toBeCloseTo(168, 0);
  });
});
