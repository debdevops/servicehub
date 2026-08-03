import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type React from 'react';
import { SignatureDetailsPage } from '@/pages/SignatureDetailsPage';

vi.mock('@servicehub/ui-shared/hooks/useDlqSignatures', () => ({
  useDlqSignatureDetail: vi.fn(),
  useSignatureTimeline: vi.fn(),
  useResolveSignature: vi.fn(),
  useReopenSignature: vi.fn(),
  useSuppressSignature: vi.fn(),
  useArchiveSignature: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/lib/demo/DemoContext', () => ({
  useDemoContext: vi.fn(),
}));
vi.mock('@/components/dlq', () => ({
  StatusBadge: ({ status }: { status: string }) => <span>Status:{status}</span>,
  TrendBadge: ({ trend }: { trend: string }) => <span>Trend:{trend}</span>,
  FailureInvestigationPanel: () => <div data-testid="investigation-panel" />,
  SignatureLifecycleActions: ({ status }: { status: string }) => <div data-testid="lifecycle-actions">Actions for {status}</div>,
  SignatureTimelinePanel: ({ events }: { events: unknown[] }) => <div data-testid="timeline-panel">{events.length} events</div>,
  DlqTimelineDrawer: ({ messageId }: { messageId: number | null }) =>
    messageId ? <div data-testid="timeline-drawer">Timeline {messageId}</div> : null,
  SignatureReplayPreviewModal: ({ onClose, onJobStarted }: { onClose: () => void; onJobStarted: (jobId: string) => void }) => (
    <div data-testid="replay-preview-modal">
      <button onClick={onClose}>Close preview</button>
      <button onClick={() => onJobStarted('job-123')}>Confirm replay</button>
    </div>
  ),
  SignatureReplayProgressPanel: ({ jobId }: { jobId: string }) => (
    <div data-testid="replay-progress-panel">Progress for {jobId}</div>
  ),
  ReplaySafetyPanel: ({ onStartReplay }: { onStartReplay: () => void }) => (
    <div data-testid="replay-safety-panel">
      <button onClick={onStartReplay}>Panel start replay</button>
    </div>
  ),
  RootCauseExplorerPanel: ({ namespaceId, signatureHash }: { namespaceId: string; signatureHash: string }) => (
    <div data-testid="root-cause-explorer-panel">Root cause explorer for {namespaceId}/{signatureHash}</div>
  ),
  RecentChangesPanel: ({ namespaceId, signatureHash, firstSeenAt }: { namespaceId: string; signatureHash: string; firstSeenAt: string }) => (
    <div data-testid="recent-changes-panel">Recent changes for {namespaceId}/{signatureHash} before {firstSeenAt}</div>
  ),
  CrossCloudTraceLink: ({ correlationId }: { correlationId?: string | null }) =>
    correlationId ? <a data-testid="cross-cloud-trace-link" href={`/cross-cloud-trace?traceId=${correlationId}`}>View cross-cloud path</a> : null,
  getTrendRecommendation: (trend: string) => (trend === 'Recurring' ? 'Check the Timeline for prior attempts before acting again.' : null),
}));

import {
  useDlqSignatureDetail,
  useSignatureTimeline,
  useResolveSignature,
  useReopenSignature,
  useSuppressSignature,
  useArchiveSignature,
} from '@servicehub/ui-shared/hooks/useDlqSignatures';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';

const mockUseDlqSignatureDetail = useDlqSignatureDetail as ReturnType<typeof vi.fn>;
const mockUseSignatureTimeline = useSignatureTimeline as ReturnType<typeof vi.fn>;
const mockUseResolveSignature = useResolveSignature as ReturnType<typeof vi.fn>;
const mockUseReopenSignature = useReopenSignature as ReturnType<typeof vi.fn>;
const mockUseSuppressSignature = useSuppressSignature as ReturnType<typeof vi.fn>;
const mockUseArchiveSignature = useArchiveSignature as ReturnType<typeof vi.fn>;
const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;
const mockUseDemoContext = useDemoContext as ReturnType<typeof vi.fn>;

const mockNamespaces = [
  { id: 'ns1', name: 'my-namespace', displayName: 'My Namespace', isActive: true, cloudProvider: 'azure' },
];

const mockDetail = {
  signatureHash: 'hash-1',
  namespaceId: 'ns1',
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
  status: 'Active',
  trend: 'Recurring',
  confidence: 'High',
  isCurrentlyClustered: true,
  relatedMessages: [
    { id: 1, messageId: 'msg-1', entityName: 'orders-queue', status: 'Active' },
  ],
};

function createWrapper(initialPath = '/signatures/hash-1?namespace=ns1') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: React.ReactNode }) => (
    <MemoryRouter initialEntries={[initialPath]}>
      <QueryClientProvider client={queryClient}>
        <Routes>
          <Route path="/signatures/:signatureHash" element={children} />
        </Routes>
      </QueryClientProvider>
    </MemoryRouter>
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  mockUseNamespaces.mockReturnValue({ data: mockNamespaces });
  mockUseDemoContext.mockReturnValue({ isDemoMode: false });
  mockUseDlqSignatureDetail.mockReturnValue({ data: mockDetail, isLoading: false });
  mockUseSignatureTimeline.mockReturnValue({ data: { signatureHash: 'hash-1', events: [] }, isLoading: false });
  mockUseResolveSignature.mockReturnValue({ mutate: vi.fn(), isPending: false });
  mockUseReopenSignature.mockReturnValue({ mutate: vi.fn(), isPending: false });
  mockUseSuppressSignature.mockReturnValue({ mutate: vi.fn(), isPending: false });
  mockUseArchiveSignature.mockReturnValue({ mutate: vi.fn(), isPending: false });
});

