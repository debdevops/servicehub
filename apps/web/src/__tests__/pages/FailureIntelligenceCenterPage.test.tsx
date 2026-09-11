import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { FailureIntelligenceCenterPage } from '@/pages/FailureIntelligenceCenterPage';
import { DemoModeProvider } from '@servicehub/ui-shared/lib/demo/DemoContext';
import type {
  InvestigationCenterResponse,
  FailedReplayItem,
  FleetHealthSummary,
  InvestigationQueueItem,
  KnowledgeReviewItem,
  NewSignatureItem,
} from '@servicehub/ui-shared/hooks/useInvestigationQueue';

vi.mock('@servicehub/ui-shared/hooks/useInvestigationQueue', async () => {
  const actual = await vi.importActual('@servicehub/ui-shared/hooks/useInvestigationQueue');
  return { ...actual, useInvestigationQueue: vi.fn() };
});

const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

import { useInvestigationQueue } from '@servicehub/ui-shared/hooks/useInvestigationQueue';

const mockUseInvestigationQueue = useInvestigationQueue as ReturnType<typeof vi.fn>;

const EMPTY_METRICS = {
  totalSignatures: 0,
  activeSignatures: 0,
  resolvedSignatures: 0,
  suppressedSignatures: 0,
  archivedSignatures: 0,
  requiresAction: 0,
};

function makeResponse(
  failedReplays: FailedReplayItem[],
  fleetHealth: FleetHealthSummary | null = null,
  investigationQueue: InvestigationQueueItem[] = [],
  knowledgeReview: KnowledgeReviewItem[] = [],
  newSignatures: NewSignatureItem[] = [],
): InvestigationCenterResponse {
  return {
    metrics: EMPTY_METRICS,
    investigationQueue,
    failedReplays,
    knowledgeReview,
    newSignatures,
    recentlyChanged: [],
    fleetHealth,
  };
}

function renderPage() {
  return render(
    <MemoryRouter>
      <FailureIntelligenceCenterPage />
    </MemoryRouter>,
  );
}

beforeEach(() => vi.clearAllMocks());

