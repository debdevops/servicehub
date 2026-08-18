import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { LiveTailPage } from '@/pages/LiveTailPage';

vi.mock('@servicehub/ui-shared/hooks/useNamespaces', () => ({ useNamespaces: vi.fn() }));
vi.mock('@servicehub/ui-shared/hooks/useQueues', () => ({ useQueues: vi.fn() }));
vi.mock('@servicehub/ui-shared/hooks/useTopics', () => ({ useTopics: vi.fn() }));
vi.mock('@servicehub/ui-shared/hooks/useSubscriptions', () => ({ useSubscriptions: vi.fn() }));
vi.mock('@servicehub/ui-shared/hooks/useLiveTail', () => ({ useLiveTail: vi.fn() }));
vi.mock('@servicehub/ui-shared/hooks/useCloudBridge', () => ({ useProviderCapabilities: vi.fn() }));
vi.mock('@servicehub/ui-shared/lib/demo/DemoContext', () => ({ useDemoContext: vi.fn() }));

import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useQueues } from '@servicehub/ui-shared/hooks/useQueues';
import { useTopics } from '@servicehub/ui-shared/hooks/useTopics';
import { useSubscriptions } from '@servicehub/ui-shared/hooks/useSubscriptions';
import { useLiveTail } from '@servicehub/ui-shared/hooks/useLiveTail';
import { useProviderCapabilities } from '@servicehub/ui-shared/hooks/useCloudBridge';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';

const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;
const mockUseQueues = useQueues as ReturnType<typeof vi.fn>;
const mockUseTopics = useTopics as ReturnType<typeof vi.fn>;
const mockUseSubscriptions = useSubscriptions as ReturnType<typeof vi.fn>;
const mockUseLiveTail = useLiveTail as ReturnType<typeof vi.fn>;
const mockUseProviderCapabilities = useProviderCapabilities as ReturnType<typeof vi.fn>;
const mockUseDemoContext = useDemoContext as ReturnType<typeof vi.fn>;

const azureNs = {
  id: 'ns-azure',
  name: 'sb-dev.servicebus.windows.net',
  displayName: 'Dev SB',
  isActive: true,
  environment: 'dev' as const,
  cloudProvider: 'azure' as const,
  createdAt: '2026-01-01T00:00:00Z',
};

const awsNs = { ...azureNs, id: 'ns-aws', displayName: 'AWS DEV', cloudProvider: 'aws' as const };

const capabilitiesMap = {
  Azure: { supportsMessageCounts: true, supportsManualDeadLetter: true, supportsPurge: false, supportsScheduledMessages: true, supportsRepeatablePeek: true, notes: '' },
  Aws: { supportsMessageCounts: true, supportsManualDeadLetter: true, supportsPurge: true, supportsScheduledMessages: false, supportsRepeatablePeek: false, notes: 'SQS has no non-destructive peek.' },
};

const start = vi.fn();
const stop = vi.fn();
const clear = vi.fn();

function renderPage(initialEntry: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <QueryClientProvider client={queryClient}>
        <LiveTailPage />
      </QueryClientProvider>
    </MemoryRouter>,
  );
}

