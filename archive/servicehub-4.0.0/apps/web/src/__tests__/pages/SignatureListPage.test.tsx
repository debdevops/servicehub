import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type React from 'react';
import { SignatureListPage } from '@/pages/SignatureListPage';

vi.mock('@servicehub/ui-shared/hooks/useDlqSignatures', () => ({
  useDlqSignatures: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useDlqHistory', () => ({
  useDlqSummary: vi.fn(() => ({ data: undefined })),
}));
vi.mock('@servicehub/ui-shared/lib/demo/DemoContext', () => ({
  useDemoContext: vi.fn(),
}));
vi.mock('@/components/dlq', () => ({
  FailureInvestigationPanel: () => <div data-testid="investigation-panel" />,
  SignatureSummaryCard: ({ signature }: { signature: { dominantDeadletterReason: string; status: string; trend: string } }) => (
    <div>
      <span>{signature.dominantDeadletterReason}</span>
      <span>{signature.status}</span>
      <span>{signature.trend}</span>
    </div>
  ),
}));

import { useDlqSignatures } from '@servicehub/ui-shared/hooks/useDlqSignatures';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';

const mockUseDlqSignatures = useDlqSignatures as ReturnType<typeof vi.fn>;
const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;
const mockUseDemoContext = useDemoContext as ReturnType<typeof vi.fn>;

const mockNamespaces = [
  { id: 'ns1', name: 'my-namespace', displayName: 'My Namespace', isActive: true, cloudProvider: 'azure' },
];

const mockCluster = {
  size: 4,
  messageIds: [1, 2, 3, 4],
  dominantEntity: 'orders-queue',
  dominantDeadletterReason: 'MaxDeliveryCountExceeded',
  dominantDeadletterReasonCount: 4,
  topTerms: ['timeout'],
  isNew: false,
  firstSeenAt: '2026-01-01T00:00:00Z',
  occurrenceCount: 4,
  windowStart: '2026-01-01T00:00:00Z',
  windowEnd: '2026-01-01T01:00:00Z',
  explanation: '4 messages: max delivery count exceeded on orders-queue.',
  signatureHash: 'hash-1',
  status: 'Active',
  trend: 'Recurring',
};

function createWrapper(initialPath = '/signatures?namespace=ns1') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: React.ReactNode }) => (
    <MemoryRouter initialEntries={[initialPath]}>
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    </MemoryRouter>
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  mockUseNamespaces.mockReturnValue({ data: mockNamespaces });
  mockUseDemoContext.mockReturnValue({ isDemoMode: false });
  mockUseDlqSignatures.mockReturnValue({
    data: { available: true, method: 'clustered', batchSize: 5, clusters: [mockCluster], singletons: [] },
    loading: false,
    error: null,
    available: true,
  });
});

describe('SignatureListPage', () => {
  it('renders the page title', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><SignatureListPage /></Wrapper>);
    expect(screen.getByText('Failure Signatures')).toBeInTheDocument();
  });

  it('renders a signature list item with status and trend badges', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><SignatureListPage /></Wrapper>);
    expect(screen.getByText('MaxDeliveryCountExceeded')).toBeInTheDocument();
    expect(screen.getAllByText('Active').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('Recurring').length).toBeGreaterThanOrEqual(1);
  });

  it('renders the signature list in demo mode too — demo fixtures come from the data hooks, not a page-level bailout', () => {
    mockUseDemoContext.mockReturnValue({ isDemoMode: true, cloudProvider: 'azure' });
    const Wrapper = createWrapper();
    render(<Wrapper><SignatureListPage /></Wrapper>);
    expect(screen.getByText('MaxDeliveryCountExceeded')).toBeInTheDocument();
  });

  it('filters the list by search query across owner, tags, runbook, and root cause', () => {
    mockUseDlqSignatures.mockReturnValue({
      data: {
        available: true,
        method: 'clustered',
        batchSize: 5,
        clusters: [
          {
            ...mockCluster,
            knowledge: {
              rootCause: 'Database timeout',
              resolutionNotes: null,
              operationalNotes: null,
              runbookLink: null,
              owner: 'platform-team@example.com',
              replayGuidance: 'Safe',
              lastUpdatedAt: null,
              knowledgeVersion: 1,
              reviewDueAt: null,
              tags: 'database',
              updatedBy: null,
              isReviewOverdue: false,
            },
          },
        ],
        singletons: [],
      },
      loading: false,
      error: null,
      available: true,
    });
    const Wrapper = createWrapper();
    render(<Wrapper><SignatureListPage /></Wrapper>);

    fireEvent.change(screen.getByPlaceholderText(/search by owner/i), { target: { value: 'nonexistent' } });
    expect(screen.getByText('No failure signatures match the current filters.')).toBeInTheDocument();

    fireEvent.change(screen.getByPlaceholderText(/search by owner/i), { target: { value: 'platform-team' } });
    expect(screen.getByText('MaxDeliveryCountExceeded')).toBeInTheDocument();
  });

  it('filters the list by review status', () => {
    mockUseDlqSignatures.mockReturnValue({
      data: {
        available: true,
        method: 'clustered',
        batchSize: 5,
        clusters: [
          {
            ...mockCluster,
            knowledge: {
              rootCause: 'Database timeout',
              resolutionNotes: null,
              operationalNotes: null,
              runbookLink: null,
              owner: null,
              replayGuidance: null,
              lastUpdatedAt: null,
              knowledgeVersion: 1,
              reviewDueAt: '2026-01-01T00:00:00Z',
              tags: null,
              updatedBy: null,
              isReviewOverdue: true,
            },
          },
        ],
        singletons: [],
      },
      loading: false,
      error: null,
      available: true,
    });
    const Wrapper = createWrapper();
    render(<Wrapper><SignatureListPage /></Wrapper>);

    fireEvent.click(screen.getByRole('button', { name: 'No review date' }));
    expect(screen.getByText('No failure signatures match the current filters.')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'No review date' }));
    fireEvent.click(screen.getByRole('button', { name: 'Overdue' }));
    expect(screen.getByText('MaxDeliveryCountExceeded')).toBeInTheDocument();
  });

  it('filters the list by status', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><SignatureListPage /></Wrapper>);

    fireEvent.click(screen.getByRole('button', { name: 'Resolved' }));
    expect(screen.getByText('No failure signatures match the current filters.')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Resolved' }));
    expect(screen.getByText('MaxDeliveryCountExceeded')).toBeInTheDocument();
  });

  it('shows unavailable message when signature analysis is unavailable', () => {
    mockUseDlqSignatures.mockReturnValue({
      data: { available: false, method: null, batchSize: 5, clusters: [], singletons: [] },
      loading: false,
      error: null,
      available: false,
    });
    const Wrapper = createWrapper();
    render(<Wrapper><SignatureListPage /></Wrapper>);
    expect(screen.getByText(/Signature analysis is unavailable/)).toBeInTheDocument();
  });
});
