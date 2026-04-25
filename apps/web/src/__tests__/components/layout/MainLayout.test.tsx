import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MainLayout } from '@/components/layout/MainLayout';
import { useNamespaces } from '@/hooks/useNamespaces';

vi.mock('@/components/layout/Header', () => ({
  Header: () => <header data-testid="header">Header</header>,
}));
vi.mock('@/components/layout/Sidebar', () => ({
  Sidebar: () => <nav data-testid="sidebar">Sidebar</nav>,
}));
vi.mock('@/components/fab', () => ({
  MessageFAB: (_props: any) => <div data-testid="message-fab">FAB</div>,
}));
vi.mock('@/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));

function createWrapper(initialPath = '/') {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: React.ReactNode }) => (
    <MemoryRouter initialEntries={[initialPath]}>
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    </MemoryRouter>
  );
}

describe('MainLayout', () => {
  beforeEach(() => {
    // Default mock: Dev environment with Manage permission
    vi.mocked(useNamespaces).mockReturnValue({
      data: [
        {
          id: 'ns1',
          name: 'test-namespace',
          isActive: true,
          createdAt: '2024-01-01T00:00:00Z',
          environment: 'dev',
          hasManagePermission: true,
          hasListenPermission: true,
          hasSendPermission: true,
        },
      ],
      isLoading: false,
      isError: false,
      error: null,
      status: 'success',
      isPending: false,
      isPaused: false,
      isFetching: false,
      isLoadingError: false,
      isPlaceholderData: false,
      isRefetching: false,
      isStale: false,
      dataUpdatedAt: Date.now(),
      errorUpdatedAt: 0,
      failureCount: 0,
      failureReason: null,
      refetch: vi.fn(),
    } as any);
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it('renders Header component', () => {
    const Wrapper = createWrapper();
    render(
      <Wrapper>
        <Routes>
          <Route path="*" element={<MainLayout />} />
        </Routes>
      </Wrapper>
    );
    expect(screen.getByTestId('header')).toBeInTheDocument();
  });

  it('renders Sidebar component', () => {
    const Wrapper = createWrapper();
    render(
      <Wrapper>
        <Routes>
          <Route path="*" element={<MainLayout />} />
        </Routes>
      </Wrapper>
    );
    expect(screen.getByTestId('sidebar')).toBeInTheDocument();
  });

  it('renders main content area', () => {
    const Wrapper = createWrapper();
    render(
      <Wrapper>
        <Routes>
          <Route path="*" element={<MainLayout />} />
        </Routes>
      </Wrapper>
    );
    expect(screen.getByRole('main')).toBeInTheDocument();
  });

  it('does not show FAB when window.location.pathname is not /messages', () => {
    // jsdom's window.location.pathname defaults to '/' in MemoryRouter
    const Wrapper = createWrapper('/connect');
    render(
      <Wrapper>
        <Routes>
          <Route path="*" element={<MainLayout />} />
        </Routes>
      </Wrapper>
    );
    // window.location.pathname is '/' in jsdom, not '/messages', so no FAB
    expect(screen.queryByTestId('message-fab')).not.toBeInTheDocument();
  });

  it('shows FAB on /app/messages with Dev environment and Manage permission', () => {
    // Simulate being on the /app/messages path
    Object.defineProperty(window, 'location', {
      value: { ...window.location, pathname: '/app/messages' },
      writable: true,
    });
    const Wrapper = createWrapper('/app/messages?namespace=ns1&queue=test-queue');
    render(
      <Wrapper>
        <Routes>
          <Route path="*" element={<MainLayout />} />
        </Routes>
      </Wrapper>
    );
    expect(screen.getByTestId('message-fab')).toBeInTheDocument();
    // Reset
    Object.defineProperty(window, 'location', {
      value: { ...window.location, pathname: '/' },
      writable: true,
    });
  });

  it('does not show FAB when environment is Prod', () => {
    // Mock Prod environment
    vi.mocked(useNamespaces).mockReturnValue({
      data: [
        {
          id: 'ns1',
          name: 'test-namespace',
          isActive: true,
          createdAt: '2024-01-01T00:00:00Z',
          environment: 'prod',
          hasManagePermission: true,
          hasListenPermission: true,
          hasSendPermission: true,
        },
      ] as any,
      isLoading: false,
      isError: false,
      error: null,
      status: 'success',
      isPending: false,
      isPaused: false,
      isFetching: false,
      isLoadingError: false,
      isPlaceholderData: false,
      isRefetching: false,
      isStale: false,
      dataUpdatedAt: Date.now(),
      errorUpdatedAt: 0,
      failureCount: 0,
      failureReason: null,
      refetch: vi.fn(),
    } as any);

    Object.defineProperty(window, 'location', {
      value: { ...window.location, pathname: '/messages' },
      writable: true,
    });
    const Wrapper = createWrapper('/messages?namespace=ns1&queue=test-queue');
    render(
      <Wrapper>
        <Routes>
          <Route path="*" element={<MainLayout />} />
        </Routes>
      </Wrapper>
    );
    expect(screen.queryByTestId('message-fab')).not.toBeInTheDocument();
    // Reset
    Object.defineProperty(window, 'location', {
      value: { ...window.location, pathname: '/' },
      writable: true,
    });
  });

  it('does not show FAB when environment is UAT', () => {
    // Mock UAT environment
    vi.mocked(useNamespaces).mockReturnValue({
      data: [
        {
          id: 'ns1',
          name: 'test-namespace',
          isActive: true,
          createdAt: '2024-01-01T00:00:00Z',
          environment: 'uat',
          hasManagePermission: true,
          hasListenPermission: true,
          hasSendPermission: true,
        },
      ] as any,
      isLoading: false,
      isError: false,
      error: null,
      status: 'success',
      isPending: false,
      isPaused: false,
      isFetching: false,
      isLoadingError: false,
      isPlaceholderData: false,
      isRefetching: false,
      isStale: false,
      dataUpdatedAt: Date.now(),
      errorUpdatedAt: 0,
      failureCount: 0,
      failureReason: null,
      refetch: vi.fn(),
    } as any);

    Object.defineProperty(window, 'location', {
      value: { ...window.location, pathname: '/messages' },
      writable: true,
    });
    const Wrapper = createWrapper('/messages?namespace=ns1&queue=test-queue');
    render(
      <Wrapper>
        <Routes>
          <Route path="*" element={<MainLayout />} />
        </Routes>
      </Wrapper>
    );
    expect(screen.queryByTestId('message-fab')).not.toBeInTheDocument();
    // Reset
    Object.defineProperty(window, 'location', {
      value: { ...window.location, pathname: '/' },
      writable: true,
    });
  });

  it('does not show FAB when user has Listen-only permission in Dev', () => {
    // Mock Dev but with Listen-only permission
    vi.mocked(useNamespaces).mockReturnValue({
      data: [
        {
          id: 'ns1',
          name: 'test-namespace',
          isActive: true,
          createdAt: '2024-01-01T00:00:00Z',
          environment: 'dev',
          hasManagePermission: false,
          hasListenPermission: true,
          hasSendPermission: false,
        },
      ] as any,
      isLoading: false,
      isError: false,
      error: null,
      status: 'success',
      isPending: false,
      isPaused: false,
      isFetching: false,
      isLoadingError: false,
      isPlaceholderData: false,
      isRefetching: false,
      isStale: false,
      dataUpdatedAt: Date.now(),
      errorUpdatedAt: 0,
      failureCount: 0,
      failureReason: null,
      refetch: vi.fn(),
    } as any);

    Object.defineProperty(window, 'location', {
      value: { ...window.location, pathname: '/messages' },
      writable: true,
    });
    const Wrapper = createWrapper('/messages?namespace=ns1&queue=test-queue');
    render(
      <Wrapper>
        <Routes>
          <Route path="*" element={<MainLayout />} />
        </Routes>
      </Wrapper>
    );
    expect(screen.queryByTestId('message-fab')).not.toBeInTheDocument();
    // Reset
    Object.defineProperty(window, 'location', {
      value: { ...window.location, pathname: '/' },
      writable: true,
    });
  });

  it('does not show FAB when user has Send permission (but not Manage) in Dev', () => {
    // Mock Dev with Send permission but not Manage
    vi.mocked(useNamespaces).mockReturnValue({
      data: [
        {
          id: 'ns1',
          name: 'test-namespace',
          isActive: true,
          createdAt: '2024-01-01T00:00:00Z',
          environment: 'dev',
          hasManagePermission: false,
          hasListenPermission: true,
          hasSendPermission: true,
        },
      ] as any,
      isLoading: false,
      isError: false,
      error: null,
      status: 'success',
      isPending: false,
      isPaused: false,
      isFetching: false,
      isLoadingError: false,
      isPlaceholderData: false,
      isRefetching: false,
      isStale: false,
      dataUpdatedAt: Date.now(),
      errorUpdatedAt: 0,
      failureCount: 0,
      failureReason: null,
      refetch: vi.fn(),
    } as any);

    Object.defineProperty(window, 'location', {
      value: { ...window.location, pathname: '/messages' },
      writable: true,
    });
    const Wrapper = createWrapper('/messages?namespace=ns1&queue=test-queue');
    render(
      <Wrapper>
        <Routes>
          <Route path="*" element={<MainLayout />} />
        </Routes>
      </Wrapper>
    );
    expect(screen.queryByTestId('message-fab')).not.toBeInTheDocument();
    // Reset
    Object.defineProperty(window, 'location', {
      value: { ...window.location, pathname: '/' },
      writable: true,
    });
  });
});
