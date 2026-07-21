import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { LiveTailPanel } from '@/components/messages/LiveTailPanel';

vi.mock('@/hooks/useLiveTail', () => ({
  useLiveTail: vi.fn(),
}));

import { useLiveTail } from '@/hooks/useLiveTail';

const mockUseLiveTail = useLiveTail as ReturnType<typeof vi.fn>;

function buildMessage(overrides: Record<string, unknown> = {}) {
  return {
    messageId: 'm1',
    sequenceNumber: 1,
    enqueuedTime: new Date().toISOString(),
    deliveryCount: 1,
    state: 'Active',
    contentType: 'application/json',
    body: '{"hello":"world"}',
    ...overrides,
  };
}

describe('LiveTailPanel', () => {
  const start = vi.fn();
  const stop = vi.fn();
  const clear = vi.fn();

  beforeEach(() => {
    start.mockClear();
    stop.mockClear();
    clear.mockClear();
    mockUseLiveTail.mockReturnValue({ status: 'connecting', messages: [], start, stop, clear });
  });

  it('renders nothing when closed', () => {
    const { container } = render(
      <LiveTailPanel isOpen={false} onClose={vi.fn()} namespaceId="ns-1" entityName="orders" />,
    );

    expect(container).toBeEmptyDOMElement();
    expect(start).not.toHaveBeenCalled();
  });

  it('starts the session when opened', () => {
    render(<LiveTailPanel isOpen={true} onClose={vi.fn()} namespaceId="ns-1" entityName="orders" />);

    expect(start).toHaveBeenCalledTimes(1);
  });

  it('shows the entity name, including the topic/subscription path', () => {
    render(
      <LiveTailPanel
        isOpen={true}
        onClose={vi.fn()}
        namespaceId="ns-1"
        entityName="orders-topic"
        subscriptionName="orders-sub"
      />,
    );

    expect(screen.getByText('orders-topic / orders-sub')).toBeInTheDocument();
  });

  it('renders newly-arrived messages', () => {
    mockUseLiveTail.mockReturnValue({
      status: 'connected',
      messages: [buildMessage({ messageId: 'm1' }), buildMessage({ messageId: 'm2', deliveryCount: 3 })],
      start,
      stop,
      clear,
    });

    render(<LiveTailPanel isOpen={true} onClose={vi.fn()} namespaceId="ns-1" entityName="orders" />);

    expect(screen.getByText('m1')).toBeInTheDocument();
    expect(screen.getByText('m2')).toBeInTheDocument();
    expect(screen.getByText((_, el) => el?.textContent === 'Delivery #3')).toBeInTheDocument();
    expect(screen.getByText((_, el) => el?.textContent === '· 2 messages')).toBeInTheDocument();
  });

  it('shows the unsupported message and disables the pause/resume control', () => {
    mockUseLiveTail.mockReturnValue({ status: 'unsupported', messages: [], start, stop, clear });

    render(<LiveTailPanel isOpen={true} onClose={vi.fn()} namespaceId="ns-1" entityName="checkout-queue" />);

    expect(screen.getByText(/Live Tail isn't available/)).toBeInTheDocument();
    expect(screen.getByLabelText('Resume Live Tail')).toBeDisabled();
  });

  it('clicking the pause control stops the session while connected', () => {
    mockUseLiveTail.mockReturnValue({ status: 'connected', messages: [], start, stop, clear });

    render(<LiveTailPanel isOpen={true} onClose={vi.fn()} namespaceId="ns-1" entityName="orders" />);
    fireEvent.click(screen.getByLabelText('Pause Live Tail'));

    expect(stop).toHaveBeenCalledTimes(1);
  });

  it('clicking clear invokes clear()', () => {
    mockUseLiveTail.mockReturnValue({
      status: 'connected',
      messages: [buildMessage()],
      start,
      stop,
      clear,
    });

    render(<LiveTailPanel isOpen={true} onClose={vi.fn()} namespaceId="ns-1" entityName="orders" />);
    fireEvent.click(screen.getByLabelText('Clear messages'));

    expect(clear).toHaveBeenCalledTimes(1);
  });

  it('calls onClose when the close button is clicked', () => {
    const onClose = vi.fn();
    render(<LiveTailPanel isOpen={true} onClose={onClose} namespaceId="ns-1" entityName="orders" />);

    fireEvent.click(screen.getByLabelText('Close Live Tail'));

    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('stops the session when isOpen transitions to false', () => {
    const { rerender } = render(
      <LiveTailPanel isOpen={true} onClose={vi.fn()} namespaceId="ns-1" entityName="orders" />,
    );

    rerender(<LiveTailPanel isOpen={false} onClose={vi.fn()} namespaceId="ns-1" entityName="orders" />);

    expect(stop).toHaveBeenCalledTimes(1);
  });
});
