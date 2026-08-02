import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
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
});
