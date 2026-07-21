import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { BulkOperationProgressPanel } from '@/components/dlq/BulkOperationProgressPanel';
import type { BulkOperationJob } from '@/lib/api/bulkOperations';

vi.mock('@/hooks/useBulkOperations', () => ({
  useBulkOperationJob: vi.fn(),
  useCancelBulkOperation: vi.fn(),
}));

import { useBulkOperationJob, useCancelBulkOperation } from '@/hooks/useBulkOperations';

const mockUseJob = useBulkOperationJob as ReturnType<typeof vi.fn>;
const mockUseCancel = useCancelBulkOperation as ReturnType<typeof vi.fn>;

function makeJob(overrides: Partial<BulkOperationJob> = {}): BulkOperationJob {
  return {
    id: 'job-1',
    operationType: 'Replay',
    status: 'Running',
    namespaceId: 'ns-1',
    namespaceDisplayName: 'Orders Namespace',
    entityNameFilter: null,
    statusFilter: 'Active',
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

describe('BulkOperationProgressPanel', () => {
  it('renders nothing while the job has not loaded yet', () => {
    mockUseJob.mockReturnValue({ data: undefined });
    const { container } = render(<BulkOperationProgressPanel jobId="job-1" onDismiss={vi.fn()} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('shows progress counts and percentage for a running job', () => {
    mockUseJob.mockReturnValue({ data: makeJob() });
    render(<BulkOperationProgressPanel jobId="job-1" onDismiss={vi.fn()} />);

    expect(screen.getByText('4 / 10 processed')).toBeInTheDocument();
    expect(screen.getByText('40%')).toBeInTheDocument();
    expect(screen.getByText('4 succeeded')).toBeInTheDocument();
    expect(screen.getByText('Orders Namespace')).toBeInTheDocument();
  });

  it('shows a Cancel button for a cancellable running job', () => {
    mockUseJob.mockReturnValue({ data: makeJob() });
    render(<BulkOperationProgressPanel jobId="job-1" onDismiss={vi.fn()} />);

    fireEvent.click(screen.getByRole('button', { name: /Cancel/ }));
    expect(cancelMutate).toHaveBeenCalledWith('job-1');
  });

  it('hides the Cancel button once the job is terminal and shows a dismiss button', () => {
    const onDismiss = vi.fn();
    mockUseJob.mockReturnValue({
      data: makeJob({ status: 'Completed', isCancellable: false, processedCount: 10, successCount: 10 }),
    });
    render(<BulkOperationProgressPanel jobId="job-1" onDismiss={onDismiss} />);

    expect(screen.queryByRole('button', { name: /Cancel/ })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Dismiss' }));
    expect(onDismiss).toHaveBeenCalled();
  });

  it('shows failure and skipped counts when present', () => {
    mockUseJob.mockReturnValue({
      data: makeJob({ status: 'CompletedWithErrors', failureCount: 2, skippedCount: 1, isCancellable: false }),
    });
    render(<BulkOperationProgressPanel jobId="job-1" onDismiss={vi.fn()} />);

    expect(screen.getByText('2 failed')).toBeInTheDocument();
    expect(screen.getByText('1 skipped')).toBeInTheDocument();
  });

  it('shows the error summary for a failed job', () => {
    mockUseJob.mockReturnValue({
      data: makeJob({ status: 'Failed', errorSummary: 'Namespace no longer exists', isCancellable: false }),
    });
    render(<BulkOperationProgressPanel jobId="job-1" onDismiss={vi.fn()} />);

    expect(screen.getByText('Namespace no longer exists')).toBeInTheDocument();
  });
});
