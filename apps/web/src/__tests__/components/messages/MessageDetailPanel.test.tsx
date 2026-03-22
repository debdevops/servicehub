import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MessageDetailPanel } from '@/components/messages/MessageDetailPanel';

vi.mock('@/hooks/useTabPersistence', () => ({
  useTabPersistence: vi.fn(() => ['properties', vi.fn()]),
}));
vi.mock('@/hooks/useMessages', () => ({
  useReplayMessage: vi.fn(),
}));
vi.mock('@/components/messages/tabs', () => ({
  PropertiesTab: ({ message }: { message: any }) => (
    <div data-testid="properties-tab">Properties: {message.id}</div>
  ),
  BodyTab: () => <div data-testid="body-tab">Body Content</div>,
  AIInsightsTab: () => <div data-testid="ai-insights-tab">AI Insights</div>,
  ForensicTab: () => <div data-testid="forensic-tab">Forensic</div>,
  HeadersTab: () => <div data-testid="headers-tab">Headers</div>,
}));
vi.mock('@/components/ConfirmDialog', () => ({
  ConfirmDialog: ({ isOpen, title, onConfirm, onCancel }: any) =>
    isOpen ? (
      <div data-testid="confirm-dialog">
        <span>{title}</span>
        <button onClick={onConfirm}>Confirm</button>
        <button onClick={onCancel}>Cancel</button>
      </div>
    ) : null,
}));
vi.mock('react-hot-toast', () => ({
  default: { success: vi.fn(), error: vi.fn() },
}));

import { useTabPersistence } from '@/hooks/useTabPersistence';
import { useReplayMessage } from '@/hooks/useMessages';

const mockUseTabPersistence = useTabPersistence as ReturnType<typeof vi.fn>;
const mockUseReplayMessage = useReplayMessage as ReturnType<typeof vi.fn>;

const mockMessage = {
  id: 'msg-aaa-bbb-111',
  enqueuedTime: new Date(),
  status: 'error' as const,
  preview: '{"eventType":"OrderFailed"}',
  contentType: 'application/json' as const,
  deliveryCount: 3,
  hasAIInsight: false,
  sequenceNumber: 42,
  properties: {},
  queueType: 'deadletter' as const,
  body: '{"eventType":"OrderFailed","orderId":"order-123"}',
  headers: { 'Content-Type': 'application/json' },
  timeToLive: '',
  lockToken: '',
  deadLetterReason: 'MaxDeliveryCountExceeded',
  deadLetterSource: undefined,
};

const mockActiveMessage = {
  ...mockMessage,
  id: 'msg-active-001',
  queueType: 'active' as const,
  deadLetterReason: undefined,
  body: '{"eventType":"PaymentProcessed","transactionId":"txn-456"}',
  deliveryCount: 1,
  status: 'success' as const,
};

function createWrapper(path = '/messages?namespace=ns1&queue=test-queue') {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: React.ReactNode }) => (
    <MemoryRouter initialEntries={[path]}>
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    </MemoryRouter>
  );
}

beforeEach(() => {
  mockUseTabPersistence.mockReturnValue(['properties', vi.fn()]);
  mockUseReplayMessage.mockReturnValue({ mutateAsync: vi.fn(), isPending: false });
});

