import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

vi.mock('react-hot-toast', () => ({
  default: { success: vi.fn(), error: vi.fn(), dismiss: vi.fn() },
}));

const mockIsDemoMode = vi.fn(() => false);
vi.mock('../../../lib/demo/DemoContext', async () => {
  const actual = await vi.importActual<typeof import('../../../lib/demo/DemoContext')>('../../../lib/demo/DemoContext');
  return {
    ...actual,
    useDemoContext: () => ({ isDemoMode: mockIsDemoMode() }),
  };
});

vi.mock('../../../lib/api/signatureReplay', () => ({
  signatureReplayApi: { getJob: vi.fn() },
}));

import toast from 'react-hot-toast';
import { signatureReplayApi } from '../../../lib/api/signatureReplay';
import { ActiveJobsProvider, useActiveJobs } from '../../../lib/activeJobs/ActiveJobsContext';
import { useSignatureReplayJob, __resetSignatureReplayNotificationsForTests } from '../../../hooks/useSignatureReplay';
import type { BulkOperationJob } from '../../../lib/api/bulkOperations';

const mockGetJob = signatureReplayApi.getJob as ReturnType<typeof vi.fn>;
const mockToastSuccess = toast.success as ReturnType<typeof vi.fn>;

function makeJob(overrides: Partial<BulkOperationJob> = {}): BulkOperationJob {
  return {
    id: 'job-1',
    operationType: 'Replay',
    status: 'Running',
    namespaceId: 'ns-1',
    namespaceDisplayName: 'Orders',
    entityNameFilter: null,
    statusFilter: null,
    categoryFilter: null,
    from: null,
    to: null,
    totalMatched: 10,
    processedCount: 4,
    successCount: 4,
    failureCount: 0,
    skippedCount: 0,
    failureSample: null,
    errorSummary: null,
    createdAt: new Date().toISOString(),
    startedAt: new Date().toISOString(),
    completedAt: null,
    isCancellable: true,
    ...overrides,
  };
}

function Tracker({ jobId, namespaceId, signatureHash }: { jobId: string; namespaceId: string; signatureHash: string }) {
  const { trackSignatureReplay } = useActiveJobs();
  React.useEffect(() => {
    trackSignatureReplay(jobId, namespaceId, signatureHash);
  }, [jobId, namespaceId, signatureHash, trackSignatureReplay]);
  return null;
}

function renderWithProvider(children: React.ReactNode) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <ActiveJobsProvider>{children}</ActiveJobsProvider>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  mockIsDemoMode.mockReturnValue(false);
  localStorage.clear();
  __resetSignatureReplayNotificationsForTests();
});

describe('ActiveJobsProvider', () => {
  it('polls a tracked job and fires the completion toast once it reaches a terminal status', async () => {
    mockGetJob.mockResolvedValue(makeJob({ id: 'job-terminal-1', status: 'Completed', successCount: 10 }));

    renderWithProvider(<Tracker jobId="job-terminal-1" namespaceId="ns-1" signatureHash="hash-1" />);

    await waitFor(() => expect(mockToastSuccess).toHaveBeenCalledWith(
      expect.stringContaining('Signature replay completed'),
    ));
  });

  it('persists tracked jobs to localStorage so a page reload resumes watching', async () => {
    mockGetJob.mockResolvedValue(makeJob({ id: 'job-persist-1', status: 'Running' }));

    renderWithProvider(<Tracker jobId="job-persist-1" namespaceId="ns-1" signatureHash="hash-1" />);

    await waitFor(() => {
      const stored = JSON.parse(localStorage.getItem('servicehub.activeJobs.v1') || '[]');
      expect(stored).toEqual([
        { kind: 'signature-replay', jobId: 'job-persist-1', namespaceId: 'ns-1', signatureHash: 'hash-1' },
      ]);
    });
  });

  it('removes a job from tracking (and localStorage) once it reaches a terminal status', async () => {
    mockGetJob.mockResolvedValue(makeJob({ id: 'job-untrack-1', status: 'Failed', errorSummary: 'boom' }));

    renderWithProvider(<Tracker jobId="job-untrack-1" namespaceId="ns-1" signatureHash="hash-1" />);

    await waitFor(() => {
      const stored = JSON.parse(localStorage.getItem('servicehub.activeJobs.v1') || '[]');
      expect(stored).toEqual([]);
    });
  });

  it('resumes watching a job restored from localStorage on mount, without the page having re-tracked it', async () => {
    localStorage.setItem(
      'servicehub.activeJobs.v1',
      JSON.stringify([{ kind: 'signature-replay', jobId: 'job-resumed-1', namespaceId: 'ns-1', signatureHash: 'hash-1' }]),
    );
    mockGetJob.mockResolvedValue(makeJob({ id: 'job-resumed-1', status: 'Completed' }));

    renderWithProvider(<div />);

    await waitFor(() => expect(mockGetJob).toHaveBeenCalledWith('job-resumed-1'));
    await waitFor(() => expect(mockToastSuccess).toHaveBeenCalled());
  });

  it('does not fire a duplicate toast when a page-level progress panel polls the same job the tracker is also watching', async () => {
    mockGetJob.mockResolvedValue(makeJob({ id: 'job-dedup-1', status: 'Completed', successCount: 5 }));

    function PageLevelPanel() {
      useSignatureReplayJob('job-dedup-1', 'ns-1', 'hash-1');
      return null;
    }

    renderWithProvider(
      <>
        <Tracker jobId="job-dedup-1" namespaceId="ns-1" signatureHash="hash-1" />
        <PageLevelPanel />
      </>,
    );

    await waitFor(() => expect(mockToastSuccess).toHaveBeenCalledTimes(1));
    // Give any second, redundant effect a chance to also fire before asserting it didn't.
    await act(() => new Promise((resolve) => setTimeout(resolve, 20)));
    expect(mockToastSuccess).toHaveBeenCalledTimes(1);
  });
});
