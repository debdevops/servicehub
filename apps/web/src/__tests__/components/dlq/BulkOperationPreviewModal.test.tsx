import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { BulkOperationPreviewModal } from '@/components/dlq/BulkOperationPreviewModal';

vi.mock('@/hooks/useBulkOperations', () => ({
  useBulkOperationPreview: vi.fn(),
  useCreateBulkOperation: vi.fn(),
}));

import { useBulkOperationPreview, useCreateBulkOperation } from '@/hooks/useBulkOperations';

const mockUsePreview = useBulkOperationPreview as ReturnType<typeof vi.fn>;
const mockUseCreate = useCreateBulkOperation as ReturnType<typeof vi.fn>;

const filter = { namespaceId: 'ns-1', status: 'Active' };

function setup(previewOverrides: Record<string, unknown> = {}, createOverrides: Record<string, unknown> = {}) {
  const previewMutate = vi.fn();
  const createMutateAsync = vi.fn();
  mockUsePreview.mockReturnValue({
    mutate: previewMutate,
    isPending: false,
    isError: false,
    data: undefined,
    ...previewOverrides,
  });
  mockUseCreate.mockReturnValue({
    mutateAsync: createMutateAsync,
    isPending: false,
    ...createOverrides,
  });

  const onClose = vi.fn();
  const onJobCreated = vi.fn();
  render(
    <BulkOperationPreviewModal
      operationType="Replay"
      filter={filter}
      onClose={onClose}
      onJobCreated={onJobCreated}
    />,
  );
  return { previewMutate, createMutateAsync, onClose, onJobCreated };
}

beforeEach(() => vi.clearAllMocks());

describe('BulkOperationPreviewModal', () => {
  it('triggers the preview mutation with the operation type and filter on mount', () => {
    const { previewMutate } = setup();
    expect(previewMutate).toHaveBeenCalledWith({ operationType: 'Replay', filter });
  });

  it('shows a loading state while the preview is pending', () => {
    setup({ isPending: true });
    expect(screen.getByText(/Matching messages/)).toBeInTheDocument();
  });

  it('renders the matched count, warnings, and sample once loaded', () => {
    setup({
      data: {
        totalMatched: 3,
        sample: [
          { dlqMessageId: 1, messageId: 'msg-1', entityName: 'orders', deadLetterReason: 'MaxDeliveryCountExceeded', replaySafety: 'Safe' },
        ],
        canExecute: true,
        warnings: ['1 of the matched message(s) are flagged \'Unsafe\' to replay'],
        unsafeReplayCount: 1,
      },
    });

    expect(screen.getByText('3')).toBeInTheDocument();
    expect(screen.getByText(/flagged 'Unsafe'/)).toBeInTheDocument();
    expect(screen.getByText('msg-1')).toBeInTheDocument();
  });

  it('disables the confirm button when canExecute is false', () => {
    setup({ data: { totalMatched: 0, sample: [], canExecute: false, warnings: ['No DLQ messages match this filter.'], unsafeReplayCount: 0 } });

    const confirmButton = screen.getByRole('button', { name: /Replay 0 message/ });
    expect(confirmButton).toBeDisabled();
  });

  it('calls createJob and onJobCreated when confirmed', async () => {
    const { createMutateAsync, onJobCreated } = setup({
      data: { totalMatched: 2, sample: [], canExecute: true, warnings: [], unsafeReplayCount: 0 },
    });
    createMutateAsync.mockResolvedValue({ id: 'job-123' });

    fireEvent.click(screen.getByRole('button', { name: /Replay 2 messages/ }));

    await waitFor(() => expect(onJobCreated).toHaveBeenCalledWith('job-123'));
    expect(createMutateAsync).toHaveBeenCalledWith({ operationType: 'Replay', filter });
  });

  it('calls onClose when Cancel is clicked', () => {
    const { onClose } = setup({ data: { totalMatched: 1, sample: [], canExecute: true, warnings: [], unsafeReplayCount: 0 } });

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(onClose).toHaveBeenCalled();
  });
});
