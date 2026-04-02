import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { DashboardPage } from '@/pages/DashboardPage';

vi.mock('@/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));

vi.mock('@/hooks/useQueues', () => ({
  useQueues: vi.fn(),
}));

const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

import { useNamespaces } from '@/hooks/useNamespaces';
import { useQueues } from '@/hooks/useQueues';

const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;
const mockUseQueues = useQueues as ReturnType<typeof vi.fn>;

const mockNamespace = {
  id: 'ns1',
  name: 'my-servicebus.servicebus.windows.net',
  displayName: 'My Namespace',
  isActive: true,
  environment: 'Dev' as const,
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

describe('DashboardPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseNamespaces.mockReturnValue({
      data: [mockNamespace],
      isLoading: false,
      isFetching: false,
      refetch: vi.fn(),
    });
    mockUseQueues.mockReturnValue({ data: mockQueues, isLoading: false, isError: false });
  });

  it('renders page title', () => {
    render(<DashboardPage />, { wrapper: createWrapper() });
    expect(screen.getByText('Namespace Overview')).toBeInTheDocument();
  });

  it('shows empty state with Connect button when no namespaces', () => {
    mockUseNamespaces.mockReturnValue({
      data: [],
      isLoading: false,
      isFetching: false,
      refetch: vi.fn(),
    });
    render(<DashboardPage />, { wrapper: createWrapper() });
    expect(screen.getByText('No namespaces connected yet')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /connect a namespace/i })).toBeInTheDocument();
  });

  it('Connect button navigates to /connect', () => {
    mockUseNamespaces.mockReturnValue({
      data: [],
      isLoading: false,
      isFetching: false,
      refetch: vi.fn(),
    });
    render(<DashboardPage />, { wrapper: createWrapper() });
    fireEvent.click(screen.getByRole('button', { name: /connect a namespace/i }));
    expect(mockNavigate).toHaveBeenCalledWith('/connect');
  });

  it('renders one NamespaceCard per namespace (displayName visible)', () => {
    render(<DashboardPage />, { wrapper: createWrapper() });
    expect(screen.getByText('My Namespace')).toBeInTheDocument();
  });

  it('renders multiple NamespaceCards for multiple namespaces', () => {
    const ns2 = { ...mockNamespace, id: 'ns2', displayName: 'Second Namespace', name: 'second.servicebus.windows.net' };
    mockUseNamespaces.mockReturnValue({
      data: [mockNamespace, ns2],
      isLoading: false,
      isFetching: false,
      refetch: vi.fn(),
    });
    render(<DashboardPage />, { wrapper: createWrapper() });
    expect(screen.getByText('My Namespace')).toBeInTheDocument();
    expect(screen.getByText('Second Namespace')).toBeInTheDocument();
  });

  it('shows loading skeletons while namespaces are loading', () => {
    mockUseNamespaces.mockReturnValue({
      data: undefined,
      isLoading: true,
      isFetching: true,
      refetch: vi.fn(),
    });
    const { container } = render(<DashboardPage />, { wrapper: createWrapper() });
    expect(screen.queryByText('My Namespace')).not.toBeInTheDocument();
    expect(container.querySelector('.animate-pulse')).toBeTruthy();
  });

  it('shows Healthy status when DLQ count is within threshold', () => {
    render(<DashboardPage />, { wrapper: createWrapper() });
    expect(screen.getByText('✅ Healthy')).toBeInTheDocument();
  });

  it('shows DLQ spike banner when DLQ count exceeds threshold', () => {
    mockUseQueues.mockReturnValue({
      data: [{ ...mockQueues[0], deadLetterMessageCount: 15 }],
      isLoading: false,
      isError: false,
    });
    render(<DashboardPage />, { wrapper: createWrapper() });
    expect(screen.getByText(/DLQ: 15 messages need attention/i)).toBeInTheDocument();
  });

  it('Browse Queues button navigates to messages page', () => {
    render(<DashboardPage />, { wrapper: createWrapper() });
    fireEvent.click(screen.getByRole('button', { name: /browse queues/i }));
    expect(mockNavigate).toHaveBeenCalledWith('/messages?namespace=ns1');
  });

  it('View DLQ History button navigates to dlq-history page', () => {
    render(<DashboardPage />, { wrapper: createWrapper() });
    fireEvent.click(screen.getByRole('button', { name: /view dlq history/i }));
    expect(mockNavigate).toHaveBeenCalledWith('/dlq-history?namespace=ns1');
  });

  it('shows DEV badge for Dev environment', () => {
    render(<DashboardPage />, { wrapper: createWrapper() });
    expect(screen.getByText('DEV')).toBeInTheDocument();
  });

  it('shows PROD badge for Prod environment', () => {
    mockUseNamespaces.mockReturnValue({
      data: [{ ...mockNamespace, environment: 'Prod' }],
      isLoading: false,
      isFetching: false,
      refetch: vi.fn(),
    });
    render(<DashboardPage />, { wrapper: createWrapper() });
    expect(screen.getByText('PROD')).toBeInTheDocument();
  });

  it('shows UAT badge for Uat environment', () => {
    mockUseNamespaces.mockReturnValue({
      data: [{ ...mockNamespace, environment: 'Uat' }],
      isLoading: false,
      isFetching: false,
      refetch: vi.fn(),
    });
    render(<DashboardPage />, { wrapper: createWrapper() });
    expect(screen.getByText('UAT')).toBeInTheDocument();
  });

  it('shows fallback badge when environment is undefined', () => {
    const { environment: _env, ...nsNoEnv } = mockNamespace;
    mockUseNamespaces.mockReturnValue({
      data: [nsNoEnv],
      isLoading: false,
      isFetching: false,
      refetch: vi.fn(),
    });
    render(<DashboardPage />, { wrapper: createWrapper() });
    expect(screen.getByText('—')).toBeInTheDocument();
  });
});
