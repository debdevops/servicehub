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
  usePurgeMessage: vi.fn(),
}));
vi.mock('@/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));
vi.mock('@/hooks/useCloudBridge', () => ({
  useProviderCapabilities: vi.fn(),
}));
vi.mock('@/components/messages/tabs', () => ({
  PropertiesTab: ({ message }: { message: any }) => (
    <div data-testid="properties-tab">Properties: {message.id}</div>
  ),
  BodyTab: () => <div data-testid="body-tab">Body Content</div>,
  AIInsightsTab: () => <div data-testid="ai-insights-tab">AI Insights</div>,
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
import { useReplayMessage, usePurgeMessage } from '@/hooks/useMessages';
import { useNamespaces } from '@/hooks/useNamespaces';
import { useProviderCapabilities } from '@/hooks/useCloudBridge';

const mockUseTabPersistence = useTabPersistence as ReturnType<typeof vi.fn>;
const mockUseReplayMessage = useReplayMessage as ReturnType<typeof vi.fn>;
const mockUsePurgeMessage = usePurgeMessage as ReturnType<typeof vi.fn>;
const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;
const mockUseProviderCapabilities = useProviderCapabilities as ReturnType<typeof vi.fn>;

const mockCapabilitiesMap = {
  Azure: {
    supportsMessageCounts: true,
    supportsManualDeadLetter: true,
    supportsPurge: false,
    supportsScheduledMessages: true,
    supportsRepeatablePeek: true,
    notes: 'Purge is not supported — the SDK has no reliable single-message delete by sequence number.',
  },
  Aws: {
    supportsMessageCounts: true,
    supportsManualDeadLetter: true,
    supportsPurge: true,
    supportsScheduledMessages: false,
    supportsRepeatablePeek: false,
    notes: 'Scheduled messages are not supported.',
  },
  Gcp: {
    supportsMessageCounts: false,
    supportsManualDeadLetter: false,
    supportsPurge: true,
    supportsScheduledMessages: false,
    supportsRepeatablePeek: true,
    notes: 'Message counts and manual dead-lettering are not supported.',
  },
};

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

function makeNamespace(cloudProvider: 'azure' | 'aws' | 'gcp') {
  return {
    id: 'ns1',
    name: 'test-ns',
    environment: 'dev',
    hasSendPermission: true,
    cloudProvider,
  };
}

beforeEach(() => {
  mockUseTabPersistence.mockReturnValue(['properties', vi.fn()]);
  mockUseReplayMessage.mockReturnValue({ mutateAsync: vi.fn(), isPending: false });
  mockUsePurgeMessage.mockReturnValue({ mutateAsync: vi.fn(), isPending: false });
  mockUseNamespaces.mockReturnValue({ data: undefined });
  mockUseProviderCapabilities.mockReturnValue({ data: mockCapabilitiesMap });
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

  describe('Purge provider gating', () => {
    it('disables Purge with explanation for Azure namespaces', () => {
      mockUseNamespaces.mockReturnValue({ data: [makeNamespace('azure')] });
      const Wrapper = createWrapper();
      render(<Wrapper><MessageDetailPanel message={mockMessage} /></Wrapper>);
      const purgeBtn = screen.getByLabelText(/Purge not supported/);
      expect(purgeBtn).toBeDisabled();
      expect(purgeBtn).toHaveAttribute('title', mockCapabilitiesMap.Azure.notes);
    });

    it('disables Purge when namespace data is unavailable', () => {
      mockUseNamespaces.mockReturnValue({ data: undefined });
      const Wrapper = createWrapper();
      render(<Wrapper><MessageDetailPanel message={mockMessage} /></Wrapper>);
      expect(screen.getByLabelText(/Purge not supported/)).toBeDisabled();
    });

    it('disables Purge with a generic reason while capabilities are still loading', () => {
      mockUseNamespaces.mockReturnValue({ data: [makeNamespace('aws')] });
      mockUseProviderCapabilities.mockReturnValue({ data: undefined });
      const Wrapper = createWrapper();
      render(<Wrapper><MessageDetailPanel message={mockMessage} /></Wrapper>);
      expect(screen.getByLabelText(/Purge not supported/)).toBeDisabled();
    });

    it('enables Purge for AWS namespaces', () => {
      mockUseNamespaces.mockReturnValue({ data: [makeNamespace('aws')] });
      const Wrapper = createWrapper();
      render(<Wrapper><MessageDetailPanel message={mockMessage} /></Wrapper>);
      expect(screen.getByLabelText('Permanently delete message')).not.toBeDisabled();
    });

    it('enables Purge for GCP namespaces', () => {
      mockUseNamespaces.mockReturnValue({ data: [makeNamespace('gcp')] });
      const Wrapper = createWrapper();
      render(<Wrapper><MessageDetailPanel message={mockMessage} /></Wrapper>);
      expect(screen.getByLabelText('Permanently delete message')).not.toBeDisabled();
    });

    it('blocks Purge for production AWS namespaces', () => {
      mockUseNamespaces.mockReturnValue({ data: [{ ...makeNamespace('aws'), environment: 'prod' }] });
      const Wrapper = createWrapper();
      render(<Wrapper><MessageDetailPanel message={mockMessage} /></Wrapper>);
      expect(screen.getByLabelText('Purge blocked in production')).toBeDisabled();
    });

    it('opens danger confirm dialog when Purge is clicked', () => {
      mockUseNamespaces.mockReturnValue({ data: [makeNamespace('aws')] });
      const Wrapper = createWrapper();
      render(<Wrapper><MessageDetailPanel message={mockMessage} /></Wrapper>);
      fireEvent.click(screen.getByLabelText('Permanently delete message'));
      expect(screen.getByTestId('confirm-dialog')).toBeInTheDocument();
      expect(screen.getByText('Permanently Delete Message')).toBeInTheDocument();
    });

    it('calls purge with fromDeadLetter for DLQ messages on confirm', () => {
      const mutateAsync = vi.fn().mockResolvedValue(undefined);
      mockUsePurgeMessage.mockReturnValue({ mutateAsync, isPending: false });
      mockUseNamespaces.mockReturnValue({ data: [makeNamespace('aws')] });
      const Wrapper = createWrapper();
      render(<Wrapper><MessageDetailPanel message={mockMessage} /></Wrapper>);
      fireEvent.click(screen.getByLabelText('Permanently delete message'));
      fireEvent.click(screen.getByText('Confirm'));
      expect(mutateAsync).toHaveBeenCalledWith({
        namespaceId: 'ns1',
        sequenceNumber: 42,
        entityName: 'test-queue',
        subscriptionName: undefined,
        fromDeadLetter: true,
      });
    });
  });
});
