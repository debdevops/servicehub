import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { SignatureReplayProgressPanel } from '@/components/dlq/SignatureReplayProgressPanel';
import type { BulkOperationJob } from '@servicehub/ui-shared/lib/api/bulkOperations';

vi.mock('@servicehub/ui-shared/hooks/useSignatureReplay', () => ({
  useSignatureReplayJob: vi.fn(),
  useCancelSignatureReplayJob: vi.fn(),
}));

import { useSignatureReplayJob, useCancelSignatureReplayJob } from '@servicehub/ui-shared/hooks/useSignatureReplay';

const mockUseJob = useSignatureReplayJob as ReturnType<typeof vi.fn>;
const mockUseCancel = useCancelSignatureReplayJob as ReturnType<typeof vi.fn>;

function makeJob(overrides: Partial<BulkOperationJob> = {}): BulkOperationJob {
  return {
    id: 'job-1',
    operationType: 'Replay',
    status: 'Running',
    namespaceId: 'ns-1',
    namespaceDisplayName: 'Orders Namespace',
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

const cancelMutate = vi.fn();

beforeEach(() => {
  vi.clearAllMocks();
  mockUseCancel.mockReturnValue({ mutate: cancelMutate, isPending: false });
});

describe('SignatureReplayProgressPanel', () => {
  it('renders nothing while the job has not loaded yet', () => {
    mockUseJob.mockReturnValue({ data: undefined });
    const { container } = render(
      <SignatureReplayProgressPanel jobId="job-1" namespaceId="ns-1" signatureHash="hash-1" onDismiss={vi.fn()} />,
      { wrapper: MemoryRouter },
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('passes namespaceId and signatureHash through to the polling hook', () => {
    mockUseJob.mockReturnValue({ data: undefined });
    render(<SignatureReplayProgressPanel jobId="job-1" namespaceId="ns-1" signatureHash="hash-1" onDismiss={vi.fn()} />, { wrapper: MemoryRouter });

    expect(mockUseJob).toHaveBeenCalledWith('job-1', 'ns-1', 'hash-1');
  });

  it('shows progress counts and percentage for a running job', () => {
    mockUseJob.mockReturnValue({ data: makeJob() });
    render(<SignatureReplayProgressPanel jobId="job-1" namespaceId="ns-1" signatureHash="hash-1" onDismiss={vi.fn()} />, { wrapper: MemoryRouter });

    expect(screen.getByText('4 / 10 processed')).toBeInTheDocument();
    expect(screen.getByText('40%')).toBeInTheDocument();
    expect(screen.getByText('Orders Namespace')).toBeInTheDocument();
  });

  it('explains an indefinite Pending spinner as waiting behind other jobs on the shared worker', () => {
    mockUseJob.mockReturnValue({
      data: makeJob({ status: 'Pending', processedCount: 0, successCount: 0, queueAheadCount: 2 }),
    });
    render(<SignatureReplayProgressPanel jobId="job-1" namespaceId="ns-1" signatureHash="hash-1" onDismiss={vi.fn()} />, { wrapper: MemoryRouter });

    expect(screen.getByText('Waiting behind 2 other replay jobs.')).toBeInTheDocument();
  });

  it('tells a next-up Pending job it will start as soon as the worker frees up', () => {
    mockUseJob.mockReturnValue({
      data: makeJob({ status: 'Pending', processedCount: 0, successCount: 0, queueAheadCount: 0 }),
    });
    render(<SignatureReplayProgressPanel jobId="job-1" namespaceId="ns-1" signatureHash="hash-1" onDismiss={vi.fn()} />, { wrapper: MemoryRouter });

    expect(screen.getByText('Next up — the worker will pick this up as soon as it is free.')).toBeInTheDocument();
  });

  it('shows a Cancel button for a cancellable running job', () => {
    mockUseJob.mockReturnValue({ data: makeJob() });
    render(<SignatureReplayProgressPanel jobId="job-1" namespaceId="ns-1" signatureHash="hash-1" onDismiss={vi.fn()} />, { wrapper: MemoryRouter });

    fireEvent.click(screen.getByRole('button', { name: /Cancel/ }));
    expect(cancelMutate).toHaveBeenCalledWith('job-1');
  });

  it('hides the Cancel button once the job is terminal and shows a dismiss button', () => {
    const onDismiss = vi.fn();
    mockUseJob.mockReturnValue({
      data: makeJob({ status: 'Completed', isCancellable: false, processedCount: 10, successCount: 10 }),
    });
    render(<SignatureReplayProgressPanel jobId="job-1" namespaceId="ns-1" signatureHash="hash-1" onDismiss={onDismiss} />, { wrapper: MemoryRouter });

    expect(screen.queryByRole('button', { name: /Cancel/ })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Dismiss' }));
    expect(onDismiss).toHaveBeenCalled();
  });

  it('labels each failure with its provider-agnostic reason category, distinguishing not-found from retryable', () => {
    mockUseJob.mockReturnValue({
      data: makeJob({
        status: 'CompletedWithErrors',
        failureCount: 2,
        failureSample: [
          {
            messageId: 'msg-1',
            entityName: 'orders',
            reason: 'Message 42 was not found in the DLQ — it may have been consumed, replayed, or expired.',
            reasonCategory: 'NotFound',
          },
          {
            messageId: 'msg-2',
            entityName: 'orders',
            reason: 'Service temporarily unavailable',
            reasonCategory: 'Retryable',
          },
        ],
      }),
    });
    render(<SignatureReplayProgressPanel jobId="job-1" namespaceId="ns-1" signatureHash="hash-1" onDismiss={vi.fn()} />, { wrapper: MemoryRouter });

    fireEvent.click(screen.getByText(/View failure details/));

    expect(screen.getByText('Not found in DLQ')).toBeInTheDocument();
    expect(screen.getByText('Retryable')).toBeInTheDocument();
  });

  it('points a completed job at the incident workspace for real verification instead of implying the replay itself is confirmation', () => {
    mockUseJob.mockReturnValue({
      data: makeJob({ status: 'Completed', isCancellable: false, processedCount: 10, successCount: 10 }),
    });
    render(<SignatureReplayProgressPanel jobId="job-1" namespaceId="ns-1" signatureHash="hash-1" onDismiss={vi.fn()} />, { wrapper: MemoryRouter });

    const link = screen.getByRole('link', { name: 'Check verification status' });
    expect(link).toHaveAttribute('href', '/incidents/hash-1?namespace=ns-1&tab=recovery');
  });

  it('does not show a verification link for a still-running job', () => {
    mockUseJob.mockReturnValue({ data: makeJob({ status: 'Running' }) });
    render(<SignatureReplayProgressPanel jobId="job-1" namespaceId="ns-1" signatureHash="hash-1" onDismiss={vi.fn()} />, { wrapper: MemoryRouter });

    expect(screen.queryByRole('link', { name: 'Check verification status' })).not.toBeInTheDocument();
  });
});
