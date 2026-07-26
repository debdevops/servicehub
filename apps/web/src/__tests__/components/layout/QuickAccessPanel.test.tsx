import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { QuickAccessPanel } from '@/components/layout/QuickAccessPanel';

vi.mock('@/lib/api/client', () => ({
  apiClient: {
    get: vi.fn().mockResolvedValue({ data: [] }),
  },
}));

vi.mock('@/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));
vi.mock('@/hooks/useSimulator', () => ({
  useIsSimulatorMode: vi.fn(),
}));
vi.mock('react-hot-toast', () => ({
  default: { error: vi.fn(), success: vi.fn() },
  toast: vi.fn(),
}));

import { useNamespaces } from '@/hooks/useNamespaces';
import { useIsSimulatorMode } from '@/hooks/useSimulator';

const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;
const mockUseIsSimulatorMode = useIsSimulatorMode as ReturnType<typeof vi.fn>;

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: React.ReactNode }) => (
    <MemoryRouter initialEntries={['/messages?namespace=ns1&queue=my-queue']}>
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    </MemoryRouter>
  );
}

const mockNamespaces = [
  { id: 'ns1', name: 'my-namespace', displayName: 'My Namespace', isActive: true },
];

beforeEach(() => {
  localStorage.clear();
  mockUseNamespaces.mockReturnValue({ data: mockNamespaces, isLoading: false, refetch: vi.fn() });
  mockUseIsSimulatorMode.mockReturnValue({ isSimulator: false });
});

describe('QuickAccessPanel', () => {
  it('renders the panel title', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><QuickAccessPanel /></Wrapper>);
    expect(screen.getByText('Quick Access')).toBeInTheDocument();
  });

  it('renders navigation shortcuts without needing to expand anything', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><QuickAccessPanel /></Wrapper>);
    expect(screen.getByText('Active Messages')).toBeInTheDocument();
    expect(screen.getByText('Dead-Letter')).toBeInTheDocument();
    expect(screen.getByText('Namespace Overview')).toBeInTheDocument();
    expect(screen.getByText('Fleet Health')).toBeInTheDocument();
    expect(screen.getByText('DLQ Intelligence')).toBeInTheDocument();
    expect(screen.getByText('Auto-Replay Rules')).toBeInTheDocument();
    expect(screen.getByText('Scheduled Messages')).toBeInTheDocument();
    expect(screen.getByText('Multi-Cloud Trace')).toBeInTheDocument();
    expect(screen.getByText('Cloud Bridge')).toBeInTheDocument();
    expect(screen.getByText('System Health')).toBeInTheDocument();
    expect(screen.getByText('Audit Trail')).toBeInTheDocument();
    expect(screen.getByText('Security & Privacy')).toBeInTheDocument();
    expect(screen.getByText('Help & Guide')).toBeInTheDocument();
  });

  it('groups shortcuts under section labels', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><QuickAccessPanel /></Wrapper>);
    expect(screen.getByText('Overview')).toBeInTheDocument();
    expect(screen.getByText('Browse across clouds')).toBeInTheDocument();
    expect(screen.getByText('Diagnose & automate')).toBeInTheDocument();
    expect(screen.getByText('Platform')).toBeInTheDocument();
    expect(screen.getByText('Support')).toBeInTheDocument();
  });

  it('collapses and re-expands via the header button', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><QuickAccessPanel /></Wrapper>);
    expect(screen.getByText('Active Messages')).toBeInTheDocument();

    fireEvent.click(screen.getByLabelText('Collapse Quick Access'));
    expect(screen.queryByText('Active Messages')).not.toBeInTheDocument();

    fireEvent.click(screen.getByLabelText('Expand Quick Access'));
    expect(screen.getByText('Active Messages')).toBeInTheDocument();
  });

  it('does not show the Simulator link outside simulator mode', () => {
    const Wrapper = createWrapper();
    render(<Wrapper><QuickAccessPanel /></Wrapper>);
    expect(screen.queryByText('Simulator')).not.toBeInTheDocument();
  });

  it('shows the Simulator link in simulator mode', () => {
    mockUseIsSimulatorMode.mockReturnValue({ isSimulator: true });
    const Wrapper = createWrapper();
    render(<Wrapper><QuickAccessPanel /></Wrapper>);
    expect(screen.getByText('Simulator')).toBeInTheDocument();
  });
});
