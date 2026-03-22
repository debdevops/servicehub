import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MainLayout } from '@/components/layout/MainLayout';

vi.mock('@/components/layout/Header', () => ({
  Header: () => <header data-testid="header">Header</header>,
}));
vi.mock('@/components/layout/Sidebar', () => ({
  Sidebar: () => <nav data-testid="sidebar">Sidebar</nav>,
}));
vi.mock('@/components/fab', () => ({
  MessageFAB: (_props: any) => <div data-testid="message-fab">FAB</div>,
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

  it('shows FAB when window.location.pathname is /messages', () => {
    // Simulate being on the /messages path
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
    expect(screen.getByTestId('message-fab')).toBeInTheDocument();
    // Reset
    Object.defineProperty(window, 'location', {
      value: { ...window.location, pathname: '/' },
      writable: true,
    });
  });
});