describe('MessageDetailPanel', () => {
  it('renders empty state when message is null', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessageDetailPanel message={null} /></Wrapper>);
    expect(screen.getByText('No Message Selected')).toBeInTheDocument();
  });

  it('renders message title from eventType', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessageDetailPanel message={mockMessage} /></Wrapper>);
    expect(screen.getByText('Order Failed')).toBeInTheDocument();
  });

  it('renders message subtitle with order ID', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessageDetailPanel message={mockMessage} /></Wrapper>);
    expect(screen.getByText(/order-123/)).toBeInTheDocument();
  });

  it('renders DLQ warning badge for dead-letter message', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessageDetailPanel message={mockMessage} /></Wrapper>);
    expect(screen.getByText(/ServiceHub Assessment/)).toBeInTheDocument();
  });

  it('shows dead letter reason', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessageDetailPanel message={mockMessage} /></Wrapper>);
    expect(screen.getByText('MaxDeliveryCountExceeded')).toBeInTheDocument();
  });

  it('renders all tab buttons', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessageDetailPanel message={mockMessage} /></Wrapper>);
    expect(screen.getByText('Properties')).toBeInTheDocument();
    expect(screen.getByText('Body')).toBeInTheDocument();
    expect(screen.getByText('AI Insights')).toBeInTheDocument();
    expect(screen.getByText('Forensic')).toBeInTheDocument();
    expect(screen.getByText('Headers')).toBeInTheDocument();
  });

  it('renders active tab content (properties by default)', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessageDetailPanel message={mockMessage} /></Wrapper>);
    expect(screen.getByTestId('properties-tab')).toBeInTheDocument();
  });

  it('switches tab when tab button is clicked', () => {
    const mockSetTab = vi.fn();
    mockUseTabPersistence.mockReturnValue(['properties', mockSetTab]);
    const Wrapper = createWrapper();
    render(<Wrapper><MessageDetailPanel message={mockMessage} /></Wrapper>);
    fireEvent.click(screen.getByText('Body'));
    expect(mockSetTab).toHaveBeenCalledWith('body');
  });

  it('renders Replay button', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessageDetailPanel message={mockMessage} /></Wrapper>);
    expect(screen.getByText('Replay')).toBeInTheDocument();
  });

  it('Replay button is enabled for dead-letter messages', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessageDetailPanel message={mockMessage} /></Wrapper>);
    const replayBtn = screen.getByLabelText('Replay message');
    expect(replayBtn).not.toBeDisabled();
  });

  it('Replay button is disabled for active messages', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessageDetailPanel message={mockActiveMessage} /></Wrapper>);
    const replayBtn = screen.getByLabelText('Replay message');
    expect(replayBtn).toBeDisabled();
  });

  it('shows "Active messages cannot be replayed" for active messages', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessageDetailPanel message={mockActiveMessage} /></Wrapper>);
    expect(screen.getByText('Active messages cannot be replayed')).toBeInTheDocument();
  });

  it('renders Copy ID button', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessageDetailPanel message={mockMessage} /></Wrapper>);
    expect(screen.getByText('Copy ID')).toBeInTheDocument();
  });

  it('opens confirm dialog when Replay is clicked', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessageDetailPanel message={mockMessage} /></Wrapper>);
    fireEvent.click(screen.getByLabelText('Replay message'));
    expect(screen.getByTestId('confirm-dialog')).toBeInTheDocument();
    expect(screen.getByText('Replay Message')).toBeInTheDocument();
  });

  it('closes confirm dialog when Cancel is clicked', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessageDetailPanel message={mockMessage} /></Wrapper>);
    fireEvent.click(screen.getByLabelText('Replay message'));
    expect(screen.getByTestId('confirm-dialog')).toBeInTheDocument();
    fireEvent.click(screen.getByText('Cancel'));
    expect(screen.queryByTestId('confirm-dialog')).not.toBeInTheDocument();
  });

  it('renders payment transaction title from transactionId', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><MessageDetailPanel message={mockActiveMessage} /></Wrapper>);
    expect(screen.getByText('Payment Processed')).toBeInTheDocument();
  });

  it('shows body tab content when Body tab is active', () => {
    mockUseTabPersistence.mockReturnValue(['body', vi.fn()]);
    const Wrapper = createWrapper();
    render(<Wrapper><MessageDetailPanel message={mockMessage} /></Wrapper>);
    expect(screen.getByTestId('body-tab')).toBeInTheDocument();
  });

  it('shows AI tab content when AI Insights tab is active', () => {
    mockUseTabPersistence.mockReturnValue(['ai', vi.fn()]);
    const Wrapper = createWrapper();
    render(<Wrapper><MessageDetailPanel message={mockMessage} /></Wrapper>);
    expect(screen.getByTestId('ai-insights-tab')).toBeInTheDocument();
  });

  it('shows forensic tab content when Forensic tab is active', () => {
    mockUseTabPersistence.mockReturnValue(['forensic', vi.fn()]);
    const Wrapper = createWrapper();
    render(<Wrapper><MessageDetailPanel message={mockMessage} /></Wrapper>);
    expect(screen.getByTestId('forensic-tab')).toBeInTheDocument();
  });

  it('shows headers tab content when Headers tab is active', () => {
    mockUseTabPersistence.mockReturnValue(['headers', vi.fn()]);
    const Wrapper = createWrapper();
    render(<Wrapper><MessageDetailPanel message={mockMessage} /></Wrapper>);
    expect(screen.getByTestId('headers-tab')).toBeInTheDocument();
  });

  it('renders critical DLQ badge when delivery count is high', () => {
    const criticalMessage = { ...mockMessage, deliveryCount: 10 };
    const Wrapper = createWrapper();
    render(<Wrapper><MessageDetailPanel message={criticalMessage} /></Wrapper>);
    expect(screen.getByText(/Critical/)).toBeInTheDocument();
  });

  it('renders message ID as fallback title when body is non-JSON', () => {
    const plainMessage = { ...mockMessage, body: 'plain text body' };
    const Wrapper = createWrapper();
    render(<Wrapper><MessageDetailPanel message={plainMessage} /></Wrapper>);
    // Title should fallback to 'Message' with ID subtitle
    expect(screen.getByText('Message')).toBeInTheDocument();
  });
});
