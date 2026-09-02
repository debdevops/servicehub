import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type React from 'react';
import { IncidentWorkspacePage } from '@/pages/IncidentWorkspacePage';

vi.mock('@servicehub/ui-shared/hooks/useIncident', () => ({
  useIncident: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/lib/demo/DemoContext', () => ({
  useDemoContext: vi.fn(),
}));

import { useIncident } from '@servicehub/ui-shared/hooks/useIncident';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';

const mockUseIncident = useIncident as ReturnType<typeof vi.fn>;
const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;
const mockUseDemoContext = useDemoContext as ReturnType<typeof vi.fn>;

const mockNamespaces = [
  { id: 'ns1', name: 'my-namespace', displayName: 'My Namespace', isActive: true, cloudProvider: 'azure', environment: 'prod' },
];

const mockIncident = {
  signatureHash: 'hash-1',
  namespaceId: 'ns1',
  namespaceName: 'contoso-prod',
  lifecycleStatus: 'Active',
  firstSeenAt: '2026-01-01T00:00:00Z',
  lastSeenAt: '2026-01-02T00:00:00Z',
  occurrenceCount: 4,
  dominantDeadletterReason: 'MaxDeliveryCountExceeded',
  topTerms: ['timeout', 'orders'],
  summary: {
    recoveryEntryCount: 2,
    openRecoveryEntryCount: 1,
    pendingDecisionCount: 1,
    anomalyFlagCount: 1,
    driftFindingCount: 0,
    correlationHypothesisCount: 0,
    preventionTriggerCount: 0,
    replayPlanCount: 1,
  },
  recoveryEntries: [
    {
      id: 'entry-1',
      operationId: 'op-1',
      dlqMessageId: 1,
      namespaceId: 'ns1',
      namespaceNameSnapshot: 'contoso-prod',
      providerSnapshot: 'azure',
      environmentSnapshot: 'prod',
      entityNameSnapshot: 'orders-queue',
      entityTypeSnapshot: 'Queue',
      topicNameSnapshot: null,
      bodyHash: 'sha256-1',
      failureCategorySnapshot: 'TransientDependency',
      deadLetterReasonSnapshot: 'MaxDeliveryCountExceeded',
      signatureHashSnapshot: 'hash-1',
      targetEntity: 'orders-queue',
      begunAt: '2026-01-02T00:00:00Z',
      markerApplied: true,
      state: 'Recovered',
      disposition: 'Recovered',
      verificationResult: 'Recovered',
      verificationConfidence: null,
      observationWindowEndsAt: null,
      closedAt: '2026-01-02T01:00:00Z',
    },
  ],
  playbookEntries: [
    {
      id: 'pb-1',
      pillarKind: 'Investigate',
      proposalKind: 'AnomalyFlag',
      evidenceRefJson: '{}',
      proposalJson: '{"detail":"spike in failures"}',
      proposedAt: '2026-01-01T12:00:00Z',
      proposerIdentity: 'system',
      proposerKind: 'System',
      signatureHashSnapshot: 'hash-1',
      namespaceId: 'ns1',
      namespaceNameSnapshot: 'contoso-prod',
      providerSnapshot: 'azure',
      environmentSnapshot: 'prod',
      relatedRecoveryOperationId: null,
      expiresAt: '2026-01-08T12:00:00Z',
      state: 'Proposed',
      disposition: null,
      closedAt: null,
    },
    {
      id: 'pb-2',
      pillarKind: 'Recover',
      proposalKind: 'ReplayPlan',
      evidenceRefJson: '{}',
      proposalJson: '{"scope":"orders-queue"}',
      proposedAt: '2026-01-01T13:00:00Z',
      proposerIdentity: 'system',
      proposerKind: 'System',
      signatureHashSnapshot: 'hash-1',
      namespaceId: 'ns1',
      namespaceNameSnapshot: 'contoso-prod',
      providerSnapshot: 'azure',
      environmentSnapshot: 'prod',
      relatedRecoveryOperationId: 'op-1',
      expiresAt: '2026-01-08T13:00:00Z',
      state: 'UnderReview',
      disposition: null,
      closedAt: null,
    },
  ],
};

function createWrapper(initialPath = '/incidents/hash-1?namespace=ns1') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: React.ReactNode }) => (
    <MemoryRouter initialEntries={[initialPath]}>
      <QueryClientProvider client={queryClient}>
        <Routes>
          <Route path="/incidents/:signatureHash" element={children} />
        </Routes>
      </QueryClientProvider>
    </MemoryRouter>
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  mockUseNamespaces.mockReturnValue({ data: mockNamespaces });
  mockUseDemoContext.mockReturnValue({ isDemoMode: false });
  mockUseIncident.mockReturnValue({ data: mockIncident, isLoading: false, isError: false, error: undefined, refetch: vi.fn() });
});