describe('FailureIntelligenceCenterPage — Fleet Health section', () => {
  it('does not render the section when fleetHealth is null', () => {
    mockUseInvestigationQueue.mockReturnValue({ data: makeResponse([], null), isLoading: false, error: null, refetch: vi.fn() });

    renderPage();

    expect(screen.queryByText('Fleet Health')).not.toBeInTheDocument();
  });

  it('renders unhealthy namespaces ahead of the Investigation Queue section', () => {
    const fleetHealth: FleetHealthSummary = {
      namespaceCount: 2,
      totalActive: 9,
      totalNewInWindow: 3,
      totalResolvedInWindow: 0,
      topUnhealthyNamespaces: [
        {
          namespaceId: 'ns-1',
          namespaceName: 'prod-orders',
          provider: 'Azure',
          environment: 'Prod',
          activeCount: 9,
          newInWindow: 3,
          resolvedInWindow: 0,
          totalCount: 9,
          topEntity: 'orders-queue',
          topEntityCount: 9,
          topCategory: 'Timeout',
          oldestActiveDetectedAt: new Date().toISOString(),
          severity: 'critical',
          coverage: 'scanned',
          coverageNote: null,
        },
      ],
    };
    mockUseInvestigationQueue.mockReturnValue({ data: makeResponse([], fleetHealth), isLoading: false, error: null, refetch: vi.fn() });

    renderPage();

    expect(screen.getByText('Fleet Health')).toBeInTheDocument();
    expect(screen.getByText('prod-orders')).toBeInTheDocument();

    const fleetHealthHeading = screen.getByText('Fleet Health');
    const queueHeading = screen.getByText('No incidents require attention');
    expect(fleetHealthHeading.compareDocumentPosition(queueHeading) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });
});

describe('FailureIntelligenceCenterPage — Failed Replays section', () => {
  it('shows the empty state when there are no failed replays', () => {
    mockUseInvestigationQueue.mockReturnValue({ data: makeResponse([]), isLoading: false, error: null, refetch: vi.fn() });

    renderPage();

    expect(screen.getByText('No failed replays')).toBeInTheDocument();
  });

  it('renders a failed replay with its status, counts, and recommended action', () => {
    const item: FailedReplayItem = {
      jobId: 'job-1',
      namespaceId: 'ns-1',
      signatureHash: 'hash-1',
      signatureName: 'MaxDeliveryCountExceeded (ID: hash-1)',
      jobStatus: 'Failed',
      failureReason: '5 of 5 message(s) failed.',
      createdAt: new Date().toISOString(),
      completedAt: new Date().toISOString(),
      attemptedCount: 5,
      failedCount: 5,
      recommendedNextAction: 'Investigate the underlying failure before replaying again.',
    };
    mockUseInvestigationQueue.mockReturnValue({ data: makeResponse([item]), isLoading: false, error: null, refetch: vi.fn() });

    renderPage();

    expect(screen.getByText('Failed Replays')).toBeInTheDocument();
    expect(screen.getByText('MaxDeliveryCountExceeded (ID: hash-1)')).toBeInTheDocument();
    expect(screen.getByText('Failed')).toBeInTheDocument();
    expect(screen.getByText('5 attempted, 5 failed')).toBeInTheDocument();
    expect(screen.getByText('5 of 5 message(s) failed.')).toBeInTheDocument();
    expect(screen.getByText(/Investigate the underlying failure before replaying again\./)).toBeInTheDocument();
  });

  it('sends "View Details" to the Incident Workspace\'s Recommended Recovery tab, not the raw signature page', () => {
    const item: FailedReplayItem = {
      jobId: 'job-1',
      namespaceId: 'ns-42',
      signatureHash: 'hash-42',
      signatureName: 'PoisonMessage (ID: hash-42)',
      jobStatus: 'CompletedWithErrors',
      failureReason: null,
      createdAt: new Date().toISOString(),
      completedAt: new Date().toISOString(),
      attemptedCount: 3,
      failedCount: 1,
      recommendedNextAction: 'Review the failure sample before retrying.',
    };
    mockUseInvestigationQueue.mockReturnValue({ data: makeResponse([item]), isLoading: false, error: null, refetch: vi.fn() });

    renderPage();

    fireEvent.click(screen.getByRole('button', { name: 'View details for PoisonMessage (ID: hash-42)' }));
    expect(mockNavigate).toHaveBeenCalledWith('/incidents/hash-42?namespace=ns-42&tab=recovery');
  });
});

// Regression coverage for a real drift bug: these sections predate the Incident Workspace
// (/incidents/:hash) and were still sending every "Investigate"/"View Details" action to the
// older, bypassed /signatures/:hash page — the same class of bug this page's own
// "Open full signature investigation →" link (IncidentWorkspacePage.tsx) exists to be the
// deliberate escape hatch *from*, not a page users should land on first. Knowledge actions are
// the one exception: IncidentWorkspacePage has no knowledge-editing UI, so those correctly stay
// on /signatures.
describe('FailureIntelligenceCenterPage — investigate/knowledge link destinations', () => {
  function queueItem(overrides: Partial<InvestigationQueueItem> = {}): InvestigationQueueItem {
    return {
      signatureHash: 'hash-1',
      namespaceId: 'ns-1',
      displayName: 'ProcessingError (ID: hash-1)',
      dominantDeadletterReason: 'ProcessingError',
      messageCount: 12,
      status: 'Active',
      trend: 'Active',
      priorityScore: 5,
      hasKnowledge: true,
      isEscalating: false,
      owner: null,
      recommendedNextAction: 'Investigate',
      explanation: null,
      ...overrides,
    };
  }

  it('sends the highest-priority banner\'s Investigate and Replay Preview actions to the Incident Workspace', () => {
    mockUseInvestigationQueue.mockReturnValue({
      data: makeResponse([], null, [queueItem({ isEscalating: true })]),
      isLoading: false,
      error: null,
      refetch: vi.fn(),
    });
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: 'Investigate incident' }));
    expect(mockNavigate).toHaveBeenLastCalledWith('/incidents/hash-1?namespace=ns-1');

    fireEvent.click(screen.getByRole('button', { name: 'Preview replay' }));
    expect(mockNavigate).toHaveBeenLastCalledWith('/incidents/hash-1?namespace=ns-1&tab=recovery');
  });

  it('sends a queued incident\'s Investigate and Replay actions to the Incident Workspace, but Knowledge to the signature page', () => {
    mockUseInvestigationQueue.mockReturnValue({
      data: makeResponse([], null, [
        queueItem({ signatureHash: 'hash-top', namespaceId: 'ns-top' }),
        queueItem({ signatureHash: 'hash-2', namespaceId: 'ns-2', displayName: 'DataQuality (ID: hash-2)' }),
      ]),
      isLoading: false,
      error: null,
      refetch: vi.fn(),
    });
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: 'Investigate DataQuality (ID: hash-2)' }));
    expect(mockNavigate).toHaveBeenLastCalledWith('/incidents/hash-2?namespace=ns-2');

    fireEvent.click(screen.getByRole('button', { name: 'Preview replay for DataQuality (ID: hash-2)' }));
    expect(mockNavigate).toHaveBeenLastCalledWith('/incidents/hash-2?namespace=ns-2&tab=recovery');

    fireEvent.click(screen.getByRole('button', { name: 'Review knowledge for DataQuality (ID: hash-2)' }));
    expect(mockNavigate).toHaveBeenLastCalledWith('/signatures/hash-2?namespace=ns-2');
  });

  function knowledgeItem(overrides: Partial<KnowledgeReviewItem> = {}): KnowledgeReviewItem {
    return {
      signatureHash: 'hash-3',
      namespaceId: 'ns-3',
      displayName: 'Transient (ID: hash-3)',
      messageCount: 4,
      status: 'Active',
      owner: null,
      hasKnowledge: false,
      isReviewOverdue: false,
      reviewDueAt: null,
      lastUpdatedAt: null,
      recommendedNextAction: 'Add Knowledge',
      ...overrides,
    };
  }

  it('sends Knowledge Review\'s "Add Knowledge" to the signature page and "View Details" to the Incident Workspace', () => {
    mockUseInvestigationQueue.mockReturnValue({
      data: makeResponse([], null, [], [knowledgeItem()]),
      isLoading: false,
      error: null,
      refetch: vi.fn(),
    });
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: 'Add knowledge for Transient (ID: hash-3)' }));
    expect(mockNavigate).toHaveBeenLastCalledWith('/signatures/hash-3?namespace=ns-3');

    fireEvent.click(screen.getByRole('button', { name: 'Investigate Transient (ID: hash-3)' }));
    expect(mockNavigate).toHaveBeenLastCalledWith('/incidents/hash-3?namespace=ns-3');
  });

  function newSignatureItem(overrides: Partial<NewSignatureItem> = {}): NewSignatureItem {
    return {
      signatureHash: 'hash-4',
      namespaceId: 'ns-4',
      displayName: 'Unknown (ID: hash-4)',
      dominantDeadletterReason: 'Unknown',
      messageCount: 2,
      firstSeenAt: new Date().toISOString(),
      lastSeenAt: new Date().toISOString(),
      explanation: null,
      recommendedNextAction: 'Add Knowledge',
      ...overrides,
    };
  }

  it('sends a new signature\'s "Add Knowledge" to the signature page and "View Details" to the Incident Workspace', () => {
    mockUseInvestigationQueue.mockReturnValue({
      data: makeResponse([], null, [], [], [newSignatureItem()]),
      isLoading: false,
      error: null,
      refetch: vi.fn(),
    });
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: 'Add knowledge for Unknown (ID: hash-4)' }));
    expect(mockNavigate).toHaveBeenLastCalledWith('/signatures/hash-4?namespace=ns-4');

    fireEvent.click(screen.getByRole('button', { name: 'Investigate Unknown (ID: hash-4)' }));
    expect(mockNavigate).toHaveBeenLastCalledWith('/incidents/hash-4?namespace=ns-4');
  });

  it('applies the /demo/{provider} prefix to every destination in Demo Mode — a real drift bug this page had (no navPrefix at all) until now', () => {
    mockUseInvestigationQueue.mockReturnValue({
      data: makeResponse([], null, [queueItem()], [knowledgeItem()]),
      isLoading: false,
      error: null,
      refetch: vi.fn(),
    });
    render(
      <MemoryRouter initialEntries={['/demo/aws/incidents']}>
        <DemoModeProvider cloudProvider="aws">
          <FailureIntelligenceCenterPage />
        </DemoModeProvider>
      </MemoryRouter>,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Investigate incident' }));
    expect(mockNavigate).toHaveBeenLastCalledWith('/demo/aws/incidents/hash-1?namespace=ns-1');

    fireEvent.click(screen.getByRole('button', { name: 'Add knowledge for Transient (ID: hash-3)' }));
    expect(mockNavigate).toHaveBeenLastCalledWith('/demo/aws/signatures/hash-3?namespace=ns-3');
  });
});
