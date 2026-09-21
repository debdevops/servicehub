import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import RecoveryLedgerPage from '@/pages/RecoveryLedgerPage';
import { useRecoveryOperations, useRecoveryEntries } from '@servicehub/ui-shared/hooks/useRecoveryLedger';
import { useRecoveryOperation, useDownloadRecoveryExport } from '@servicehub/ui-shared/hooks/useRecoveryOperation';
import { useVerifyChain } from '@servicehub/ui-shared/hooks/useChainVerification';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { RECOVERY_LIMITATION_SENTENCE } from '@servicehub/ui-shared/lib/api/recovery';

vi.mock('@servicehub/ui-shared/hooks/useRecoveryLedger', () => ({
  useRecoveryOperations: vi.fn(),
  useRecoveryEntries: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useRecoveryOperation', () => ({
  useRecoveryOperation: vi.fn(),
  useDownloadRecoveryExport: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useChainVerification', () => ({ useVerifyChain: vi.fn() }));
vi.mock('@servicehub/ui-shared/hooks/useNamespaces', () => ({ useNamespaces: vi.fn() }));
vi.mock('@servicehub/ui-shared/lib/demo/DemoContext', () => ({ useDemoContext: vi.fn() }));

const m = <T,>(fn: T) => fn as unknown as ReturnType<typeof vi.fn>;

const manualOp = {
  id: 'op-1',
  kind: 'Replay',
  trigger: 'Manual',
  actorIdentity: 'alex@contoso.com',
  actorKind: 'User',
  reason: 'INC-4471',
  namespaceId: 'ns-1',
  namespaceNameSnapshot: 'contoso-prod',
  providerSnapshot: 'azure',
  environmentSnapshot: 'prod',
  scopeDescription: 'entity=orders-dlq',
  sourceRuleId: null,
  sourceJobId: null,
  serviceVersion: '4.0.0',
  openedAt: '2026-09-10T09:00:00Z',
  targetCount: 2,
  entryCount: 2,
};

const autoOp = {
  ...manualOp,
  id: 'op-2',
  trigger: 'AutoRule',
  actorIdentity: 'Rule:8',
  actorKind: 'Automation',
  reason: 'Auto: DeserializationError',
  providerSnapshot: 'aws',
  environmentSnapshot: 'dev',
  scopeDescription: 'auto-replay rule 8',
  sourceRuleId: 8,
  openedAt: '2026-09-09T11:00:00Z',
  targetCount: 0,
  entryCount: 5,
};

function entry(operationId: string, state: string, i: number) {
  return {
    id: `${operationId}-e${i}`,
    operationId,
    dlqMessageId: i,
    namespaceId: 'ns-1',
    namespaceNameSnapshot: 'contoso-prod',
    providerSnapshot: 'azure',
    environmentSnapshot: 'prod',
    entityNameSnapshot: 'orders',
    entityTypeSnapshot: 'Queue',
    topicNameSnapshot: null,
    bodyHash: 'sha256-x',
    failureCategorySnapshot: 'TransientDependency',
    deadLetterReasonSnapshot: 'MaxDeliveryCountExceeded',
    signatureHashSnapshot: null,
    targetEntity: 'orders',
    begunAt: '2026-09-10T09:00:01Z',
    markerApplied: true,
    state,
    disposition: null,
    verificationResult: null,
    verificationConfidence: null,
    observationWindowEndsAt: null,
    closedAt: null,
  };
}

const verifyMutate = vi.fn();

function setup(ops: object[] | undefined, entries: object[] = [], extra: object = {}) {
  m(useRecoveryOperations).mockReturnValue({ data: ops, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false, ...extra });
  m(useRecoveryEntries).mockReturnValue({ data: entries });
}

function renderPage(url = '/recovery') {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <RecoveryLedgerPage />
    </MemoryRouter>,
  );
}

describe('RecoveryLedgerPage (Recovery Evidence)', () => {
  beforeEach(() => {
    verifyMutate.mockReset();
    m(useDemoContext).mockReturnValue({ isDemoMode: false, cloudProvider: null });
    m(useNamespaces).mockReturnValue({ data: [] });
    m(useVerifyChain).mockReturnValue({ mutate: verifyMutate, isPending: false, data: undefined, isError: false });
    m(useDownloadRecoveryExport).mockReturnValue({ mutate: vi.fn(), isPending: false });
    m(useRecoveryOperation).mockReturnValue({ data: undefined, isLoading: true, isError: false });
  });

  it('shows a loading state under the page title', () => {
    setup(undefined, [], { isLoading: true, isFetching: true });
    renderPage();
    expect(screen.getByRole('heading', { name: 'Recovery Evidence' })).toBeInTheDocument();
    expect(screen.getByText('Loading recoveries…')).toBeInTheDocument();
  });

  it('shows the empty state when there are no operations', () => {
    setup([]);
    renderPage();
    expect(screen.getByText('No recovery operations recorded')).toBeInTheDocument();
  });

  it('shows an error state with a retry option', () => {
    setup(undefined, [], { isError: true });
    renderPage();
    expect(screen.getByText('Failed to load recovery operations')).toBeInTheDocument();
  });

  it('renders a row with actor, scope, message count, and an outcome derived from its entries', () => {
    setup([manualOp], [entry('op-1', 'Recovered', 1), entry('op-1', 'Recovered', 2)]);
    renderPage();
    const table = screen.getByRole('table', { name: 'Recovery operations' });
    expect(within(table).getByText('alex@contoso.com')).toBeInTheDocument();
    expect(within(table).getByText('entity=orders-dlq')).toBeInTheDocument();
    expect(within(table).getByText('2')).toBeInTheDocument();
    expect(within(table).getByText('Recovered')).toBeInTheDocument();
  });

  it('shows the real entry count for an auto-replay rule tick, not its unknown-up-front target count of 0', () => {
    setup([autoOp]);
    renderPage();
    const table = screen.getByRole('table', { name: 'Recovery operations' });
    expect(within(table).getByText('5')).toBeInTheDocument();
    expect(within(table).queryByText('0')).not.toBeInTheDocument();
  });

  it('does not claim an outcome for an operation whose entries are not all loaded', () => {
    setup([autoOp], [entry('op-2', 'Recovered', 1)]);
    renderPage();
    expect(within(screen.getByRole('table', { name: 'Recovery operations' })).getByText('See details')).toBeInTheDocument();
  });

  it('computes the verified recovery rate only from entries with a verdict', () => {
    setup([manualOp, { ...autoOp, entryCount: 3 }], [
      entry('op-1', 'Recovered', 1), entry('op-1', 'Returned', 2),
      entry('op-2', 'Unverified', 3), entry('op-2', 'Unverified', 4), entry('op-2', 'Recovered', 5),
    ]);
    renderPage();
    // 2 recovered of 3 decided (1 returned); the 2 unverified are counted separately, not as failures.
    expect(screen.getByText('67% of verified outcomes')).toBeInTheDocument();
    expect(screen.getByText("Outcome couldn't be proven")).toBeInTheDocument();
  });

  it('filters by outcome and by search text', () => {
    setup([manualOp, autoOp], [entry('op-1', 'Recovered', 1), entry('op-1', 'Recovered', 2)]);
    renderPage();
    const table = () => screen.getByRole('table', { name: 'Recovery operations' });
    expect(within(table()).getByText('Rule:8')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Filter by outcome'), { target: { value: 'Recovered' } });
    expect(within(table()).queryByText('Rule:8')).not.toBeInTheDocument();
    expect(within(table()).getByText('alex@contoso.com')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Filter by outcome'), { target: { value: '' } });
    fireEvent.change(screen.getByLabelText('Search recoveries'), { target: { value: 'rule 8' } });
    expect(within(table()).queryByText('alex@contoso.com')).not.toBeInTheDocument();
    expect(within(table()).getByText('Rule:8')).toBeInTheDocument();
  });

  it('filters by actor type', () => {
    setup([manualOp, autoOp]);
    renderPage();
    fireEvent.change(screen.getByLabelText('Filter by actor'), { target: { value: 'automation' } });
    const table = screen.getByRole('table', { name: 'Recovery operations' });
    expect(within(table).queryByText('alex@contoso.com')).not.toBeInTheDocument();
  });

  it('opens the recovery story for the selected row', () => {
    setup([manualOp], [entry('op-1', 'Recovered', 1), entry('op-1', 'Recovered', 2)]);
    m(useRecoveryOperation).mockReturnValue({
      data: {
        operation: manualOp,
        entries: [entry('op-1', 'Recovered', 1), entry('op-1', 'Recovered', 2)],
        events: [
          { id: 'ev1', ownerId: 'o', seq: 1, entryId: 'op-1-e1', operationId: 'op-1', eventType: 'ProviderAccepted', occurredAt: '2026-09-10T09:00:02Z', actorIdentity: 'alex', actorKind: 'User', detailJson: null, prevHash: '', entryHash: '', schemaVersion: 1 },
          { id: 'ev2', ownerId: 'o', seq: 2, entryId: 'op-1-e2', operationId: 'op-1', eventType: 'ProviderAccepted', occurredAt: '2026-09-10T09:00:03Z', actorIdentity: 'alex', actorKind: 'User', detailJson: null, prevHash: '', entryHash: '', schemaVersion: 1 },
        ],
      },
      isLoading: false,
      isError: false,
    });
    renderPage();
    fireEvent.click(within(screen.getByRole('table', { name: 'Recovery operations' })).getByText('alex@contoso.com'));

    const panel = screen.getByRole('complementary', { name: 'Details' });
    expect(within(panel).getByText('Authorized by alex@contoso.com')).toBeInTheDocument();
    expect(within(panel).getByText('Asked Azure Service Bus to replay 2 messages')).toBeInTheDocument();
    expect(within(panel).getByText(RECOVERY_LIMITATION_SENTENCE)).toBeInTheDocument();
    expect(within(panel).getByRole('link', { name: /Open full evidence record/ })).toHaveAttribute('href', '/recovery/op-1');
  });

  it('opens directly from a ?op= deep link and closes on the close button', () => {
    setup([manualOp]);
    m(useRecoveryOperation).mockReturnValue({ data: undefined, isLoading: false, isError: true });
    renderPage('/recovery?op=missing');
    expect(screen.getByText('Recovery operation not found')).toBeInTheDocument();
    fireEvent.click(screen.getByLabelText('Close details'));
    expect(screen.queryByText('Recovery operation not found')).not.toBeInTheDocument();
  });

  it('verifies the hash chain on request, and never shows it as verified before that', () => {
    setup([manualOp, autoOp]);
    renderPage();
    expect(screen.getByText('Not verified this session')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Verify chain/ }));
    expect(verifyMutate).toHaveBeenCalledWith('op-1');
  });

  it('shows an intact chain once verification succeeds', () => {
    setup([manualOp]);
    m(useVerifyChain).mockReturnValue({ mutate: verifyMutate, isPending: false, data: { isValid: true, eventsChecked: 42, firstDivergentSeq: null, reason: null }, isError: false });
    renderPage();
    expect(screen.getByText('Chain intact · 42 events')).toBeInTheDocument();
  });

  it('shows the demo-mode notice', () => {
    m(useDemoContext).mockReturnValue({ isDemoMode: true, cloudProvider: 'azure' });
    setup([manualOp]);
    renderPage();
    expect(screen.getByText(/Demo Mode — this is fixture data/)).toBeInTheDocument();
  });
});
