import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
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

  it('shows a demo-mode message instead of details when in demo mode', () => {
    mockUseDemoContext.mockReturnValue({ isDemoMode: true });
    const Wrapper = createWrapper();
    render(<Wrapper><SignatureDetailsPage /></Wrapper>);
    expect(screen.getByText(/require a live connection/)).toBeInTheDocument();
    expect(screen.queryByText(/MaxDeliveryCountExceeded/)).not.toBeInTheDocument();
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
});
