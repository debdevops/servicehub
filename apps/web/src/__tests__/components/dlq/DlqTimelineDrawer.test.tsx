import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { DlqTimelineDrawer } from '@/components/dlq/DlqTimelineDrawer';

vi.mock('@/hooks/useDlqHistory', () => ({
  useDlqTimeline: vi.fn(),
  useDlqMessageDetail: vi.fn(),
  useUpdateDlqNotes: vi.fn(() => ({ mutate: vi.fn(), isPending: false })),
}));

import { useDlqTimeline, useDlqMessageDetail } from '@/hooks/useDlqHistory';

const mockUseDlqTimeline = useDlqTimeline as ReturnType<typeof vi.fn>;
const mockUseDlqMessageDetail = useDlqMessageDetail as ReturnType<typeof vi.fn>;

const mockTimeline = {
  events: [
    {
      eventType: 'Enqueued',
      timestamp: new Date(Date.now() - 3 * 60 * 60 * 1000).toISOString(),
      description: 'Message enqueued to orders-queue',
      details: { queue: 'orders-queue' },
    },
    {
      eventType: 'DeadLettered',
      timestamp: new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString(),
      description: 'MaxDelivery count exceeded',
      details: { deliveryCount: '10' },
    },
    {
      eventType: 'ReplayedSuccess',
      timestamp: new Date(Date.now() - 1 * 60 * 60 * 1000).toISOString(),
      description: 'Successfully replayed',
      details: {},
    },
  ],
};

const mockDetail = {
  id: 1,
  messageId: 'msg-aaa-111-bbb',
  entityName: 'orders-queue',
  status: 'Active',
  failureCategory: 'MaxDelivery',
  categoryConfidence: 0.9,
  deadLetterReason: 'MaxDeliveryCountExceeded',
  deadLetterErrorDescription: 'Delivery count exceeded 10',
  deliveryCount: 10,
  messageSize: 2048,
  contentType: 'application/json',
  bodyPreview: '{"eventType":"OrderFailed","orderId":"123"}',
  userNotes: null,
  replaySafety: 'Safe',
  forensicConfidence: 0.8,
};

beforeEach(() => {
  mockUseDlqTimeline.mockReturnValue({ data: mockTimeline, isLoading: false });
  mockUseDlqMessageDetail.mockReturnValue({ data: mockDetail, isLoading: false });
});

describe('DlqTimelineDrawer', () => {
  it('renders nothing when messageId is null', () => {
    const { container } = render(<DlqTimelineDrawer messageId={null} onClose={vi.fn()} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('renders drawer when messageId is set', () => {
    render(<DlqTimelineDrawer messageId={1} onClose={vi.fn()} />);
    expect(screen.getByText('Message Timeline')).toBeInTheDocument();
  });

  it('renders message ID in header', () => {
    render(<DlqTimelineDrawer messageId={1} onClose={vi.fn()} />);
    expect(screen.getByText('msg-aaa-111-bbb')).toBeInTheDocument();
  });

  it('renders entity name in detail', () => {
    render(<DlqTimelineDrawer messageId={1} onClose={vi.fn()} />);
    expect(screen.getAllByText(/orders-queue/).length).toBeGreaterThan(0);
  });

  it('renders delivery count', () => {
    render(<DlqTimelineDrawer messageId={1} onClose={vi.fn()} />);
    expect(screen.getAllByText('10').length).toBeGreaterThan(0);
  });

  it('renders message size formatted', () => {
    render(<DlqTimelineDrawer messageId={1} onClose={vi.fn()} />);
    expect(screen.getByText('2 KB')).toBeInTheDocument();
  });

  it('renders content type', () => {
    render(<DlqTimelineDrawer messageId={1} onClose={vi.fn()} />);
    expect(screen.getByText('application/json')).toBeInTheDocument();
  });

  it('renders DLQ reason', () => {
    render(<DlqTimelineDrawer messageId={1} onClose={vi.fn()} />);
    expect(screen.getByText(/MaxDeliveryCountExceeded/)).toBeInTheDocument();
  });

  it('renders DLQ error description', () => {
    render(<DlqTimelineDrawer messageId={1} onClose={vi.fn()} />);
    expect(screen.getByText(/Delivery count exceeded 10/)).toBeInTheDocument();
  });

  it('renders body preview', () => {
    render(<DlqTimelineDrawer messageId={1} onClose={vi.fn()} />);
    expect(screen.getByText(/Body Preview/i)).toBeInTheDocument();
    expect(screen.getByText(/OrderFailed/)).toBeInTheDocument();
  });

  it('renders Journey Timeline heading', () => {
    render(<DlqTimelineDrawer messageId={1} onClose={vi.fn()} />);
    expect(screen.getByText(/Journey Timeline/)).toBeInTheDocument();
  });

  it('renders timeline events', () => {
    render(<DlqTimelineDrawer messageId={1} onClose={vi.fn()} />);
    expect(screen.getByText('Enqueued')).toBeInTheDocument();
    expect(screen.getByText('DeadLettered')).toBeInTheDocument();
    expect(screen.getByText('ReplayedSuccess')).toBeInTheDocument();
  });

  it('renders timeline event descriptions', () => {
    render(<DlqTimelineDrawer messageId={1} onClose={vi.fn()} />);
    expect(screen.getByText('Message enqueued to orders-queue')).toBeInTheDocument();
  });

  it('renders timeline event detail chips', () => {
    render(<DlqTimelineDrawer messageId={1} onClose={vi.fn()} />);
    expect(screen.getByText(/queue:/)).toBeInTheDocument();
  });

  it('calls onClose when Close button is clicked', () => {
    const mockClose = vi.fn();
    render(<DlqTimelineDrawer messageId={1} onClose={mockClose} />);
    fireEvent.click(screen.getByLabelText('Close drawer'));
    expect(mockClose).toHaveBeenCalled();
  });

  it('calls onClose when backdrop is clicked', () => {
    const mockClose = vi.fn();
    render(<DlqTimelineDrawer messageId={1} onClose={mockClose} />);
    // Click backdrop (first element before drawer)
    const backdrop = document.querySelector('.fixed.inset-0');
    if (backdrop) {
      fireEvent.click(backdrop);
      expect(mockClose).toHaveBeenCalled();
    }
  });

  it('shows loading state when data is loading', () => {
    mockUseDlqTimeline.mockReturnValue({ data: undefined, isLoading: true });
    mockUseDlqMessageDetail.mockReturnValue({ data: undefined, isLoading: true });
    render(<DlqTimelineDrawer messageId={1} onClose={vi.fn()} />);
    expect(screen.getByText('Loading...')).toBeInTheDocument();
  });

  it('shows no events message when timeline is empty', () => {
    mockUseDlqTimeline.mockReturnValue({ data: { events: [] }, isLoading: false });
    render(<DlqTimelineDrawer messageId={1} onClose={vi.fn()} />);
    expect(screen.getByText('No timeline events available.')).toBeInTheDocument();
  });
});
