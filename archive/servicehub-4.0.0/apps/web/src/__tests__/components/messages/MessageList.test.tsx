import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MessageList } from '@/components/messages/MessageList';

// useVirtualizer doesn't render items in jsdom without proper scroll container setup;
// we test structural behavior at component API level

const futureTime = new Date(Date.now() + 2 * 60 * 60 * 1000).toISOString();

const mockMessages = [
  {
    id: 'msg-1',
    enqueuedTime: new Date('2024-01-01T10:00:00Z'),
    status: 'success' as const,
    preview: '{"eventType":"OrderCreated","orderId":"123"}',
    contentType: 'application/json' as const,
    deliveryCount: 1,
    hasAIInsight: false,
    sequenceNumber: 1,
    properties: {},
    queueType: 'active' as const,
    body: '{"eventType":"OrderCreated"}',
    headers: {},
    timeToLive: '',
    lockToken: '',
    eventType: 'OrderCreated',
    displayTitle: 'OrderCreated',
  },
  {
    id: 'msg-2',
    enqueuedTime: new Date('2024-01-01T09:00:00Z'),
    status: 'warning' as const,
    preview: '{"eventType":"PaymentFailed"}',
    contentType: 'application/json' as const,
    deliveryCount: 3,
    hasAIInsight: true,
    sequenceNumber: 2,
    properties: {},
    queueType: 'active' as const,
    body: '{"eventType":"PaymentFailed"}',
    headers: {},
    timeToLive: '',
    lockToken: '',
    eventType: 'PaymentFailed',
    displayTitle: 'PaymentFailed',
    scheduledEnqueueTime: futureTime,
  },
  {
    id: 'msg-3',
    enqueuedTime: new Date('2024-01-01T08:00:00Z'),
    status: 'error' as const,
    preview: '{"eventType":"OrderExpired"}',
    contentType: 'application/json' as const,
    deliveryCount: 10,
    hasAIInsight: false,
    sequenceNumber: 3,
    properties: {},
    queueType: 'deadletter' as const,
    body: '{"eventType":"OrderExpired"}',
    headers: {},
    timeToLive: '',
    lockToken: '',
    eventType: 'OrderExpired',
    displayTitle: 'OrderExpired',
    deadLetterReason: 'MaxDeliveryCountExceeded',
  },
];

const defaultProps = {
  messages: mockMessages,
  selectedId: null,
  onSelectMessage: vi.fn(),
  queueTab: 'active' as const,
  onQueueTabChange: vi.fn(),
  activeCounts: { active: 2, deadletter: 1 },
};

describe('MessageList', () => {
  it('renders Active tab with active count', () => {
    render(<MessageList {...defaultProps} />);
    expect(screen.getByText('Active (2)')).toBeInTheDocument();
  });

  it('renders Dead-Letter tab with dead-letter count', () => {
    render(<MessageList {...defaultProps} />);
    expect(screen.getByText(/Dead-Letter \(1\)/)).toBeInTheDocument();
  });

  it('calls onQueueTabChange when Dead-Letter tab is clicked', () => {
    const mockTabChange = vi.fn();
    render(<MessageList {...defaultProps} onQueueTabChange={mockTabChange} />);
    fireEvent.click(screen.getByText(/Dead-Letter/));
    expect(mockTabChange).toHaveBeenCalledWith('deadletter');
  });

  it('calls onQueueTabChange when Active tab is clicked', () => {
    const mockTabChange = vi.fn();
    render(<MessageList {...defaultProps} queueTab="deadletter" onQueueTabChange={mockTabChange} />);
    fireEvent.click(screen.getByText(/Active \(2\)/));
    expect(mockTabChange).toHaveBeenCalledWith('active');
  });

  it('shows empty state for active tab when no active messages', () => {
    const emptyProps = {
      ...defaultProps,
      messages: mockMessages.filter(m => m.queueType === 'deadletter'), // only DLQ
      activeCounts: { active: 0, deadletter: 1 },
    };
    render(<MessageList {...emptyProps} />);
    expect(screen.getByText('No messages')).toBeInTheDocument();
    expect(screen.getByText(/Active queue is empty/)).toBeInTheDocument();
  });

  it('shows empty state for dead-letter tab when no DLQ messages', () => {
    render(
      <MessageList
        {...defaultProps}
        messages={mockMessages.filter(m => m.queueType === 'active')}
        queueTab="deadletter"
        activeCounts={{ active: 2, deadletter: 0 }}
      />
    );
    expect(screen.getByText('No messages')).toBeInTheDocument();
    expect(screen.getByText(/Dead-letter queue is empty/)).toBeInTheDocument();
  });

  it('shows DLQ indicator dot when dead-letter count > 0', () => {
    render(<MessageList {...defaultProps} />);
    const dlqDot = document.querySelector('.bg-red-500.rounded-full');
    expect(dlqDot).not.toBeNull();
  });

  it('does not show DLQ indicator dot when dead-letter count is 0', () => {
    render(
      <MessageList
        {...defaultProps}
        activeCounts={{ active: 2, deadletter: 0 }}
      />
    );
    const dlqDots = document.querySelectorAll('.bg-red-500.rounded-full');
    expect(dlqDots).toHaveLength(0);
  });

  it('does not render "Scheduled" badge for messages without scheduledEnqueueTime', () => {
    // Only msg-1 (no scheduledEnqueueTime) in the active list
    const propsWithoutScheduled = {
      ...defaultProps,
      messages: [mockMessages[0]],
      activeCounts: { active: 1, deadletter: 0 },
    };
    render(<MessageList {...propsWithoutScheduled} />);
    expect(screen.queryByText('Scheduled')).not.toBeInTheDocument();
  });

  it('navigates messages with j/k keyboard when list has items', () => {
    const onSelect = vi.fn();
    render(
      <MessageList
        {...defaultProps}
        selectedId="msg-1"
        onSelectMessage={onSelect}
      />
    );
    // 'j' moves to next message
    fireEvent.keyDown(window, { key: 'j' });
    expect(onSelect).toHaveBeenCalledWith('msg-2');
  });

  it('navigates messages with ArrowDown keyboard', () => {
    const onSelect = vi.fn();
    render(
      <MessageList
        {...defaultProps}
        selectedId="msg-1"
        onSelectMessage={onSelect}
      />
    );
    fireEvent.keyDown(window, { key: 'ArrowDown' });
    expect(onSelect).toHaveBeenCalledWith('msg-2');
  });

  it('navigates messages with k/ArrowUp keyboard', () => {
    const onSelect = vi.fn();
    render(
      <MessageList
        {...defaultProps}
        selectedId="msg-2"
        onSelectMessage={onSelect}
      />
    );
    fireEvent.keyDown(window, { key: 'k' });
    expect(onSelect).toHaveBeenCalledWith('msg-1');
  });

  it('does not navigate past the first message with k', () => {
    const onSelect = vi.fn();
    render(
      <MessageList
        {...defaultProps}
        selectedId="msg-1"
        onSelectMessage={onSelect}
      />
    );
    fireEvent.keyDown(window, { key: 'k' });
    // already at first item, should not call
    expect(onSelect).not.toHaveBeenCalled();
  });

  it('renders large (localized) counts in tabs', () => {
    render(
      <MessageList
        {...defaultProps}
        activeCounts={{ active: 1000, deadletter: 500 }}
      />
    );
    // toLocaleString may format as "1,000" in en-US or "1000" depending on locale
    expect(screen.getByText(/1[\s,.]?000/)).toBeInTheDocument();
  });
});

