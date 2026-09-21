import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import PlaybookLedgerPage from '@/pages/PlaybookLedgerPage';
import {
  usePlaybookEntries, usePlaybookEntry, useMarkPlaybookEntryUnderReview, useDispositionPlaybookEntry,
  useCorrelationAccountability, useBacktestReport,
} from '@servicehub/ui-shared/hooks/usePlaybookLedger';
import { useMe } from '@servicehub/ui-shared/hooks/useMe';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';

vi.mock('@servicehub/ui-shared/hooks/usePlaybookLedger', () => ({
  usePlaybookEntries: vi.fn(),
  usePlaybookEntry: vi.fn(),
  useMarkPlaybookEntryUnderReview: vi.fn(),
  useDispositionPlaybookEntry: vi.fn(),
  useCorrelationAccountability: vi.fn(),
  useBacktestReport: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useMe', () => ({ useMe: vi.fn() }));
vi.mock('@servicehub/ui-shared/hooks/useNamespaces', () => ({ useNamespaces: vi.fn() }));
vi.mock('@servicehub/ui-shared/lib/demo/DemoContext', () => ({ useDemoContext: vi.fn() }));

const m = <T,>(fn: T) => fn as unknown as ReturnType<typeof vi.fn>;

const anomaly = {
  id: 'entry-1',
  pillarKind: 'Investigate' as const,
  proposalKind: 'AnomalyFlag',
  evidenceRefJson: '{"AnomalyId":"abc-123"}',
  proposalJson: JSON.stringify({ EntityName: 'orders', Type: 'DlqGrowthSpike', Severity: 80, Description: 'DLQ grew 4x in 10 minutes', RecommendedActions: ['Check the consumer'] }),
  proposedAt: '2026-09-10T09:00:00Z',
  proposerIdentity: 'System:AnomalyDetectionWorker',
  proposerKind: 'System' as const,
  signatureHashSnapshot: null,
  namespaceId: 'ns-1',
  namespaceNameSnapshot: 'contoso-prod',
  providerSnapshot: 'azure',
  environmentSnapshot: 'prod',
  relatedRecoveryOperationId: null,
  expiresAt: '2026-09-17T09:00:00Z',
  state: 'Proposed' as const,
  disposition: null,
  closedAt: null,
};

const correlation = {
  ...anomaly,
  id: 'entry-2',
  pillarKind: 'Correlate' as const,
  proposalKind: 'CorrelationHypothesis',
  proposalJson: JSON.stringify({ Providers: ['Azure', 'Aws'], Members: [{ EntityName: 'orders' }, { EntityName: 'payments' }] }),
  namespaceId: null,
  namespaceNameSnapshot: null,
  providerSnapshot: null,
  environmentSnapshot: null,
  state: 'Approved' as const,
  disposition: 'Approved' as const,
};

const preventionRule = {
  ...anomaly,
  id: 'entry-3',
  pillarKind: 'Prevent' as const,
  proposalKind: 'PreventionRuleProposal',
  proposalJson: JSON.stringify({ Name: 'Retry storm', EntityName: 'orders' }),
};

const disposition = vi.fn();
const markUnderReview = vi.fn();

function setup(entries: object[] | undefined, extra: object = {}) {
  m(usePlaybookEntries).mockReturnValue({ data: entries, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false, ...extra });
}

function renderPage(url = '/playbook') {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <PlaybookLedgerPage />
    </MemoryRouter>,
  );
}

const table = () => screen.getByRole('table', { name: 'Playbook Ledger entries' });

describe('PlaybookLedgerPage', () => {
  beforeEach(() => {
    disposition.mockReset();
    markUnderReview.mockReset();
    m(useDemoContext).mockReturnValue({ isDemoMode: false, cloudProvider: null });
    m(usePlaybookEntry).mockReturnValue({ data: { entry: anomaly, events: [] }, isLoading: false });
    m(useMarkPlaybookEntryUnderReview).mockReturnValue({ mutate: markUnderReview, isPending: false });
    m(useDispositionPlaybookEntry).mockReturnValue({ mutate: disposition, isPending: false });
    m(useCorrelationAccountability).mockReturnValue({ data: undefined });
    m(useBacktestReport).mockReturnValue({ data: undefined });
    m(useMe).mockReturnValue({ data: { ownerId: 'o', authMethod: 'ApiKey', governanceRole: 'Admin' } });
    m(useNamespaces).mockReturnValue({ data: [] });
  });

  it('shows a loading state under the page title', () => {
    setup(undefined, { isLoading: true, isFetching: true });
    renderPage();
    expect(screen.getByRole('heading', { name: 'Playbook Ledger' })).toBeInTheDocument();
    expect(screen.getByText('Loading proposals…')).toBeInTheDocument();
  });

  it('shows an error state', () => {
    setup(undefined, { isError: true });
    renderPage();
    expect(screen.getByText('Failed to load Playbook Ledger entries')).toBeInTheDocument();
  });

  it('shows the empty state when there are no entries', () => {
    setup([]);
    renderPage();
    expect(screen.getByText('No proposals recorded')).toBeInTheDocument();
  });

  it('renders a plain-language title instead of the raw proposal kind', () => {
    setup([anomaly]);
    renderPage();
    expect(within(table()).getByText('DLQ growth spike on orders')).toBeInTheDocument();
    expect(within(table()).getByText('Anomaly')).toBeInTheDocument();
    expect(within(table()).getByText('contoso-prod')).toBeInTheDocument();
    expect(within(table()).getByText('Awaiting decision')).toBeInTheDocument();
  });

  it('shows "Fleet-wide" for a correlation hypothesis with no namespace', () => {
    setup([correlation]);
    renderPage();
    expect(within(table()).getByText('2 related failures across Azure and Aws')).toBeInTheDocument();
    expect(within(table()).getByText('Fleet-wide')).toBeInTheDocument();
  });

  it('pre-selects the pillar from a ?pillar= deep link and filters to it', () => {
    setup([anomaly, correlation]);
    renderPage('/playbook?pillar=Correlate');
    expect(screen.getByRole('tab', { name: /Correlate/ })).toHaveAttribute('aria-selected', 'true');
    expect(within(table()).queryByText('DLQ growth spike on orders')).not.toBeInTheDocument();
    expect(screen.getByText(/share a cause/)).toBeInTheDocument();
  });

  it('ignores an invalid ?pillar= value and shows all proposals', () => {
    setup([anomaly, correlation]);
    renderPage('/playbook?pillar=NotAPillar');
    expect(screen.getByRole('tab', { name: /All proposals/ })).toHaveAttribute('aria-selected', 'true');
    expect(within(table()).getAllByRole('row')).toHaveLength(3); // header + 2
  });

  it('filters to proposals awaiting a decision from ?state=awaiting', () => {
    setup([anomaly, correlation]);
    renderPage('/playbook?state=awaiting');
    expect(within(table()).getByText('DLQ growth spike on orders')).toBeInTheDocument();
    expect(within(table()).queryByText('2 related failures across Azure and Aws')).not.toBeInTheDocument();
  });

  it('shows an "AI suggestion" badge only for reasoning-companion proposals', () => {
    setup([anomaly, { ...anomaly, id: 'entry-ai', proposalKind: 'ReasoningCompanionObservation', proposalJson: '{"Summary":"Consumer looks stalled","Considerations":["Check deploy"]}', proposerKind: 'ReasoningAgent' }]);
    renderPage();
    expect(within(table()).getAllByText('AI suggestion')).toHaveLength(1);
    expect(within(table()).getByText('Consumer looks stalled')).toBeInTheDocument();
  });

  it('reports approval rate and backtest corroboration honestly', () => {
    m(useBacktestReport).mockReturnValue({ data: { totalBacktested: 4, corroboratedCount: 3, corroborationRate: 0.75, entries: [] } });
    setup([anomaly, correlation, { ...anomaly, id: 'r', state: 'Rejected', disposition: 'Rejected' }]);
    renderPage();
    expect(screen.getByText('50% of 2 decided')).toBeInTheDocument();
    expect(screen.getByText('3/4')).toBeInTheDocument();
  });

  it('opens the detail panel, explains what approving means, and approves', () => {
    setup([anomaly]);
    renderPage();
    fireEvent.click(within(table()).getByText('DLQ growth spike on orders'));
    const panel = screen.getByRole('complementary', { name: 'Details' });
    expect(within(panel).getByText('What approving means')).toBeInTheDocument();
    expect(within(panel).getByText(/never triggers a replay or purge/)).toBeInTheDocument();
    expect(within(panel).getByText('Suggested next step')).toBeInTheDocument();
    expect(within(panel).getAllByText('Check the consumer').length).toBeGreaterThan(0);
    expect(within(panel).getByText('80/100')).toBeInTheDocument();
    fireEvent.click(within(panel).getByRole('button', { name: /Approve/ }));
    expect(disposition).toHaveBeenCalledWith({ entryId: 'entry-1', disposition: 'Approved' });
  });

  it('requires a reason to reject', () => {
    setup([anomaly]);
    renderPage('/playbook?entry=entry-1');
    fireEvent.click(screen.getByRole('button', { name: /Reject/ }));
    const confirm = screen.getByRole('button', { name: 'Confirm reject' });
    expect(confirm).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Rejection reason'), { target: { value: 'Known deploy blip' } });
    fireEvent.click(confirm);
    expect(disposition).toHaveBeenCalledWith({ entryId: 'entry-1', disposition: 'Rejected', reason: 'Known deploy blip' }, expect.anything());
  });

  it('marks a proposal under review', () => {
    setup([anomaly]);
    renderPage('/playbook?entry=entry-1');
    fireEvent.click(screen.getByRole('button', { name: /Reviewing/ }));
    expect(markUnderReview).toHaveBeenCalledWith('entry-1');
  });

  it('offers no decision on a proposal that is already decided', () => {
    setup([correlation]);
    renderPage('/playbook?entry=entry-2');
    expect(screen.getByRole('complementary', { name: 'Details' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Approve/ })).not.toBeInTheDocument();
  });

  it('says approving a prevention rule makes it observe-only', () => {
    setup([preventionRule]);
    renderPage('/playbook?entry=entry-3');
    expect(screen.getByText(/active, observe-only prevention rule/)).toBeInTheDocument();
  });

  it('warns a caller whose fleet-wide role is below Approver that the server may refuse', () => {
    m(useMe).mockReturnValue({ data: { ownerId: 'o', authMethod: 'ApiKey', governanceRole: 'Viewer' } });
    setup([anomaly]);
    renderPage('/playbook?entry=entry-1');
    expect(screen.getByText(/Deciding needs the Approver role/)).toBeInTheDocument();
  });
});
