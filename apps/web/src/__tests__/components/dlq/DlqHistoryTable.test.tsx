import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { DlqHistoryTable } from '@/components/dlq/DlqHistoryTable';

const mockItems = [
  {
    id: 1,
    messageId: 'msg-aaa-111',
    entityName: 'orders-queue',
    status: 'Active',
    failureCategory: 'MaxDelivery',
    categoryConfidence: 0.9,
    replaySafety: 'Safe',
    forensicConfidence: 0.85,
    deadLetterReason: 'MaxDeliveryCountExceeded',
    deliveryCount: 10,
    detectedAtUtc: new Date(Date.now() - 30 * 60 * 1000).toISOString(), // 30 min ago
    firstSeenAt: '2024-01-01T10:00:00Z',
    lastSeenAt: '2024-01-01T12:00:00Z',
  },
  {
    id: 2,
    messageId: 'msg-bbb-222',
    entityName: 'payments-queue',
    status: 'Replayed',
    failureCategory: 'Unknown',
    categoryConfidence: 0,
    replaySafety: 'Unsafe',
    forensicConfidence: 0,
    deadLetterReason: 'Processing error - timeout exceeded',
    deliveryCount: 3,
    detectedAtUtc: new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString(), // 2 hours ago
    firstSeenAt: '2024-01-01T08:00:00Z',
    lastSeenAt: '2024-01-01T09:00:00Z',
  },
];

const defaultProps = {
  items: mockItems as any,
  totalCount: 2,
  page: 1,
  pageSize: 50,
  hasNextPage: false,
  hasPreviousPage: false,
  isLoading: false,
  onPageChange: vi.fn(),
  onViewTimeline: vi.fn(),
};

describe('DlqHistoryTable', () => {
  it('renders table headers', () => {
    render(<DlqHistoryTable {...defaultProps} />);
    expect(screen.getByText('Entity')).toBeInTheDocument();
    expect(screen.getByText('Status')).toBeInTheDocument();
    expect(screen.getByText('Category')).toBeInTheDocument();
    expect(screen.getByText('DLQ Reason')).toBeInTheDocument();
    expect(screen.getByText('Detected')).toBeInTheDocument();
    expect(screen.getByText('Actions')).toBeInTheDocument();
  });

  it('renders entity names for items', () => {
    render(<DlqHistoryTable {...defaultProps} />);
    expect(screen.getByText('orders-queue')).toBeInTheDocument();
    expect(screen.getByText('payments-queue')).toBeInTheDocument();
  });

  it('renders message IDs', () => {
    render(<DlqHistoryTable {...defaultProps} />);
    expect(screen.getByText('msg-aaa-111')).toBeInTheDocument();
    expect(screen.getByText('msg-bbb-222')).toBeInTheDocument();
  });

  it('renders dead letter reasons', () => {
    render(<DlqHistoryTable {...defaultProps} />);
    expect(screen.getByText('MaxDeliveryCountExceeded')).toBeInTheDocument();
  });

  it('renders delivery counts', () => {
    render(<DlqHistoryTable {...defaultProps} />);
    expect(screen.getByText('10')).toBeInTheDocument();
    expect(screen.getByText('3')).toBeInTheDocument();
  });

  it('renders replay safety badges', () => {
    render(<DlqHistoryTable {...defaultProps} />);
    expect(screen.getByText('Safe')).toBeInTheDocument();
    expect(screen.getByText('Unsafe')).toBeInTheDocument();
  });

  it('renders forensic confidence as percentage', () => {
    render(<DlqHistoryTable {...defaultProps} />);
    expect(screen.getByText('85%')).toBeInTheDocument();
  });

  it('renders dash for zero forensic confidence', () => {
    render(<DlqHistoryTable {...defaultProps} />);
    // Item 2 has forensicConfidence 0 so should render "—"
    const dashes = screen.getAllByText('—');
    expect(dashes.length).toBeGreaterThan(0);
  });

  it('renders pagination info', () => {
    render(<DlqHistoryTable {...defaultProps} />);
    // Pagination area shows "Showing X to Y of Z messages"  
    expect(screen.getByText(/Showing/)).toBeInTheDocument();
    expect(screen.getByText(/messages/)).toBeInTheDocument();
  });

  it('renders page navigation', () => {
    render(<DlqHistoryTable {...defaultProps} />);
    expect(screen.getByText(/Page 1 of 1/)).toBeInTheDocument();
  });

  it('calls onViewTimeline when row is clicked', () => {
    const mockViewTimeline = vi.fn();
    render(<DlqHistoryTable {...defaultProps} onViewTimeline={mockViewTimeline} />);
    fireEvent.click(screen.getByText('orders-queue'));
    expect(mockViewTimeline).toHaveBeenCalledWith(1);
  });

  it('calls onViewTimeline when Eye button is clicked', () => {
    const mockViewTimeline = vi.fn();
    render(<DlqHistoryTable {...defaultProps} onViewTimeline={mockViewTimeline} />);
    const viewButtons = screen.getAllByTitle('View timeline');
    fireEvent.click(viewButtons[0]);
    expect(mockViewTimeline).toHaveBeenCalledWith(1);
  });

  it('calls onPageChange with prev page when Previous button is clicked', () => {
    const mockPageChange = vi.fn();
    render(
      <DlqHistoryTable
        {...defaultProps}
        page={2}
        hasPreviousPage={true}
        totalCount={100}
        onPageChange={mockPageChange}
      />
    );
    fireEvent.click(screen.getByLabelText('Previous page'));
    expect(mockPageChange).toHaveBeenCalledWith(1);
  });

  it('calls onPageChange with next page when Next button is clicked', () => {
    const mockPageChange = vi.fn();
    render(
      <DlqHistoryTable
        {...defaultProps}
        hasNextPage={true}
        totalCount={100}
        onPageChange={mockPageChange}
      />
    );
    fireEvent.click(screen.getByLabelText('Next page'));
    expect(mockPageChange).toHaveBeenCalledWith(2);
  });

  it('disables Previous button on first page', () => {
    render(<DlqHistoryTable {...defaultProps} hasPreviousPage={false} />);
    expect(screen.getByLabelText('Previous page')).toBeDisabled();
  });

  it('disables Next button on last page', () => {
    render(<DlqHistoryTable {...defaultProps} hasNextPage={false} />);
    expect(screen.getByLabelText('Next page')).toBeDisabled();
  });

  it('shows loading state', () => {
    const { container } = render(<DlqHistoryTable {...defaultProps} isLoading={true} />);
    expect(container.querySelector('.animate-pulse')).not.toBeNull();
    // Skeleton: no actual data rows visible
    expect(screen.queryByText('No DLQ messages found')).not.toBeInTheDocument();
  });

  it('shows empty state when items is empty', () => {
    render(<DlqHistoryTable {...defaultProps} items={[]} totalCount={0} />);
    expect(screen.getByText('No DLQ messages found')).toBeInTheDocument();
  });

  it('uses high delivery count style (red) for 10+ deliveries', () => {
    render(<DlqHistoryTable {...defaultProps} />);
    // Item 1 has deliveryCount 10
    const countEl = screen.getByText('10');
    expect(countEl.className).toContain('text-red-600');
  });
});
