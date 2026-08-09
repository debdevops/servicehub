import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { RecentChangesPanel } from '@/components/dlq/RecentChangesPanel';
import type { AuditLogItem, AuditPageResponse } from '@servicehub/ui-shared/lib/api/audit';

vi.mock('@servicehub/ui-shared/hooks/useAudit', () => ({
  useAuditLogs: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/lib/demo/DemoContext', () => ({
  useDemoContext: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/lib/demo/mockProviders', () => ({
  getMockRecentChanges: vi.fn(),
}));

import { useAuditLogs } from '@servicehub/ui-shared/hooks/useAudit';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { getMockRecentChanges } from '@servicehub/ui-shared/lib/demo/mockProviders';

const mockUseAuditLogs = useAuditLogs as ReturnType<typeof vi.fn>;
const mockUseDemoContext = useDemoContext as ReturnType<typeof vi.fn>;
const mockGetMockRecentChanges = getMockRecentChanges as ReturnType<typeof vi.fn>;

function change(overrides: Partial<AuditLogItem> = {}): AuditLogItem {
  return {
    id: 'audit-1',
    timestamp: '2026-08-03T09:14:00Z',
    userIdentity: 'alice@example.com',
    action: 'Rule.Toggle',
    outcome: 'Success',
    namespaceId: 'ns-1',
    namespaceName: 'prod-orders',
    entityName: null,
    cloudProvider: 'azure',
    environment: 'Prod',
    resourceName: 'Retry-on-timeout',
    sequenceNumber: null,
    detailsJson: null,
    errorDetails: null,
    clientIp: null,
    userAgent: null,
    correlationId: null,
    httpMethod: null,
    httpPath: null,
    ...overrides,
  };
}

function page(items: AuditLogItem[]): AuditPageResponse {
  return { items, totalCount: items.length, page: 1, pageSize: 20, hasNextPage: false, hasPreviousPage: false };
}

function renderPanel() {
  return render(
    <RecentChangesPanel namespaceId="ns-1" signatureHash="hash-1" firstSeenAt="2026-08-03T14:00:00.000Z" />,
    { wrapper: MemoryRouter },
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  mockUseDemoContext.mockReturnValue({ isDemoMode: false });
  mockGetMockRecentChanges.mockReturnValue(undefined);
});

describe('RecentChangesPanel', () => {
  it('shows a loading state while fetching', () => {
    mockUseAuditLogs.mockReturnValue({ data: undefined, isLoading: true, error: null });

    renderPanel();

    expect(screen.getByText('Loading recent changes…')).toBeInTheDocument();
  });

  it('shows the no-changes recommendation when the window has no audit entries', () => {
    mockUseAuditLogs.mockReturnValue({ data: page([]), isLoading: false, error: null });

    renderPanel();

    expect(
      screen.getByText('No recorded configuration changes in the 24h before this failure started.'),
    ).toBeInTheDocument();
  });

  it('renders each change and the review recommendation when entries are present', () => {
    mockUseAuditLogs.mockReturnValue({
      data: page([
        change({ id: 'audit-1', action: 'Rule.Toggle', resourceName: 'Retry-on-timeout', userIdentity: 'alice@example.com' }),
      ]),
      isLoading: false,
      error: null,
    });

    renderPanel();

    expect(screen.getByText('Rule.Toggle')).toBeInTheDocument();
    expect(screen.getByText(/Retry-on-timeout/)).toBeInTheDocument();
    expect(screen.getByText('alice@example.com')).toBeInTheDocument();
    expect(
      screen.getByText('1 change occurred in the 24h before this failure started — review before further action.'),
    ).toBeInTheDocument();
  });

  it('links to the full audit trail pre-filtered to the namespace', () => {
    mockUseAuditLogs.mockReturnValue({ data: page([]), isLoading: false, error: null });

    renderPanel();

    const link = screen.getByRole('link', { name: /View full audit trail/ });
    expect(link).toHaveAttribute('href', '/audit?namespace=ns-1');
  });

  it('renders literal window boundary text so the fixed lookback is never hidden', () => {
    mockUseAuditLogs.mockReturnValue({ data: page([]), isLoading: false, error: null });

    renderPanel();

    expect(screen.getByText(/Aug 2, 14:00.*Aug 3, 14:00.*UTC/)).toBeInTheDocument();
  });

  it('shows a graceful message when the caller lacks audit:read (403)', () => {
    mockUseAuditLogs.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: { response: { status: 403 } },
    });

    renderPanel();

    expect(screen.getByText('Audit access required to view recent changes.')).toBeInTheDocument();
  });

  it('renders fixture changes from demo mode instead of calling the live audit query', () => {
    mockUseDemoContext.mockReturnValue({ isDemoMode: true });
    mockUseAuditLogs.mockReturnValue({ data: undefined, isLoading: false, error: null });
    mockGetMockRecentChanges.mockReturnValue(
      page([change({ id: 'demo-1', action: 'Namespace.Create', resourceName: 'prod-orders-eastus' })]),
    );

    renderPanel();

    expect(screen.getByText('Namespace.Create')).toBeInTheDocument();
    expect(screen.getByText(/prod-orders-eastus/)).toBeInTheDocument();
  });

  it('shows the no-changes recommendation in demo mode when the fixture has no entries', () => {
    mockUseDemoContext.mockReturnValue({ isDemoMode: true });
    mockUseAuditLogs.mockReturnValue({ data: undefined, isLoading: false, error: null });
    mockGetMockRecentChanges.mockReturnValue(page([]));

    renderPanel();

    expect(
      screen.getByText('No recorded configuration changes in the 24h before this failure started.'),
    ).toBeInTheDocument();
  });
});
