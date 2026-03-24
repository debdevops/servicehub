import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { HealthPage } from '@/pages/HealthPage';

vi.mock('@/hooks/useHealth', () => ({
  useHealthVersion: vi.fn(),
  useHealthStatus: vi.fn(),
}));

import { useHealthVersion, useHealthStatus } from '@/hooks/useHealth';

const mockUseHealthVersion = useHealthVersion as ReturnType<typeof vi.fn>;
const mockUseHealthStatus = useHealthStatus as ReturnType<typeof vi.fn>;

const mockVersionData = {
  version: '2.1.3.0',
  informationalVersion: '2.1.3+abc123',
  environment: 'Development',
  machineName: 'test-machine',
  osDescription: 'macOS 14.0',
  frameworkDescription: '.NET 10.0.0',
  startedAt: '2024-01-01T00:00:00Z',
};

const mockStatusData = {
  isHealthy: true,
  uptime: '01:30:45.1234567',
  memoryUsageMb: 128,
  threadCount: 24,
  gcTotalMemoryMb: 64,
  gen0Collections: 100,
  gen1Collections: 20,
  gen2Collections: 5,
  timestamp: '2024-01-01T01:30:45Z',
};

function renderHealthPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <HealthPage />
      </MemoryRouter>
    </QueryClientProvider>
  );
}

describe('HealthPage', () => {
  beforeEach(() => vi.clearAllMocks());

  it('renders loading state', () => {
    mockUseHealthVersion.mockReturnValue({ data: undefined, isLoading: true, error: null });
    mockUseHealthStatus.mockReturnValue({ data: undefined, isLoading: true, error: null, refetch: vi.fn() });

    renderHealthPage();
    expect(screen.getByText(/Loading health data/)).toBeInTheDocument();
  });

  it('renders error state when API is unreachable', () => {
    mockUseHealthVersion.mockReturnValue({ data: undefined, isLoading: false, error: new Error('Network Error') });
    mockUseHealthStatus.mockReturnValue({ data: undefined, isLoading: false, error: null, refetch: vi.fn() });

    renderHealthPage();
    expect(screen.getByText(/Unable to reach the API server/)).toBeInTheDocument();
  });

  it('renders healthy badge', () => {
    mockUseHealthVersion.mockReturnValue({ data: mockVersionData, isLoading: false, error: null });
    mockUseHealthStatus.mockReturnValue({ data: mockStatusData, isLoading: false, error: null, refetch: vi.fn() });

    renderHealthPage();
    expect(screen.getByText('Healthy')).toBeInTheDocument();
  });

  it('renders unhealthy badge', () => {
    const unhealthyStatus = { ...mockStatusData, isHealthy: false };
    mockUseHealthVersion.mockReturnValue({ data: mockVersionData, isLoading: false, error: null });
    mockUseHealthStatus.mockReturnValue({ data: unhealthyStatus, isLoading: false, error: null, refetch: vi.fn() });

    renderHealthPage();
    expect(screen.getByText('Unhealthy')).toBeInTheDocument();
  });

  it('renders memory usage stat', () => {
    mockUseHealthVersion.mockReturnValue({ data: mockVersionData, isLoading: false, error: null });
    mockUseHealthStatus.mockReturnValue({ data: mockStatusData, isLoading: false, error: null, refetch: vi.fn() });

    renderHealthPage();
    expect(screen.getByText('128 MB')).toBeInTheDocument();
  });

  it('renders thread count stat', () => {
    mockUseHealthVersion.mockReturnValue({ data: mockVersionData, isLoading: false, error: null });
    mockUseHealthStatus.mockReturnValue({ data: mockStatusData, isLoading: false, error: null, refetch: vi.fn() });

    renderHealthPage();
    expect(screen.getByText('24')).toBeInTheDocument();
  });

  it('renders version information', () => {
    mockUseHealthVersion.mockReturnValue({ data: mockVersionData, isLoading: false, error: null });
    mockUseHealthStatus.mockReturnValue({ data: mockStatusData, isLoading: false, error: null, refetch: vi.fn() });

    renderHealthPage();
    expect(screen.getByText('2.1.3.0')).toBeInTheDocument();
    expect(screen.getByText('Development')).toBeInTheDocument();
    expect(screen.getByText('test-machine')).toBeInTheDocument();
  });

  it('renders page header', () => {
    mockUseHealthVersion.mockReturnValue({ data: mockVersionData, isLoading: false, error: null });
    mockUseHealthStatus.mockReturnValue({ data: mockStatusData, isLoading: false, error: null, refetch: vi.fn() });

    renderHealthPage();
    expect(screen.getByText('System Health')).toBeInTheDocument();
  });

  it('renders refresh button', () => {
    mockUseHealthVersion.mockReturnValue({ data: mockVersionData, isLoading: false, error: null });
    mockUseHealthStatus.mockReturnValue({ data: mockStatusData, isLoading: false, error: null, refetch: vi.fn() });

    renderHealthPage();
    expect(screen.getByText('Refresh')).toBeInTheDocument();
  });

  it('renders GC collections', () => {
    mockUseHealthVersion.mockReturnValue({ data: mockVersionData, isLoading: false, error: null });
    mockUseHealthStatus.mockReturnValue({ data: mockStatusData, isLoading: false, error: null, refetch: vi.fn() });

    renderHealthPage();
    expect(screen.getByText('100 / 20 / 5')).toBeInTheDocument();
  });

  it('formats uptime correctly', () => {
    mockUseHealthVersion.mockReturnValue({ data: mockVersionData, isLoading: false, error: null });
    mockUseHealthStatus.mockReturnValue({ data: mockStatusData, isLoading: false, error: null, refetch: vi.fn() });

    renderHealthPage();
    expect(screen.getByText('01h 30m 45s')).toBeInTheDocument();
  });

  it('formats uptime with days', () => {
    const statusWithDays = { ...mockStatusData, uptime: '3.12:05:30.0000000' };
    mockUseHealthVersion.mockReturnValue({ data: mockVersionData, isLoading: false, error: null });
    mockUseHealthStatus.mockReturnValue({ data: statusWithDays, isLoading: false, error: null, refetch: vi.fn() });

    renderHealthPage();
    expect(screen.getByText('3d 12h 05m 30s')).toBeInTheDocument();
  });
});
