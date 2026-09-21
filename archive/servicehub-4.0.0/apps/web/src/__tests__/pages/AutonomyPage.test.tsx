import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import AutonomyPage from '@/pages/AutonomyPage';
import {
  useAutonomyDashboard, useApprovalQueue, useOutcomeMetrics, useRecoveryOperations, useRecoveryEntries,
} from '@servicehub/ui-shared/hooks/useRecoveryLedger';
import { usePlaybookEntries } from '@servicehub/ui-shared/hooks/usePlaybookLedger';
import { useProviderCapabilities } from '@servicehub/ui-shared/hooks/useCloudBridge';
import { useMe } from '@servicehub/ui-shared/hooks/useMe';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useGovernanceGrants } from '@servicehub/ui-shared/hooks/useGovernanceGrants';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';

vi.mock('@servicehub/ui-shared/hooks/useRecoveryLedger', () => ({
  useAutonomyDashboard: vi.fn(),
  useApprovalQueue: vi.fn(),
  useOutcomeMetrics: vi.fn(),
  useRecoveryOperations: vi.fn(),
  useRecoveryEntries: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/usePlaybookLedger', () => ({ usePlaybookEntries: vi.fn() }));
vi.mock('@servicehub/ui-shared/hooks/useCloudBridge', () => ({ useProviderCapabilities: vi.fn() }));
vi.mock('@servicehub/ui-shared/hooks/useMe', () => ({ useMe: vi.fn() }));
vi.mock('@servicehub/ui-shared/hooks/useNamespaces', () => ({ useNamespaces: vi.fn() }));
vi.mock('@servicehub/ui-shared/hooks/useGovernanceGrants', () => ({ useGovernanceGrants: vi.fn() }));
vi.mock('@servicehub/ui-shared/lib/demo/DemoContext', () => ({ useDemoContext: vi.fn() }));

const m = <T,>(fn: T) => fn as unknown as ReturnType<typeof vi.fn>;

const emptyOverview = {
  generatedAt: '2026-09-10T00:00:00Z',
  emergencyStopActive: false,
  totalSignatures: 0,
  levelCounts: [],
  grants: [],
  circuitBreakerTrips: [],
  recentTransitions: [],
};

const capabilitiesMap = {
  Azure: { canProveDlqAbsence: true, notes: 'Full peek coverage.' },
  Aws: { canProveDlqAbsence: false, notes: 'No non-destructive peek.' },
  Gcp: { canProveDlqAbsence: false, notes: 'Capped scan per cycle.' },
};

function dashboard(data: object = emptyOverview, extra: object = {}) {
  m(useAutonomyDashboard).mockReturnValue({ data, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false, ...extra });
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/autonomy']}>
      <AutonomyPage />
    </MemoryRouter>,
  );
}

describe('AutonomyPage (Autonomy Control Center)', () => {
  beforeEach(() => {
    m(useDemoContext).mockReturnValue({ isDemoMode: false, cloudProvider: null });
    m(useApprovalQueue).mockReturnValue({ data: [] });
    m(usePlaybookEntries).mockReturnValue({ data: [], isError: false });
    m(useProviderCapabilities).mockReturnValue({ data: capabilitiesMap });
    m(useMe).mockReturnValue({ data: { ownerId: 'owner-1', authMethod: 'ApiKey', governanceRole: 'Admin' } });
    m(useNamespaces).mockReturnValue({ data: [{ id: 'ns-a', name: 'aws-dev', cloudProvider: 'aws' }, { id: 'ns-z', name: 'az-dev', cloudProvider: 'azure' }] });
    m(useGovernanceGrants).mockReturnValue({ data: [{ id: 'g1' }, { id: 'g2' }] });
    m(useOutcomeMetrics).mockReturnValue({
      data: { messagesRecovered: 12, messagesAbandoned: 1, autonomousRecoveries: 3, gateRefusals: 4, medianSecondsToVerifiedRecovery: null },
      isError: false,
    });
    m(useRecoveryOperations).mockReturnValue({ data: [] });
    m(useRecoveryEntries).mockReturnValue({ data: [] });
  });

  it('shows a loading state under the page title', () => {
    dashboard(undefined, { isLoading: true, isFetching: true });
    renderPage();
    expect(screen.getByRole('heading', { name: 'Autonomy Control Center' })).toBeInTheDocument();
    expect(screen.getByText('Loading autonomy…')).toBeInTheDocument();
  });

  it('shows an error state with a retry option', () => {
    dashboard(undefined, { isError: true });
    renderPage();
    expect(screen.getByText('Failed to load the autonomy overview')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });

  it('renders the emergency-stop banner and safety status when active', () => {
    dashboard({ ...emptyOverview, emergencyStopActive: true });
    renderPage();
    expect(screen.getByText('Emergency stop is active')).toBeInTheDocument();
    expect(screen.getAllByText('Emergency stop').length).toBeGreaterThan(0);
  });

  it('at the L3 floor, says human approval is required and explains the AWS cap as a provider fact', () => {
    dashboard();
    renderPage();
    expect(screen.getByText('Approve (L3)')).toBeInTheDocument();
    expect(screen.getByText('Human approval required')).toBeInTheDocument();
    expect(screen.getByText(/AWS cannot prove a replayed message stayed out of the dead-letter queue/)).toBeInTheDocument();
    // Azure is connected and capable, so the reason is the evidence bar, not the provider.
    expect(screen.getByText(/Standing \(L4\) needs at least 10 verified outcomes/)).toBeInTheDocument();
  });

  it('shows earned Standing (L4) trust from real level counts, with signatures listed', () => {
    dashboard({
      ...emptyOverview,
      totalSignatures: 2,
      levelCounts: [{ actionKind: 'Replay', level: 4, levelLabel: 'Standing (L4)', count: 2 }],
      grants: [{ signatureHash: 'abc123def4567890', actionKind: 'Replay', currentLevel: 4, levelLabel: 'Standing (L4)', updatedAtUtc: '2026-09-09T00:00:00Z' }],
    });
    renderPage();
    expect(screen.getAllByText('Standing (L4)').length).toBeGreaterThan(0);
    expect(screen.getByText('Earned by 2 signatures')).toBeInTheDocument();
    expect(screen.getByText('Signatures with earned trust')).toBeInTheDocument();
  });

  it('never presents an unreported provider capability as supported', () => {
    dashboard();
    m(useProviderCapabilities).mockReturnValue({ data: { Azure: capabilitiesMap.Azure, Aws: capabilitiesMap.Aws } });
    renderPage();
    expect(screen.getByText('Not reported')).toBeInTheDocument();
    const table = screen.getByRole('table', { name: 'Provider autonomy boundaries' });
    expect(table).toHaveTextContent('Unattended (L5), once earned'); // Azure only
    expect(table.textContent?.match(/Approve \(L3\) — human approval always/g)).toHaveLength(2); // AWS + GCP
  });

  it('has no global autonomy switch and states that no one can set trust', () => {
    dashboard();
    renderPage();
    expect(screen.queryByRole('button', { name: /enable autonomy|turn on autonomy|set level/i })).not.toBeInTheDocument();
    expect(screen.getByText(/There is no global autonomy switch/)).toBeInTheDocument();
  });

  it('reports real outcome metrics and the waiting-for-a-human count', () => {
    dashboard();
    m(useApprovalQueue).mockReturnValue({ data: [{ entryId: 'a' }, { entryId: 'b' }] });
    m(usePlaybookEntries).mockReturnValue({ data: [{ id: 'p1', state: 'Proposed', pillarKind: 'Investigate', proposerKind: 'System', proposalKind: 'AnomalyFlag', proposalJson: '{}' }], isError: false });
    renderPage();
    expect(screen.getByText('2 replays · 1 proposals')).toBeInTheDocument();
    expect(screen.getAllByText('12').length).toBeGreaterThan(0);
    expect(screen.getByRole('link', { name: 'Review 2 escalated replays' })).toHaveAttribute('href', '/approval-queue');
  });

  it('counts AI proposals from the ledger and keeps the AI companion advisory-only', () => {
    dashboard();
    m(usePlaybookEntries).mockReturnValue({
      data: [
        { id: 'p1', state: 'Proposed', pillarKind: 'Investigate', proposerKind: 'ReasoningAgent', proposalKind: 'ReasoningCompanionObservation', proposalJson: '{}' },
        { id: 'p2', state: 'Approved', pillarKind: 'Investigate', proposerKind: 'ReasoningAgent', proposalKind: 'ReasoningCompanionObservation', proposalJson: '{}' },
        { id: 'p3', state: 'Proposed', pillarKind: 'Investigate', proposerKind: 'System', proposalKind: 'AnomalyFlag', proposalJson: '{}' },
      ],
      isError: false,
    });
    renderPage();
    expect(screen.getByText('Advisory only')).toBeInTheDocument();
    expect(screen.getByText(/2 proposals recorded\. It can propose — never execute, approve or promote\./)).toBeInTheDocument();
  });

  it('does not request grants for a non-admin, and says grants are Admin only', () => {
    dashboard();
    m(useMe).mockReturnValue({ data: { ownerId: 'owner-1', authMethod: 'ApiKey', governanceRole: 'Viewer' } });
    renderPage();
    expect(m(useGovernanceGrants)).toHaveBeenLastCalledWith(undefined, false);
    expect(screen.getByText('Admin only')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /View governance/ })).toHaveAttribute('href', '/governance');
  });

  it('links to the Audit Trail for the full record instead of duplicating it', () => {
    dashboard();
    renderPage();
    expect(screen.getByRole('link', { name: 'Full record: Audit Trail →' })).toHaveAttribute('href', '/audit');
    expect(screen.getByText('No recoveries, trust changes or proposal decisions recorded yet.')).toBeInTheDocument();
  });

  it('never describes a gate-blocked automated attempt as having run unattended', () => {
    dashboard();
    m(useRecoveryOperations).mockReturnValue({
      data: [{ id: 'op-9', kind: 'Replay', actorKind: 'Automation', actorIdentity: 'Rule:10', scopeDescription: 'auto-replay rule 10', openedAt: '2026-09-06T20:54:00Z', entryCount: 2 }],
    });
    m(useRecoveryEntries).mockReturnValue({ data: [{ id: 'e1', operationId: 'op-9', state: 'Declined' }, { id: 'e2', operationId: 'op-9', state: 'Declined' }] });
    renderPage();
    expect(screen.getByText('Automated replay of 2 messages — blocked by the Eligibility Gate')).toBeInTheDocument();
    expect(screen.queryByText(/ran unattended/)).not.toBeInTheDocument();
  });

  it('shows recent promotions and recoveries as activity', () => {
    dashboard({
      ...emptyOverview,
      recentTransitions: [{ signatureHash: 'abcdef1234567890', actionKind: 'Replay', previousLevel: 3, newLevel: 4, reason: '10 verified at 100%', occurredAtUtc: '2026-09-09T10:00:00Z' }],
    });
    m(useRecoveryOperations).mockReturnValue({
      data: [{ id: 'op-1', kind: 'Replay', actorKind: 'Automation', actorIdentity: 'Rule:8', scopeDescription: 'rule 8', openedAt: '2026-09-09T11:00:00Z', entryCount: 5 }],
    });
    renderPage();
    expect(screen.getByText('Promoted L3 → L4')).toBeInTheDocument();
    // No entries loaded for this operation, so the line claims no outcome at all.
    expect(screen.getByText('Automated replay of 5 messages')).toBeInTheDocument();
  });

  it('shows the demo-mode banner and never claims a live ledger', () => {
    m(useDemoContext).mockReturnValue({ isDemoMode: true, cloudProvider: 'azure' });
    dashboard();
    renderPage();
    expect(screen.getByText(/Demo Mode — there is no live ledger here/)).toBeInTheDocument();
  });
});
