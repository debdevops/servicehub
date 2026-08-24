import { render, screen } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { CloudBridgePage } from '@/pages/CloudBridgePage';

vi.mock('@servicehub/ui-shared/hooks/useCloudBridge', () => ({
  useProviderStatus: vi.fn(),
  useCloudEntities: vi.fn(),
  useVisibilityStatus: vi.fn(),
  useProviderCapabilities: vi.fn(),
}));

vi.mock('@servicehub/ui-shared/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));

// CloudBridgePage rolls up per-provider DLQ counts via the same namespace-stats
// endpoint the Header/Quick Access already warm — mock it to an empty response.
vi.mock('@servicehub/ui-shared/lib/api/client', () => ({
  apiClient: {
    get: vi.fn().mockResolvedValue({ data: { totalDlq: 0 } }),
  },
}));

import { useProviderStatus, useProviderCapabilities } from '@servicehub/ui-shared/hooks/useCloudBridge';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';

const mockUseProviderStatus = useProviderStatus as ReturnType<typeof vi.fn>;
const mockUseProviderCapabilities = useProviderCapabilities as ReturnType<typeof vi.fn>;
const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <CloudBridgePage />
      </QueryClientProvider>
    </MemoryRouter>
  );
}

describe('CloudBridgePage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseNamespaces.mockReturnValue({ data: [], isLoading: false });
    mockUseProviderCapabilities.mockReturnValue({ data: undefined, isLoading: false });
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

  it('shows "Not available on this server" for disabled providers, distinct from "Not configured"', () => {
    mockUseProviderStatus.mockReturnValue({
      data: { Azure: false, Aws: false, Gcp: false },
      isLoading: false,
    });
    renderPage();
    const unavailableBadges = screen.getAllByText(/not available on this server/i);
    expect(unavailableBadges.length).toBe(3);
    expect(screen.queryByText(/not configured/i)).not.toBeInTheDocument();
  });

  it('shows "Connected" (not "Active") badges for enabled providers with ≥1 namespace', () => {
    mockUseProviderStatus.mockReturnValue({
      data: { Azure: true, Aws: true, Gcp: true },
      isLoading: false,
    });
    mockUseNamespaces.mockReturnValue({
      data: [
        { id: 'ns-1', name: 'azure-ns', isActive: true, cloudProvider: 'azure' },
        { id: 'ns-2', name: 'aws-ns', isActive: true, cloudProvider: 'aws' },
        { id: 'ns-3', name: 'gcp-ns', isActive: true, cloudProvider: 'gcp' },
      ],
      isLoading: false,
    });
    renderPage();
    const connectedBadges = screen.getAllByText('Connected');
    expect(connectedBadges.length).toBe(3);
    expect(screen.queryByText('Active')).not.toBeInTheDocument();
  });

  // ── F4 regression: GCP has no count API — "No backlog" would assert a fact the
  // app can't actually verify for GCP. Azure/AWS genuinely report zero, so they keep it.
  it('shows "No live count available" (not "No backlog") for GCP, whose capabilities report no live count', () => {
    mockUseProviderStatus.mockReturnValue({
      data: { Azure: true, Aws: true, Gcp: true },
      isLoading: false,
    });
    mockUseNamespaces.mockReturnValue({
      data: [
        { id: 'ns-1', name: 'azure-ns', isActive: true, cloudProvider: 'azure' },
        { id: 'ns-2', name: 'aws-ns', isActive: true, cloudProvider: 'aws' },
        { id: 'ns-3', name: 'gcp-ns', isActive: true, cloudProvider: 'gcp' },
      ],
      isLoading: false,
    });
    mockUseProviderCapabilities.mockReturnValue({
      data: {
        Azure: { supportsMessageCounts: true, supportsManualDeadLetter: true, supportsPurge: false, supportsScheduledMessages: true, supportsRepeatablePeek: true, notes: '' },
        Aws: { supportsMessageCounts: true, supportsManualDeadLetter: true, supportsPurge: true, supportsScheduledMessages: false, supportsRepeatablePeek: false, notes: '' },
        Gcp: { supportsMessageCounts: false, supportsManualDeadLetter: false, supportsPurge: true, supportsScheduledMessages: false, supportsRepeatablePeek: true, notes: '' },
      },
      isLoading: false,
    });
    renderPage();
    expect(screen.getByText('No live count available')).toBeInTheDocument();
    expect(screen.getAllByText('No backlog').length).toBe(2); // Azure + AWS
  });

  // Release-gate regression: capabilities still unknown (map not yet loaded) must never be
  // treated as "this provider supports live counts" — that would assert an unearned "No
  // backlog" for a provider whose real capability hasn't come back yet.
  it('shows "No live count available" (not "No backlog") while capabilities are still unknown/loading', () => {
    mockUseProviderStatus.mockReturnValue({
      data: { Azure: true },
      isLoading: false,
    });
    mockUseNamespaces.mockReturnValue({
      data: [{ id: 'ns-1', name: 'azure-ns', isActive: true, cloudProvider: 'azure' }],
      isLoading: false,
    });
    // beforeEach already leaves capabilities as { data: undefined, isLoading: false } — the
    // shape useProviderCapabilities has before its query resolves.
    renderPage();
    expect(screen.getByText('No live count available')).toBeInTheDocument();
    expect(screen.queryByText('No backlog')).not.toBeInTheDocument();
  });

  it('shows "Not configured" (not "Active") for an enabled provider with zero namespaces, with an add-connection link', () => {
    mockUseProviderStatus.mockReturnValue({
      data: { Azure: true, Aws: true, Gcp: true },
      isLoading: false,
    });
    // No namespaces at all — every enabled provider has 0.
    renderPage();
    const notConfiguredBadges = screen.getAllByText('Not configured');
    expect(notConfiguredBadges.length).toBe(3);
    expect(screen.getAllByText('+ Add a connection').length).toBe(3);
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

  it('shows amber "Connection issue" (not green) while still displaying stats, when a namespace test failed', () => {
    mockUseProviderStatus.mockReturnValue({
      data: { Azure: true, Aws: true, Gcp: true },
      isLoading: false,
    });
    mockUseNamespaces.mockReturnValue({
      data: [{ id: 'ns-1', name: 'aws-ns', isActive: true, cloudProvider: 'aws', lastConnectionTestSucceeded: false }],
      isLoading: false,
    });
    renderPage();
    expect(screen.getByText('Connection issue')).toBeInTheDocument();
    expect(screen.getByText(/namespace.*connected/i)).toBeInTheDocument();
  });

  it('still resolves to "Not available on this server" (never a false "Connected") when provider-status has no data, as in Demo Mode', () => {
    // useProviderStatus() is disabled in Demo Mode and its `data` never resolves — this
    // mirrors that shape without depending on DemoContext internals.
    mockUseProviderStatus.mockReturnValue({ data: undefined, isLoading: false });
    renderPage();
    expect(screen.getAllByText('Not available on this server').length).toBe(3);
    expect(screen.queryByLabelText(/namespace/i)).not.toBeInTheDocument();
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
