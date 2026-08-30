import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import AutonomyPage from '@/pages/AutonomyPage';
import { useAutonomyDashboard, useApprovalQueue } from '@servicehub/ui-shared/hooks/useRecoveryLedger';
import { usePlaybookEntries } from '@servicehub/ui-shared/hooks/usePlaybookLedger';
import { useProviderCapabilities } from '@servicehub/ui-shared/hooks/useCloudBridge';
import { useMe } from '@servicehub/ui-shared/hooks/useMe';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';

vi.mock('@servicehub/ui-shared/hooks/useRecoveryLedger', () => ({
  useAutonomyDashboard: vi.fn(),
  useApprovalQueue: vi.fn(),
}));

vi.mock('@servicehub/ui-shared/hooks/usePlaybookLedger', () => ({
  usePlaybookEntries: vi.fn(),
}));

vi.mock('@servicehub/ui-shared/hooks/useCloudBridge', () => ({
  useProviderCapabilities: vi.fn(),
}));

vi.mock('@servicehub/ui-shared/hooks/useMe', () => ({
  useMe: vi.fn(),
}));

vi.mock('@servicehub/ui-shared/lib/demo/DemoContext', () => ({
  useDemoContext: vi.fn(),
}));

const mockUseAutonomyDashboard = useAutonomyDashboard as ReturnType<typeof vi.fn>;
const mockUseApprovalQueue = useApprovalQueue as ReturnType<typeof vi.fn>;
const mockUsePlaybookEntries = usePlaybookEntries as ReturnType<typeof vi.fn>;
const mockUseProviderCapabilities = useProviderCapabilities as ReturnType<typeof vi.fn>;
const mockUseMe = useMe as ReturnType<typeof vi.fn>;
const mockUseDemoContext = useDemoContext as ReturnType<typeof vi.fn>;

const emptyOverview = {
  generatedAt: '2026-08-30T00:00:00Z',
  emergencyStopActive: false,
  totalSignatures: 0,
  levelCounts: [],
  grants: [],
  circuitBreakerTrips: [],
  recentTransitions: [],
};

const capabilitiesMap = {
  Azure: { supportsMessageCounts: true, supportsManualDeadLetter: true, supportsPurge: true, supportsScheduledMessages: true, supportsRepeatablePeek: true, notes: 'Full peek coverage.', canProveDlqAbsence: true },
  Aws: { supportsMessageCounts: true, supportsManualDeadLetter: true, supportsPurge: true, supportsScheduledMessages: false, supportsRepeatablePeek: false, notes: 'No non-destructive peek.', canProveDlqAbsence: false },
  Gcp: { supportsMessageCounts: true, supportsManualDeadLetter: true, supportsPurge: true, supportsScheduledMessages: false, supportsRepeatablePeek: false, notes: 'Capped scan per cycle.', canProveDlqAbsence: false },
};

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/autonomy']}>
      <AutonomyPage />
    </MemoryRouter>,
  );
}

describe('AutonomyPage', () => {
  beforeEach(() => {
    mockUseDemoContext.mockReturnValue({ isDemoMode: false, cloudProvider: null });
    mockUseApprovalQueue.mockReturnValue({ data: [] });
    mockUsePlaybookEntries.mockReturnValue({ data: [] });
    mockUseProviderCapabilities.mockReturnValue({ data: capabilitiesMap });
    mockUseMe.mockReturnValue({ data: { ownerId: 'owner-1', authMethod: 'ApiKey', governanceRole: 'Admin' } });
  });

  it('shows a loading state', () => {
    mockUseAutonomyDashboard.mockReturnValue({ data: undefined, isLoading: true, isError: false, refetch: vi.fn(), isFetching: true });
    renderPage();
    expect(screen.getByText('Autonomy')).toBeInTheDocument();
  });

  it('shows an error state with a retry option', () => {
    mockUseAutonomyDashboard.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn(), isFetching: false });
    renderPage();
    expect(screen.getByText('Failed to load the autonomy overview')).toBeInTheDocument();
  });

  it('renders the emergency-stop banner when active', () => {
    mockUseAutonomyDashboard.mockReturnValue({ data: { ...emptyOverview, emergencyStopActive: true }, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();
    expect(screen.getByText('Emergency stop is active')).toBeInTheDocument();
  });

  it('renders per-pillar cards and provider constraints from real data, with no fabricated values', () => {
    mockUseAutonomyDashboard.mockReturnValue({
      data: {
        ...emptyOverview,
        totalSignatures: 5,
        levelCounts: [{ actionKind: 'Replay', level: 4, levelLabel: 'Standing (L4)', count: 2 }],
        grants: [{ signatureHash: 'abc123', actionKind: 'Replay', currentLevel: 4, levelLabel: 'Standing (L4)', updatedAtUtc: '2026-08-30T00:00:00Z' }],
      },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
      isFetching: false,
    });

    renderPage();

    // Pillar headings present
    expect(screen.getByText('Recover')).toBeInTheDocument();
    expect(screen.getByText('Investigate')).toBeInTheDocument();
    expect(screen.getByText('Correlate')).toBeInTheDocument();
    expect(screen.getByText('Prevent')).toBeInTheDocument();

    // Provider constraint table renders real capability facts
    expect(screen.getByText('Azure')).toBeInTheDocument();
    expect(screen.getByText('AWS')).toBeInTheDocument();
    expect(screen.getByText('GCP')).toBeInTheDocument();
    expect(screen.getAllByText('Permanently capped at Approve (L3) — a provider fact, not a maturity gap. Human approval is always required.')).toHaveLength(2);

    // The one honestly-unavailable card is present and marked as such
    expect(screen.getByText('Future AI reasoning')).toBeInTheDocument();
    expect(screen.getByText('Not available yet')).toBeInTheDocument();

    // Governance role surfaced from /me
    expect(screen.getByText('Admin')).toBeInTheDocument();
  });

  it('shows an honest error, not a fabricated empty state, when a pillar fetch fails', () => {
    mockUseAutonomyDashboard.mockReturnValue({ data: emptyOverview, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    mockUsePlaybookEntries.mockImplementation((params: { pillarKind?: string }) =>
      params.pillarKind === 'Correlate' ? { data: undefined, isError: true } : { data: [] },
    );
    renderPage();
    expect(screen.getByText("Couldn't load this pillar's proposals.")).toBeInTheDocument();
    // Investigate/Prevent, which didn't error, still show the honest empty state
    expect(screen.getAllByText('No proposals recorded yet for this pillar.')).toHaveLength(2);
  });

  it('shows the demo-mode banner and never claims a live ledger', () => {
    mockUseDemoContext.mockReturnValue({ isDemoMode: true, cloudProvider: 'azure' });
    mockUseAutonomyDashboard.mockReturnValue({ data: emptyOverview, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();
    expect(screen.getByText(/Demo Mode/)).toBeInTheDocument();
  });
});
