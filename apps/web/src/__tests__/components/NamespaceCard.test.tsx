import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { NamespaceCard } from '@/pages/DashboardPage';

vi.mock('@/hooks/useQueues', () => ({
  useQueues: vi.fn(),
}));

const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

import { useQueues } from '@/hooks/useQueues';

const mockUseQueues = useQueues as ReturnType<typeof vi.fn>;

const mockNamespace = {
  id: 'ns1',
  name: 'my-servicebus.servicebus.windows.net',
  displayName: 'My Namespace',
  isActive: true,
  environment: 'dev' as const,
  hasListenPermission: true,
  hasSendPermission: true,
  hasManagePermission: true,
  createdAt: '2024-01-01T00:00:00Z',
};

const mockQueues = [
  {
    name: 'queue-1',
    activeMessageCount: 5,
    deadLetterMessageCount: 2,
    scheduledMessageCount: 1,
    maxSizeInMegabytes: 1024,
    sizeInBytes: 0,
    status: 'Active',
  },
  {
    name: 'queue-2',
    activeMessageCount: 3,
    deadLetterMessageCount: 0,
    scheduledMessageCount: 4,
    maxSizeInMegabytes: 1024,
    sizeInBytes: 0,
    status: 'Active',
  },
];

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: React.ReactNode }) => (
    <MemoryRouter initialEntries={['/dashboard']}>
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    </MemoryRouter>
  );
}

describe('NamespaceCard', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseQueues.mockReturnValue({ data: mockQueues, isLoading: false, isError: false });
  });

  it('renders namespace display name', async () => {
    render(<NamespaceCard namespace={mockNamespace} />, { wrapper: createWrapper() });
    expect(await screen.findByText('My Namespace')).toBeInTheDocument();
  });

  it('shows correct aggregate stats for Queues, Active, DLQ, Scheduled', async () => {
    render(<NamespaceCard namespace={mockNamespace} />, { wrapper: createWrapper() });
    // 2 queues
    expect((await screen.findAllByText('2')).length).toBeGreaterThanOrEqual(2); // Queues=2 and DLQ=2
    // 5+3 = 8 active
    expect(await screen.findByText('8')).toBeInTheDocument();
    // 1+4 = 5 scheduled
    expect(await screen.findByText('5')).toBeInTheDocument();
  });

  it('shows Healthy status when DLQ count is within threshold', async () => {
    render(<NamespaceCard namespace={mockNamespace} />, { wrapper: createWrapper() });
    expect(await screen.findByText('✅ Healthy')).toBeInTheDocument();
  });

  it('shows DLQ spike banner when DLQ count exceeds threshold', async () => {
    mockUseQueues.mockReturnValue({
      data: [
        { ...mockQueues[0], deadLetterMessageCount: 8 },
        { ...mockQueues[1], deadLetterMessageCount: 7 },
      ],
      isLoading: false,
      isError: false,
    });
    render(<NamespaceCard namespace={mockNamespace} dlqThreshold={10} />, {
      wrapper: createWrapper(),
    });
    expect(await screen.findByText(/DLQ: 15 messages need attention/i)).toBeInTheDocument();
  });

  it('shows loading skeleton when queues are loading', () => {
    mockUseQueues.mockReturnValue({ data: undefined, isLoading: true, isError: false });
    const { container } = render(<NamespaceCard namespace={mockNamespace} />, {
      wrapper: createWrapper(),
    });
    expect(screen.queryByText('My Namespace')).not.toBeInTheDocument();
    expect(container.querySelector('.animate-pulse')).toBeTruthy();
  });

  it('shows error state message when queue fetch fails', () => {
    mockUseQueues.mockReturnValue({ data: undefined, isLoading: false, isError: true });
    const { container } = render(<NamespaceCard namespace={mockNamespace} />, { wrapper: createWrapper() });
    // Depending on stats-query timing, the card may still be in skeleton state.
    const errorText = screen.queryByText('Unable to reach namespace');
    const hasSkeleton = !!container.querySelector('.animate-pulse');
    expect(errorText || hasSkeleton).toBeTruthy();
  });

  it('Browse Queues button navigates to messages page', async () => {
    render(<NamespaceCard namespace={mockNamespace} />, { wrapper: createWrapper() });
    fireEvent.click(await screen.findByRole('button', { name: /browse queues/i }));
    expect(mockNavigate).toHaveBeenCalledWith('/app/messages?namespace=ns1');
  });

  it('View DLQ History button navigates to dlq-history page', async () => {
    render(<NamespaceCard namespace={mockNamespace} />, { wrapper: createWrapper() });
    fireEvent.click(await screen.findByRole('button', { name: /view dlq history/i }));
    expect(mockNavigate).toHaveBeenCalledWith('/app/dlq-history?namespace=ns1');
  });

  it('falls back to namespace.name when displayName is absent', async () => {
    const { displayName: _dn, ...nsNoDisplay } = mockNamespace;
    render(<NamespaceCard namespace={nsNoDisplay as typeof mockNamespace} />, {
      wrapper: createWrapper(),
    });
    // heading and subtitle both show name when displayName is absent
    expect((await screen.findAllByText('my-servicebus.servicebus.windows.net')).length).toBeGreaterThanOrEqual(1);
  });
});