describe('SignatureDetailsPage', () => {
  it('renders the signature heading and fingerprint', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><SignatureDetailsPage /></Wrapper>);
    expect(screen.getByText(/MaxDeliveryCountExceeded/)).toBeInTheDocument();
    expect(screen.getByText(/Fingerprint: hash-1/)).toBeInTheDocument();
  });

  it('renders status, trend, occurrence count, and confidence', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><SignatureDetailsPage /></Wrapper>);
    expect(screen.getAllByText('Status:Active').length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('Trend:Recurring')).toBeInTheDocument();
    expect(screen.getByText('4')).toBeInTheDocument();
    expect(screen.getByText('High')).toBeInTheDocument();
  });

  it('renders lifecycle actions for the current status', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><SignatureDetailsPage /></Wrapper>);
    expect(screen.getByText('Actions for Active')).toBeInTheDocument();
  });

  it('renders the timeline panel', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><SignatureDetailsPage /></Wrapper>);
    expect(screen.getByTestId('timeline-panel')).toBeInTheDocument();
  });

  it('renders related messages', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><SignatureDetailsPage /></Wrapper>);
    expect(screen.getByText('Related Messages (1)')).toBeInTheDocument();
    expect(screen.getByText('msg-1')).toBeInTheDocument();
  });

  it('renders the Replay Safety & History panel', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><SignatureDetailsPage /></Wrapper>);
    expect(screen.getByTestId('replay-safety-panel')).toBeInTheDocument();
  });

  it('renders the Root Cause Explorer panel for the current namespace and signature', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><SignatureDetailsPage /></Wrapper>);
    expect(screen.getByTestId('root-cause-explorer-panel')).toHaveTextContent('Root cause explorer for ns1/hash-1');
  });

  it('renders the Recent Changes Before Failure panel for the current namespace, signature, and first-seen window', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><SignatureDetailsPage /></Wrapper>);
    expect(screen.getByTestId('recent-changes-panel')).toHaveTextContent(
      'Recent changes for ns1/hash-1 before 2026-01-01T00:00:00Z',
    );
  });

  it('opening replay from the Replay Safety panel reuses the same preview flow as the Actions bar button', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><SignatureDetailsPage /></Wrapper>);

    expect(screen.queryByTestId('replay-preview-modal')).not.toBeInTheDocument();
    fireEvent.click(screen.getByText('Panel start replay'));
    expect(screen.getByTestId('replay-preview-modal')).toBeInTheDocument();
  });

  describe('View cross-cloud path', () => {
    it('does not render when no related message carries a correlation ID', () => {
      const Wrapper = createWrapper();
      render(<Wrapper><SignatureDetailsPage /></Wrapper>);
      expect(screen.queryByTestId('cross-cloud-trace-link')).not.toBeInTheDocument();
    });

    it('renders when a related message carries a correlation ID', () => {
      mockUseDlqSignatureDetail.mockReturnValue({
        data: {
          ...mockDetail,
          relatedMessages: [
            { id: 1, messageId: 'msg-1', entityName: 'orders-queue', status: 'Active', correlationId: 'trace-xyz' },
          ],
        },
        isLoading: false,
      });
      const Wrapper = createWrapper();
      render(<Wrapper><SignatureDetailsPage /></Wrapper>);
      expect(screen.getByTestId('cross-cloud-trace-link')).toHaveAttribute(
        'href',
        '/cross-cloud-trace?traceId=trace-xyz',
      );
    });
  });

  it('renders signature details in demo mode too — demo fixtures come from the data hooks, not a page-level bailout', () => {
    mockUseDemoContext.mockReturnValue({ isDemoMode: true, cloudProvider: 'azure' });
    const Wrapper = createWrapper();
    render(<Wrapper><SignatureDetailsPage /></Wrapper>);
    expect(screen.getByText(/MaxDeliveryCountExceeded/)).toBeInTheDocument();
  });

  it('shows a historical-record note when the signature is not currently clustered', () => {
    mockUseDlqSignatureDetail.mockReturnValue({
      data: { ...mockDetail, isCurrentlyClustered: false, relatedMessages: [] },
      isLoading: false,
    });
    const Wrapper = createWrapper();
    render(<Wrapper><SignatureDetailsPage /></Wrapper>);
    expect(screen.getByText('No — historical record')).toBeInTheDocument();
  });

  describe('Replay Signature', () => {
    it('is enabled when the signature is currently clustered with related messages', () => {
      const Wrapper = createWrapper();
      render(<Wrapper><SignatureDetailsPage /></Wrapper>);
      expect(screen.getByRole('button', { name: /Replay Signature/ })).toBeEnabled();
    });

    it('is disabled when the signature is not currently clustered', () => {
      mockUseDlqSignatureDetail.mockReturnValue({
        data: { ...mockDetail, isCurrentlyClustered: false, relatedMessages: [] },
        isLoading: false,
      });
      const Wrapper = createWrapper();
      render(<Wrapper><SignatureDetailsPage /></Wrapper>);
      expect(screen.getByRole('button', { name: /Replay Signature/ })).toBeDisabled();
    });

    it('opens the preview modal on click, then shows the progress panel once a job starts', () => {
      const Wrapper = createWrapper();
      render(<Wrapper><SignatureDetailsPage /></Wrapper>);

      expect(screen.queryByTestId('replay-preview-modal')).not.toBeInTheDocument();

      fireEvent.click(screen.getByRole('button', { name: /Replay Signature/ }));
      expect(screen.getByTestId('replay-preview-modal')).toBeInTheDocument();

      fireEvent.click(screen.getByText('Confirm replay'));
      expect(screen.queryByTestId('replay-preview-modal')).not.toBeInTheDocument();
      expect(screen.getByTestId('replay-progress-panel')).toHaveTextContent('Progress for job-123');
    });
  });
});
