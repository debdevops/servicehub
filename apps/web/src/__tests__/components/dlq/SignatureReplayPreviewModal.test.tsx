import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { SignatureReplayPreviewModal } from '@/components/dlq/SignatureReplayPreviewModal';

vi.mock('@servicehub/ui-shared/hooks/useSignatureReplay', () => ({
  useSignatureReplayPreview: vi.fn(),
  useStartSignatureReplay: vi.fn(),
}));

import { useSignatureReplayPreview, useStartSignatureReplay } from '@servicehub/ui-shared/hooks/useSignatureReplay';

const mockUsePreview = useSignatureReplayPreview as ReturnType<typeof vi.fn>;
const mockUseStart = useStartSignatureReplay as ReturnType<typeof vi.fn>;

function setup(previewOverrides: Record<string, unknown> = {}, startOverrides: Record<string, unknown> = {}) {
  const previewMutate = vi.fn();
  const startMutateAsync = vi.fn();
  mockUsePreview.mockReturnValue({
    mutate: previewMutate,
    isPending: false,
    isError: false,
    data: undefined,
    ...previewOverrides,
  });
  mockUseStart.mockReturnValue({
    mutateAsync: startMutateAsync,
    isPending: false,
    ...startOverrides,
  });

  const onClose = vi.fn();
  const onJobStarted = vi.fn();
  render(
    <SignatureReplayPreviewModal
      namespaceId="ns-1"
      signatureHash="hash-1"
      onClose={onClose}
      onJobStarted={onJobStarted}
    />,
  );
  return { previewMutate, startMutateAsync, onClose, onJobStarted };
}

beforeEach(() => vi.clearAllMocks());

describe('SignatureReplayPreviewModal', () => {
  it('triggers the preview mutation with scope "all" on mount', () => {
    const { previewMutate } = setup();
    expect(previewMutate).toHaveBeenCalledWith({
      namespaceId: 'ns-1',
      signatureHash: 'hash-1',
      filter: { scope: 'all', from: undefined, to: undefined },
    });
  });

  it('re-triggers the preview when a different scope is selected', () => {
    const { previewMutate } = setup();
    previewMutate.mockClear();

    fireEvent.click(screen.getByLabelText('Only unresolved messages'));

    expect(previewMutate).toHaveBeenCalledWith({
      namespaceId: 'ns-1',
      signatureHash: 'hash-1',
      filter: { scope: 'unresolved', from: undefined, to: undefined },
    });
  });

  it('shows date pickers only for the date-range scope and skips preview until a date is chosen', () => {
    const { previewMutate } = setup();
    previewMutate.mockClear();

    fireEvent.click(screen.getByLabelText('Messages within a date range'));

    expect(screen.getByLabelText('From date')).toBeInTheDocument();
    expect(previewMutate).not.toHaveBeenCalled();
    expect(screen.getByText(/Pick at least one date/)).toBeInTheDocument();
  });

  it('renders the matched count and sample once loaded', () => {
    setup({
      data: {
        totalMatched: 3,
        sample: [
          { dlqMessageId: 1, messageId: 'msg-1', entityName: 'orders', deadLetterReason: 'MaxDeliveryCountExceeded', replaySafety: 'Safe' },
        ],
        canExecute: true,
        warnings: [],
        unsafeReplayCount: 0,
      },
    });

    expect(screen.getByText('3')).toBeInTheDocument();
    expect(screen.getByText('msg-1')).toBeInTheDocument();
  });

  it('disables the confirm button when canExecute is false', () => {
    setup({ data: { totalMatched: 0, sample: [], canExecute: false, warnings: ['No DLQ messages match this signature and filter.'], unsafeReplayCount: 0 } });

    const confirmButton = screen.getByRole('button', { name: /Replay 0 message/ });
    expect(confirmButton).toBeDisabled();
  });

  it('calls startReplay and onJobStarted when confirmed', async () => {
    const { startMutateAsync, onJobStarted } = setup({
      data: { totalMatched: 2, sample: [], canExecute: true, warnings: [], unsafeReplayCount: 0 },
    });
    startMutateAsync.mockResolvedValue({ id: 'job-123' });

    fireEvent.click(screen.getByRole('button', { name: /Replay 2 messages/ }));

    await waitFor(() => expect(onJobStarted).toHaveBeenCalledWith('job-123'));
    expect(startMutateAsync).toHaveBeenCalledWith({
      namespaceId: 'ns-1',
      signatureHash: 'hash-1',
      filter: { scope: 'all', from: undefined, to: undefined },
    });
  });

  it('calls onClose when Cancel is clicked', () => {
    const { onClose } = setup({ data: { totalMatched: 1, sample: [], canExecute: true, warnings: [], unsafeReplayCount: 0 } });

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(onClose).toHaveBeenCalled();
  });

  it('closes on Escape', () => {
    const { onClose } = setup({ data: { totalMatched: 1, sample: [], canExecute: true, warnings: [], unsafeReplayCount: 0 } });

    fireEvent.keyDown(window, { key: 'Escape' });

    expect(onClose).toHaveBeenCalled();
  });
});
