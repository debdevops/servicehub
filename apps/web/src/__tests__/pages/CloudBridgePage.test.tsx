import { render, screen } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { CloudBridgePage } from '@/pages/CloudBridgePage';
import { MemoryRouter } from 'react-router-dom';

vi.mock('@/hooks/useCloudBridge', () => ({
  useProviderStatus: vi.fn(),
  useCloudEntities: vi.fn(),
  useVisibilityStatus: vi.fn(),
}));

vi.mock('@/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));

import { useProviderStatus } from '@/hooks/useCloudBridge';
import { useNamespaces } from '@/hooks/useNamespaces';

const mockUseProviderStatus = useProviderStatus as ReturnType<typeof vi.fn>;
const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;

function renderPage() {
  return render(
    <MemoryRouter>
      <CloudBridgePage />
    </MemoryRouter>
  );
}

describe('CloudBridgePage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseNamespaces.mockReturnValue({ data: [], isLoading: false });
  });

  it('renders page heading', () => {
    mockUseProviderStatus.mockReturnValue({ data: undefined, isLoading: true });
    renderPage();
    expect(screen.getByRole('heading', { level: 1, name: /cloud bridge/i })).toBeInTheDocument();
  });

  it('shows Provider Status section', () => {
    mockUseProviderStatus.mockReturnValue({ data: undefined, isLoading: true });
    renderPage();
    expect(screen.getByText(/provider status/i)).toBeInTheDocument();
  });

  it('shows loading spinner while status is loading', () => {
    mockUseProviderStatus.mockReturnValue({ data: undefined, isLoading: true });
    renderPage();
    expect(screen.getByText(/checking providers/i)).toBeInTheDocument();
  });

  it('shows Disabled badges when all providers are disabled', () => {
    mockUseProviderStatus.mockReturnValue({
      data: { Aws: false, Gcp: false },
      isLoading: false,
    });
    renderPage();
    const disabledBadges = screen.getAllByText(/disabled/i);
    expect(disabledBadges.length).toBeGreaterThanOrEqual(2);
  });

  it('shows Active badges when providers are enabled', () => {
    mockUseProviderStatus.mockReturnValue({
      data: { Aws: true, Gcp: true },
      isLoading: false,
    });
    renderPage();
    const activeBadges = screen.getAllByText(/active/i);
    expect(activeBadges.length).toBeGreaterThanOrEqual(2);
  });

  it('shows no-providers warning when all disabled', () => {
    mockUseProviderStatus.mockReturnValue({
      data: { Aws: false, Gcp: false },
      isLoading: false,
    });
    renderPage();
    expect(screen.getByText(/no cloud providers are currently enabled/i)).toBeInTheDocument();
  });

  it('shows namespace selector when providers are enabled', () => {
    mockUseProviderStatus.mockReturnValue({
      data: { Aws: true, Gcp: false },
      isLoading: false,
    });
    renderPage();
    expect(screen.getByLabelText(/namespace/i)).toBeInTheDocument();
  });

  it('shows "select a namespace" prompt when no namespace selected', () => {
    mockUseProviderStatus.mockReturnValue({
      data: { Aws: true, Gcp: false },
      isLoading: false,
    });
    renderPage();
    expect(screen.getByText(/select a namespace above/i)).toBeInTheDocument();
  });

  it('populates namespace options from hook data', () => {
    mockUseProviderStatus.mockReturnValue({
      data: { Aws: true, Gcp: false },
      isLoading: false,
    });
    mockUseNamespaces.mockReturnValue({
      data: [
        { id: 'ns-1', name: 'prod-bus', displayName: 'Production Bus' },
        { id: 'ns-2', name: 'dev-bus', displayName: null },
      ],
      isLoading: false,
    });
    renderPage();
    expect(screen.getByRole('option', { name: 'Production Bus' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'dev-bus' })).toBeInTheDocument();
  });
});
