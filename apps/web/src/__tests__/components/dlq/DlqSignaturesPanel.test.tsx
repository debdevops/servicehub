import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { DlqSignaturesPanel } from '@/components/dlq/DlqSignaturesPanel';
import type { DlqSignaturesResponse } from '@servicehub/ui-shared/lib/api/dlqSignatures';

vi.mock('@servicehub/ui-shared/hooks/useDlqSignatures', () => ({
  useDlqSignatures: vi.fn(),
}));

import { useDlqSignatures } from '@servicehub/ui-shared/hooks/useDlqSignatures';

const mockUseDlqSignatures = useDlqSignatures as ReturnType<typeof vi.fn>;

function makeResponse(overrides: Partial<DlqSignaturesResponse> = {}): DlqSignaturesResponse {
  return {
    available: true,
    method: 'clustered',
    batchSize: 5,
    clusters: [
      {
        size: 4,
        messageIds: [1, 2, 3, 4],
        dominantEntity: 'orders-queue',
        dominantDeadletterReason: 'MaxDeliveryCountExceeded',
        dominantDeadletterReasonCount: 4,
        topTerms: ['timeout'],
        isNew: true,
        firstSeenAt: '2026-01-01T00:00:00Z',
        occurrenceCount: 1,
        windowStart: '2026-01-01T00:00:00Z',
        windowEnd: '2026-01-01T01:00:00Z',
        explanation: '4 messages: max delivery count exceeded on orders-queue.',
      },
    ],
    singletons: [{ messageId: 5, dominantEntity: 'orders-queue', dominantDeadletterReason: 'TTLExpiredException' }],
    ...overrides,
  };
}

describe('DlqSignaturesPanel', () => {
  it('renders nothing while loading', () => {
    mockUseDlqSignatures.mockReturnValue({ data: undefined, loading: true, error: null, available: false });
    const { container } = render(<DlqSignaturesPanel namespaceId="ns-1" />);
    expect(container).toBeEmptyDOMElement();
  });

  it('renders nothing when AI is unavailable', () => {
    mockUseDlqSignatures.mockReturnValue({
      data: makeResponse({ available: false, clusters: [], singletons: [] }),
      loading: false,
      error: null,
      available: false,
    });
    const { container } = render(<DlqSignaturesPanel namespaceId="ns-1" />);
    expect(container).toBeEmptyDOMElement();
  });

  it('renders nothing when there are no clusters', () => {
    mockUseDlqSignatures.mockReturnValue({
      data: makeResponse({ clusters: [] }),
      loading: false,
      error: null,
      available: true,
    });
    const { container } = render(<DlqSignaturesPanel namespaceId="ns-1" />);
    expect(container).toBeEmptyDOMElement();
  });

  it('renders cluster cards, singleton count, and filters on click', () => {
    mockUseDlqSignatures.mockReturnValue({ data: makeResponse(), loading: false, error: null, available: true });
    const onFilterEntity = vi.fn();
    render(<DlqSignaturesPanel namespaceId="ns-1" onFilterEntity={onFilterEntity} />);

    expect(screen.getByText('Recurring Failure Signatures')).toBeInTheDocument();
    expect(screen.getByText(/4 messages: max delivery count exceeded/)).toBeInTheDocument();
    expect(screen.getByText(/1 additional one-off failure/)).toBeInTheDocument();

    fireEvent.click(screen.getByText('Filter table to orders-queue →'));
    expect(onFilterEntity).toHaveBeenCalledWith('orders-queue');
  });
});
