import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { FailureIntelligenceCenterPage } from '@/pages/FailureIntelligenceCenterPage';
import type { InvestigationCenterResponse, FailedReplayItem } from '@servicehub/ui-shared/hooks/useInvestigationQueue';

vi.mock('@servicehub/ui-shared/hooks/useInvestigationQueue', async () => {
  const actual = await vi.importActual('@servicehub/ui-shared/hooks/useInvestigationQueue');
  return { ...actual, useInvestigationQueue: vi.fn() };
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

function makeResponse(failedReplays: FailedReplayItem[]): InvestigationCenterResponse {
  return {
    metrics: EMPTY_METRICS,
    investigationQueue: [],
    failedReplays,
    knowledgeReview: [],
    newSignatures: [],
    recentlyChanged: [],
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

  it('links "View Details" to the signature, scoped to its namespace', () => {
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

    const link = screen.getByRole('button', { name: 'View details for PoisonMessage (ID: hash-42)' });
    expect(link).toBeInTheDocument();
  });
});