describe('IncidentWorkspacePage', () => {
  it('renders the incident heading, fingerprint, and top terms', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><IncidentWorkspacePage /></Wrapper>);
    expect(screen.getByText('MaxDeliveryCountExceeded')).toBeInTheDocument();
    expect(screen.getByText(/Fingerprint: hash-1/)).toBeInTheDocument();
    expect(screen.getByText('timeout')).toBeInTheDocument();
    expect(screen.getByText('orders')).toBeInTheDocument();
  });

  it('shows a pending-decision banner when the incident is blocked on a human', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><IncidentWorkspacePage /></Wrapper>);
    expect(screen.getByText(/1 decision waiting on a human/)).toBeInTheDocument();
  });

  it('does not show the pending-decision banner when nothing is blocked', () => {
    mockUseIncident.mockReturnValue({
      data: { ...mockIncident, summary: { ...mockIncident.summary, pendingDecisionCount: 0 } },
      isLoading: false,
      isError: false,
    });
    const Wrapper = createWrapper();
    render(<Wrapper><IncidentWorkspacePage /></Wrapper>);
    expect(screen.queryByText(/waiting on a human/)).not.toBeInTheDocument();
  });

  it('defaults to the Summary tab, showing the incident counts', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><IncidentWorkspacePage /></Wrapper>);
    expect(screen.getByText('Anomaly Flags')).toBeInTheDocument();
    expect(screen.getByText('Replay Plans')).toBeInTheDocument();
  });

  it('switches to the Evidence tab and shows anomaly/drift/correlation proposals only', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><IncidentWorkspacePage /></Wrapper>);

    fireEvent.click(screen.getByRole('button', { name: 'Evidence' }));
    expect(screen.getByText('AnomalyFlag')).toBeInTheDocument();
    expect(screen.queryByText('ReplayPlan')).not.toBeInTheDocument();
  });

  it('switches to the Recommended Recovery tab and shows replay/prevention proposals plus grouped operations', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><IncidentWorkspacePage /></Wrapper>);

    fireEvent.click(screen.getByRole('button', { name: 'Recommended Recovery' }));
    expect(screen.getByText('ReplayPlan')).toBeInTheDocument();
    expect(screen.getByText('orders-queue')).toBeInTheDocument();
    expect(screen.getByText('1 entry')).toBeInTheDocument();
  });

  it('links a grouped recovery operation to its own detail page', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><IncidentWorkspacePage /></Wrapper>);

    fireEvent.click(screen.getByRole('button', { name: 'Recommended Recovery' }));
    expect(screen.getByText('orders-queue').closest('a')).toHaveAttribute('href', '/recovery/op-1');
  });

  it('switches to the Activity tab and shows a merged, most-recent-first feed', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><IncidentWorkspacePage /></Wrapper>);

    fireEvent.click(screen.getByRole('button', { name: 'Activity' }));
    const rows = screen.getAllByText(/Recovery entry begun|proposed/);
    // Most recent first: recovery entry (Jan 2) before either playbook proposal (Jan 1).
    expect(rows[0]).toHaveTextContent('Recovery entry begun');
  });

  it('shows an empty state on the Evidence tab when there is no evidence', () => {
    mockUseIncident.mockReturnValue({
      data: { ...mockIncident, playbookEntries: [] },
      isLoading: false,
      isError: false,
    });
    const Wrapper = createWrapper();
    render(<Wrapper><IncidentWorkspacePage /></Wrapper>);

    fireEvent.click(screen.getByRole('button', { name: 'Evidence' }));
    expect(screen.getByText('No evidence recorded')).toBeInTheDocument();
  });

  it('links out to the full signature investigation', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><IncidentWorkspacePage /></Wrapper>);
    expect(screen.getByText('Open full signature investigation →')).toBeInTheDocument();
  });

  describe('load states', () => {
    it('shows the loading text only while the request is in flight', () => {
      mockUseIncident.mockReturnValue({ data: undefined, isLoading: true, isError: false });
      const Wrapper = createWrapper();
      render(<Wrapper><IncidentWorkspacePage /></Wrapper>);
      expect(screen.getByText('Loading incident…')).toBeInTheDocument();
    });

    it('shows a terminal not-found message for a 404 rather than an infinite spinner', () => {
      mockUseIncident.mockReturnValue({
        data: undefined,
        isLoading: false,
        isError: true,
        error: { response: { status: 404 } },
        refetch: vi.fn(),
      });
      const Wrapper = createWrapper();
      render(<Wrapper><IncidentWorkspacePage /></Wrapper>);
      expect(screen.getByText('Incident not found')).toBeInTheDocument();
    });

    it('surfaces the API error message with a retry when the request fails', () => {
      const refetch = vi.fn();
      mockUseIncident.mockReturnValue({
        data: undefined,
        isLoading: false,
        isError: true,
        error: { response: { status: 500, data: { detail: 'Namespace is unreachable.' } } },
        refetch,
      });
      const Wrapper = createWrapper();
      render(<Wrapper><IncidentWorkspacePage /></Wrapper>);

      expect(screen.getByText('Namespace is unreachable.')).toBeInTheDocument();
      fireEvent.click(screen.getByRole('button', { name: 'Try Again' }));
      expect(refetch).toHaveBeenCalledTimes(1);
    });

    it('still reports the missing-reference guard when the namespace query param is absent', () => {
      const Wrapper = createWrapper('/incidents/hash-1');
      render(<Wrapper><IncidentWorkspacePage /></Wrapper>);
      expect(screen.getByText('Missing namespace or signature reference.')).toBeInTheDocument();
    });
  });
});