describe('LiveTailPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseNamespaces.mockReturnValue({ data: [azureNs, awsNs], isLoading: false });
    mockUseQueues.mockReturnValue({ data: [{ name: 'orders', activeMessageCount: 0, deadLetterMessageCount: 0, scheduledMessageCount: 0, sizeInBytes: 0, status: 'Active' }], isLoading: false });
    mockUseTopics.mockReturnValue({ data: [], isLoading: false });
    mockUseSubscriptions.mockReturnValue({ data: [], isLoading: false });
    mockUseProviderCapabilities.mockReturnValue({ data: capabilitiesMap });
    mockUseDemoContext.mockReturnValue({ isDemoMode: false, cloudProvider: undefined });
    mockUseLiveTail.mockReturnValue({ status: 'idle', messages: [], start, stop, clear });
  });

  it('shows the source picker when nothing is selected', () => {
    renderPage('/live-tail');
    expect(screen.queryByText('No namespaces connected')).not.toBeInTheDocument();
    expect(screen.getByText('Dev SB')).toBeInTheDocument();
    expect(screen.getByText('AWS DEV')).toBeInTheDocument();
    expect(start).not.toHaveBeenCalled();
  });

  it('selecting a queue navigates and starts watching', () => {
    mockUseLiveTail.mockReturnValue({ status: 'connecting', messages: [], start, stop, clear });
    renderPage('/live-tail?namespace=ns-azure&queue=orders');
    expect(screen.getByText(/Azure · Dev SB · Azure Service Bus Queue · orders/)).toBeInTheDocument();
    expect(start).toHaveBeenCalledTimes(1);
  });

  it('shows the connected status and rendered messages', () => {
    mockUseLiveTail.mockReturnValue({
      status: 'connected',
      messages: [
        { messageId: 'm1', sequenceNumber: 1, enqueuedTime: new Date().toISOString(), deliveryCount: 1, contentType: 'application/json', body: '{"hello":"world"}' },
      ],
      start,
      stop,
      clear,
    });
    renderPage('/live-tail?namespace=ns-azure&queue=orders');
    expect(screen.getByText('Live')).toBeInTheDocument();
    expect(screen.getByText('m1')).toBeInTheDocument();
  });

  it('expands a message row to show full detail', () => {
    mockUseLiveTail.mockReturnValue({
      status: 'connected',
      messages: [
        { messageId: 'm1', sequenceNumber: 1, enqueuedTime: new Date().toISOString(), deliveryCount: 1, contentType: 'application/json', body: '{"hello":"world"}', correlationId: 'corr-1' },
      ],
      start,
      stop,
      clear,
    });
    renderPage('/live-tail?namespace=ns-azure&queue=orders');
    fireEvent.click(screen.getByText('m1'));
    expect(screen.getByText(/Correlation-Id:/)).toBeInTheDocument();
    expect(screen.getByText('corr-1')).toBeInTheDocument();
  });

  it('shows the empty state while connected with no messages yet', () => {
    mockUseLiveTail.mockReturnValue({ status: 'connected', messages: [], start, stop, clear });
    renderPage('/live-tail?namespace=ns-azure&queue=orders');
    expect(screen.getByText('Watching for new messages…')).toBeInTheDocument();
  });

  // rerender() must be given a freshly-created element each time, not the same element
  // reference reused — React (and RTL's rerender) can bail out of re-invoking the tree when
  // the root is re-rendered with an identical element object, which would silently defeat
  // these tests' whole premise (the mocked hook's new return value never being picked up).
  function pausableLiveTailTree() {
    return (
      <MemoryRouter initialEntries={['/live-tail?namespace=ns-azure&queue=orders']}>
        <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })}>
          <LiveTailPage />
        </QueryClientProvider>
      </MemoryRouter>
    );
  }

  it('shows "Paused" (not "Stopped") after the operator clicks Pause', () => {
    mockUseLiveTail.mockReturnValue({ status: 'connected', messages: [], start, stop, clear });
    const { rerender } = render(pausableLiveTailTree());
    expect(screen.getByText('Live')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Pause/ }));
    expect(stop).toHaveBeenCalledTimes(1);

    // The underlying session actually closes after Pause calls stop() — simulate that.
    mockUseLiveTail.mockReturnValue({ status: 'idle', messages: [], start, stop, clear });
    rerender(pausableLiveTailTree());

    expect(screen.getByText('Paused')).toBeInTheDocument();
    expect(screen.queryByText('Stopped')).not.toBeInTheDocument();
  });

  it('shows "Stopped" (not "Paused") after the operator clicks Stop', () => {
    mockUseLiveTail.mockReturnValue({ status: 'connected', messages: [], start, stop, clear });
    const { rerender } = render(pausableLiveTailTree());

    fireEvent.click(screen.getByRole('button', { name: /^Stop$/ }));
    expect(stop).toHaveBeenCalledTimes(1);

    mockUseLiveTail.mockReturnValue({ status: 'idle', messages: [], start, stop, clear });
    rerender(pausableLiveTailTree());

    expect(screen.getByText('Stopped')).toBeInTheDocument();
    expect(screen.queryByText('Paused')).not.toBeInTheDocument();
  });

  it('blocks AWS from starting a session and explains why, without calling start()', () => {
    renderPage('/live-tail?namespace=ns-aws&queue=orders');
    expect(screen.getByText('Live Tail unavailable for AWS SQS')).toBeInTheDocument();
    expect(screen.getByText(/does not provide a non-destructive observation mechanism/)).toBeInTheDocument();
    expect(start).not.toHaveBeenCalled();
  });

  it('shows the demo-mode explanation instead of the AWS-specific one when unsupported in demo', () => {
    mockUseDemoContext.mockReturnValue({ isDemoMode: true, cloudProvider: 'azure' });
    mockUseLiveTail.mockReturnValue({ status: 'unsupported', messages: [], start, stop, clear });
    renderPage('/live-tail?namespace=ns-azure&queue=orders');
    expect(screen.getByText(/isn't available in Demo Mode/)).toBeInTheDocument();
  });

  it('filters the message list by search text', () => {
    mockUseLiveTail.mockReturnValue({
      status: 'connected',
      messages: [
        { messageId: 'm1', sequenceNumber: 1, enqueuedTime: new Date().toISOString(), deliveryCount: 1, contentType: 'application/json', body: 'order-created' },
        { messageId: 'm2', sequenceNumber: 2, enqueuedTime: new Date().toISOString(), deliveryCount: 1, contentType: 'application/json', body: 'order-cancelled' },
      ],
      start,
      stop,
      clear,
    });
    renderPage('/live-tail?namespace=ns-azure&queue=orders');
    fireEvent.change(screen.getByLabelText('Filter messages'), { target: { value: 'm1' } });
    expect(screen.getByText('m1')).toBeInTheDocument();
    expect(screen.queryByText('m2')).not.toBeInTheDocument();
  });
});
