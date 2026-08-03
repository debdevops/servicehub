import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { ReplaySafetyPanel } from '@/components/dlq/ReplaySafetyPanel';
import type { BulkOperationJob } from '@servicehub/ui-shared/lib/api/bulkOperations';

vi.mock('@servicehub/ui-shared/hooks/useCloudBridge', () => ({
  useProviderCapabilities: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useSignatureReplay', () => ({
  useSignatureReplayHistory: vi.fn(),
}));

import { useProviderCapabilities } from '@servicehub/ui-shared/hooks/useCloudBridge';
import { useSignatureReplayHistory } from '@servicehub/ui-shared/hooks/useSignatureReplay';

const mockUseCapabilities = useProviderCapabilities as ReturnType<typeof vi.fn>;
const mockUseHistory = useSignatureReplayHistory as ReturnType<typeof vi.fn>;

function makeJob(overrides: Partial<BulkOperationJob> = {}): BulkOperationJob {
  return {
    id: 'job-1',
    operationType: 'Replay',
    status: 'Completed',
    namespaceId: 'ns-1',
    namespaceDisplayName: 'Orders Namespace',
    entityNameFilter: null,
    statusFilter: null,
    categoryFilter: null,
    from: null,
    to: null,
    totalMatched: 10,
    processedCount: 10,
    successCount: 10,
    failureCount: 0,
    skippedCount: 0,
    failureSample: null,
    errorSummary: null,
    createdAt: new Date().toISOString(),
    startedAt: new Date().toISOString(),
    completedAt: new Date().toISOString(),
    isCancellable: false,
    ...overrides,
  };
}

const AZURE_CAPABILITIES = {
  Azure: {
    supportsMessageCounts: true,
    supportsManualDeadLetter: true,
    supportsPurge: false,
    supportsScheduledMessages: true,
    supportsRepeatablePeek: true,
    notes: 'Purge is not supported.',
  },
};

const AWS_CAPABILITIES = {
  Aws: {
    supportsMessageCounts: true,
    supportsManualDeadLetter: true,
    supportsPurge: true,
    supportsScheduledMessages: false,
    supportsRepeatablePeek: false,
    notes: 'Every peek is a real receive.',
  },
};

beforeEach(() => vi.clearAllMocks());

describe('ReplaySafetyPanel', () => {
  it('shows "Safe to replay" and a Start Replay button when the provider supports repeatable peek and the last job succeeded', () => {
    mockUseCapabilities.mockReturnValue({ data: AZURE_CAPABILITIES });
    mockUseHistory.mockReturnValue({ data: { items: [makeJob({ status: 'Completed' })], totalCount: 1 }, isLoading: false });
    const onStartReplay = vi.fn();

    render(
      <ReplaySafetyPanel namespaceId="ns-1" signatureHash="hash-1" cloudProvider="azure" onStartReplay={onStartReplay} />,
    );

    expect(screen.getByText('Safe to replay')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Start Replay' }));
    expect(onStartReplay).toHaveBeenCalledTimes(1);
  });

  it('shows "Review before replay" and no Start Replay button when the provider has no repeatable peek', () => {
    mockUseCapabilities.mockReturnValue({ data: AWS_CAPABILITIES });
    mockUseHistory.mockReturnValue({ data: { items: [], totalCount: 0 }, isLoading: false });

    render(
      <ReplaySafetyPanel namespaceId="ns-1" signatureHash="hash-1" cloudProvider="aws" onStartReplay={vi.fn()} />,
    );

    expect(screen.getByText(/Review before replay/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Start Replay' })).not.toBeInTheDocument();
    expect(screen.getByText('Every peek is a real receive.')).toBeInTheDocument();
  });

  it('shows "Review before replay" when the most recent job failed, even on a repeatable-peek provider', () => {
    mockUseCapabilities.mockReturnValue({ data: AZURE_CAPABILITIES });
    mockUseHistory.mockReturnValue({
      data: { items: [makeJob({ status: 'Failed', completedAt: new Date().toISOString() })], totalCount: 1 },
      isLoading: false,
    });

    render(
      <ReplaySafetyPanel namespaceId="ns-1" signatureHash="hash-1" cloudProvider="azure" onStartReplay={vi.fn()} />,
    );

    expect(screen.getByText(/Review before replay/)).toBeInTheDocument();
  });

  it('renders past replay attempts with their outcome', () => {
    mockUseCapabilities.mockReturnValue({ data: AZURE_CAPABILITIES });
    mockUseHistory.mockReturnValue({
      data: { items: [makeJob({ status: 'Completed', successCount: 4, totalMatched: 4 })], totalCount: 1 },
      isLoading: false,
    });

    render(
      <ReplaySafetyPanel namespaceId="ns-1" signatureHash="hash-1" cloudProvider="azure" onStartReplay={vi.fn()} />,
    );

    expect(screen.getByText('4/4 succeeded')).toBeInTheDocument();
    expect(screen.getByText('Completed')).toBeInTheDocument();
  });

  it('shows an empty state when there is no replay history', () => {
    mockUseCapabilities.mockReturnValue({ data: AZURE_CAPABILITIES });
    mockUseHistory.mockReturnValue({ data: { items: [], totalCount: 0 }, isLoading: false });

    render(
      <ReplaySafetyPanel namespaceId="ns-1" signatureHash="hash-1" cloudProvider="azure" onStartReplay={vi.fn()} />,
    );

    expect(screen.getByText('No replay attempts recorded for this signature yet.')).toBeInTheDocument();
  });
});
