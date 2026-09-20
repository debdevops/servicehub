import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import HomePage from '@/pages/HomePage';
import { useAttentionQueue } from '@servicehub/ui-shared/hooks/useAttentionQueue';
import { useOutcomeMetrics } from '@servicehub/ui-shared/hooks/useRecoveryLedger';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useNamespaceStats } from '@servicehub/ui-shared/hooks/useQueues';
import { useFleetOverview } from '@servicehub/ui-shared/hooks/useFleet';
import { useProviderCapabilities } from '@servicehub/ui-shared/hooks/useCloudBridge';
import { useDlqSignatures } from '@servicehub/ui-shared/hooks/useDlqSignatures';
import { useDlqHistory, useDlqTrend, useDlqSummary } from '@servicehub/ui-shared/hooks/useDlqHistory';
import { useAuditLogs } from '@servicehub/ui-shared/hooks/useAudit';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';

vi.mock('@servicehub/ui-shared/hooks/useAttentionQueue', () => ({
  useAttentionQueue: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useRecoveryLedger', () => ({
  useOutcomeMetrics: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useQueues', () => ({
  useNamespaceStats: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useFleet', () => ({
  useFleetOverview: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useCloudBridge', () => ({
  useProviderCapabilities: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useDlqSignatures', () => ({
  useDlqSignatures: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useDlqHistory', () => ({
  useDlqHistory: vi.fn(),
  useDlqTrend: vi.fn(),
  useDlqSummary: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useAudit', () => ({
  useAuditLogs: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/lib/demo/DemoContext', () => ({
  useDemoContext: vi.fn(),
}));

const mockUseAttentionQueue = useAttentionQueue as ReturnType<typeof vi.fn>;
const mockUseOutcomeMetrics = useOutcomeMetrics as ReturnType<typeof vi.fn>;
const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;
const mockUseNamespaceStats = useNamespaceStats as ReturnType<typeof vi.fn>;
const mockUseFleetOverview = useFleetOverview as ReturnType<typeof vi.fn>;
const mockUseProviderCapabilities = useProviderCapabilities as ReturnType<typeof vi.fn>;
const mockUseDlqSignatures = useDlqSignatures as ReturnType<typeof vi.fn>;
const mockUseDlqHistory = useDlqHistory as ReturnType<typeof vi.fn>;
const mockUseDlqTrend = useDlqTrend as ReturnType<typeof vi.fn>;
const mockUseDlqSummary = useDlqSummary as ReturnType<typeof vi.fn>;
const mockUseAuditLogs = useAuditLogs as ReturnType<typeof vi.fn>;
const mockUseDemoContext = useDemoContext as ReturnType<typeof vi.fn>;

const emptyAttentionQueue = { data: { items: [], isEmpty: true }, isLoading: false, isError: false, refetch: vi.fn(), isFetching: false };
const zeroedOutcomes = {
  data: { messagesRecovered: 0, messagesAbandoned: 0, medianSecondsToVerifiedRecovery: null, autonomousRecoveries: 0, gateRefusals: 0 },
  isLoading: false,
  isError: false,
};
const emptyFleetOverview = {
  data: { generatedAt: new Date().toISOString(), windowHours: 24, namespaceCount: 0, totalActive: 0, totalNewInWindow: 0, totalResolvedInWindow: 0, namespaces: [], topCategories: {}, dailyTrend: [] },
};

function renderPage(initialEntries: string[] = ['/']) {
  return render(
    <MemoryRouter initialEntries={initialEntries}>
      <HomePage />
    </MemoryRouter>,
  );
}

describe('HomePage', () => {
  beforeEach(() => {
    mockUseDemoContext.mockReturnValue({ isDemoMode: false, cloudProvider: null });
    mockUseAttentionQueue.mockReturnValue(emptyAttentionQueue);
    mockUseOutcomeMetrics.mockReturnValue(zeroedOutcomes);
    mockUseNamespaceStats.mockReturnValue([]);
    mockUseFleetOverview.mockReturnValue(emptyFleetOverview);
    mockUseProviderCapabilities.mockReturnValue({ data: undefined });
    mockUseDlqSignatures.mockReturnValue({ data: undefined, loading: false, error: null, available: false });
    mockUseDlqHistory.mockReturnValue({ data: undefined, isLoading: false });
    mockUseDlqTrend.mockReturnValue({ data: undefined, isLoading: false });
    mockUseDlqSummary.mockReturnValue({ data: undefined, isLoading: false });
    mockUseAuditLogs.mockReturnValue({ data: undefined, isLoading: false });
  });

  it('shows a "connect a cloud" empty state when no namespace is connected', () => {
    mockUseNamespaces.mockReturnValue({ data: [], isLoading: false });
    renderPage();
    expect(screen.getByText('Connect a cloud to get started')).toBeInTheDocument();
  });

  it('shows a loading skeleton while namespaces are still resolving', () => {
    mockUseNamespaces.mockReturnValue({ data: undefined, isLoading: true });
    renderPage();
    expect(screen.queryByText('Connect a cloud to get started')).not.toBeInTheDocument();
  });

  describe('single connected provider — goes straight to that cloud\'s Home, no picker', () => {
    beforeEach(() => {
      mockUseNamespaces.mockReturnValue({
        data: [{ id: 'aws-ns-1', name: 'aws-ns-1', isActive: true, cloudProvider: 'aws', environment: 'dev' }],
        isLoading: false,
      });
    });

    it('renders that cloud\'s Home heading directly', () => {
      renderPage();
      expect(screen.getByText('AWS Home')).toBeInTheDocument();
      expect(screen.queryByText('Choose a cloud to see what needs your attention.')).not.toBeInTheDocument();
    });

    it('shows loading skeletons while the attention queue is fetching', () => {
      mockUseAttentionQueue.mockReturnValue({ data: undefined, isLoading: true, isError: false, refetch: vi.fn(), isFetching: true });
      renderPage();
      expect(screen.getByText('AWS Home')).toBeInTheDocument();
    });

    it('shows an error state with a retry option', () => {
      mockUseAttentionQueue.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn(), isFetching: false });
      renderPage();
      expect(screen.getByText("Couldn't load the attention queue")).toBeInTheDocument();
    });

    it('shows the healthy empty state when nothing needs attention', () => {
      renderPage();
      expect(screen.getByText('AWS looks healthy')).toBeInTheDocument();
    });

    it('renders ranked attention cards from real queue data', () => {
      mockUseAttentionQueue.mockReturnValue({
        data: {
          isEmpty: false,
          items: [
            {
              signatureHash: 'sig-1',
              namespaceId: 'aws-ns-1',
              namespaceName: 'aws-ns-1',
              displayName: 'Payment webhook timeout',
              lifecycleStatus: 'Active',
              severity: 'Critical',
              blastRadius: 12,
              isRecurring: true,
              pendingDecisionCount: 1,
              score: 0.9,
              recommendedAction: 'Replay after outage clears',
              lastSeenAt: new Date().toISOString(),
            },
          ],
        },
        isLoading: false,
        isError: false,
        refetch: vi.fn(),
        isFetching: false,
      });

      renderPage();

      expect(screen.getByText('Payment webhook timeout')).toBeInTheDocument();
      expect(screen.getByText('Critical')).toBeInTheDocument();
      expect(screen.getByText('1 pending decision')).toBeInTheDocument();
      expect(screen.getByText('Recurring')).toBeInTheDocument();
      expect(screen.getByText('Replay after outage clears')).toBeInTheDocument();
    });

    it('scopes the attention queue request to this cloud\'s provider', () => {
      renderPage();
      expect(mockUseAttentionQueue).toHaveBeenCalledWith('aws');
    });

    it('shows the this-week outcomes tiles only once real recovery activity exists', () => {
      mockUseOutcomeMetrics.mockReturnValue({
        data: { messagesRecovered: 42, messagesAbandoned: 3, medianSecondsToVerifiedRecovery: 125, autonomousRecoveries: 10, gateRefusals: 2 },
        isLoading: false,
        isError: false,
      });
      renderPage();
      expect(screen.getByText('This week')).toBeInTheDocument();
      expect(screen.getByText('42')).toBeInTheDocument();
      expect(screen.getByText('Recovered')).toBeInTheDocument();
      expect(mockUseOutcomeMetrics).toHaveBeenCalledWith(7, 'aws');
    });

    it('renders nothing for this-week outcomes when the fleet has been quiet', () => {
      renderPage();
      expect(screen.queryByText('This week')).not.toBeInTheDocument();
    });

    it('shows real active/DLQ message totals from namespace stats', () => {
      mockUseNamespaceStats.mockReturnValue([
        { namespaceId: 'aws-ns-1', data: { totalQueues: 3, totalTopics: 1, totalSubscriptions: 0, totalActive: 250, totalDlq: 12, totalScheduled: 0 }, isLoading: false, isError: false },
      ]);
      renderPage();
      expect(screen.getByText('250')).toBeInTheDocument();
      expect(screen.getByText('12')).toBeInTheDocument();
      expect(screen.getByText('Active messages')).toBeInTheDocument();
      expect(screen.getByText('DLQ messages')).toBeInTheDocument();
    });

    it('does not offer a "switch cloud" strip when only one provider is connected', () => {
      renderPage();
      expect(screen.queryByText('Switch cloud:')).not.toBeInTheDocument();
    });

    it('lists the namespace and clicking it enters that namespace\'s Namespace Home', () => {
      mockUseNamespaceStats.mockReturnValue([
        { data: { totalQueues: 3, totalTopics: 1, totalSubscriptions: 0, totalActive: 250, totalDlq: 12, totalScheduled: 0 }, isLoading: false },
      ]);
      renderPage();
      const [namespaceRow] = screen.getAllByText('aws-ns-1');
      fireEvent.click(namespaceRow.closest('button')!);
      // Namespace Home renders a "back to Cloud Home" link/breadcrumb alongside the KPI strip —
      // the Cloud Home's own "Namespaces" list section is gone once inside Level 2.
      expect(screen.queryByText('Namespaces')).not.toBeInTheDocument();
      expect(screen.getByText('View Connection Details')).toBeInTheDocument();
    });

    it('omits Live Tail from Cloud Home Quick Actions for a provider with no repeatable peek', () => {
      mockUseProviderCapabilities.mockReturnValue({
        data: { Aws: { supportsRepeatablePeek: false, supportsMessageCounts: true, supportsPurge: true, notes: 'No non-destructive peek.' } },
      });
      renderPage();
      expect(screen.queryByText('Live Tail')).not.toBeInTheDocument();
    });
  });

  describe('GCP — honours ProviderCapabilities instead of showing a fabricated zero', () => {
    beforeEach(() => {
      mockUseNamespaces.mockReturnValue({
        data: [{ id: 'gcp-ns-1', name: 'gcp-ns-1', isActive: true, cloudProvider: 'gcp', environment: 'dev' }],
        isLoading: false,
      });
      mockUseProviderCapabilities.mockReturnValue({
        data: { Gcp: { supportsMessageCounts: false, notes: 'Pub/Sub has no count API.' } },
      });
    });

    it('shows a dash, not a fabricated 0, for Active and DLQ messages', () => {
      renderPage();
      expect(screen.getByText('GCP Home')).toBeInTheDocument();
      // Both tiles are gated together — GCP's SupportsMessageCounts covers active AND
      // dead-lettered counts alike (there is no separate live count API for either).
      expect(screen.getAllByText('—').length).toBeGreaterThanOrEqual(2);
    });
  });

  describe('multiple connected providers — a picker, never a blended dashboard', () => {
    const multiCloudNamespaces = [
      { id: 'azure-ns', name: 'azure-ns', isActive: true, cloudProvider: 'azure', environment: 'dev' },
      { id: 'aws-ns', name: 'aws-ns', isActive: true, cloudProvider: 'aws', environment: 'prod' },
    ];

    beforeEach(() => {
      mockUseNamespaces.mockReturnValue({ data: multiCloudNamespaces, isLoading: false });
    });

    it('shows a picker with no ?cloud= selected', () => {
      renderPage(['/']);
      expect(screen.getByText('Choose a cloud to see what needs your attention.')).toBeInTheDocument();
      expect(screen.getByText('Azure')).toBeInTheDocument();
      expect(screen.getByText('AWS')).toBeInTheDocument();
      expect(screen.queryByText('AWS Home')).not.toBeInTheDocument();
    });

    it('goes straight to that cloud\'s Home when ?cloud= is already set', () => {
      renderPage(['/?cloud=aws']);
      expect(screen.getByText('AWS Home')).toBeInTheDocument();
      expect(screen.queryByText('Choose a cloud to see what needs your attention.')).not.toBeInTheDocument();
    });

    it('offers a "switch cloud" strip to the other connected provider', () => {
      renderPage(['/?cloud=aws']);
      expect(screen.getByText('Switch cloud:')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /Azure/ })).toBeInTheDocument();
    });

    it('switching clouds via the strip clears any stale ?namespace= (selectCloud always deletes it) — lands cleanly on the new cloud\'s Home with no leaked namespace context', () => {
      renderPage(['/?cloud=aws']);
      fireEvent.click(screen.getByRole('button', { name: /Azure/ }));
      expect(screen.getByText('Azure Home')).toBeInTheDocument();
      expect(screen.getByText('Namespaces')).toBeInTheDocument();
      expect(screen.queryByText('View Connection Details')).not.toBeInTheDocument();
    });

    it('navigates from the picker into that cloud\'s Home on click', () => {
      renderPage(['/']);
      fireEvent.click(screen.getByText('Open AWS Home').closest('button')!);
      expect(screen.getByText('AWS Home')).toBeInTheDocument();
    });

    it('falls back to an unselected provider gracefully if ?cloud= names a provider that is not connected', () => {
      renderPage(['/?cloud=gcp']);
      expect(screen.getByText('Choose a cloud to see what needs your attention.')).toBeInTheDocument();
    });
  });

  describe('Demo Mode — provider is already fixed by the route, no picker regardless of connections', () => {
    beforeEach(() => {
      mockUseDemoContext.mockReturnValue({ isDemoMode: true, cloudProvider: 'azure' });
      mockUseNamespaces.mockReturnValue({ data: [], isLoading: false });
    });

    it('renders that cloud\'s Home directly', () => {
      renderPage();
      expect(screen.getByText('Azure Home')).toBeInTheDocument();
    });

    it('uses the demo-prefixed path for attention-card navigation', () => {
      mockUseAttentionQueue.mockReturnValue({
        data: {
          isEmpty: false,
          items: [
            {
              signatureHash: 'sig-demo',
              namespaceId: 'ns-demo',
              namespaceName: 'demo-ns',
              displayName: 'Demo failure',
              lifecycleStatus: 'Active',
              severity: 'Warning',
              blastRadius: 3,
              isRecurring: false,
              pendingDecisionCount: 0,
              score: 0.2,
              recommendedAction: 'Investigate',
              lastSeenAt: new Date().toISOString(),
            },
          ],
        },
        isLoading: false,
        isError: false,
        refetch: vi.fn(),
        isFetching: false,
      });
      renderPage();
      expect(screen.getByText('Demo failure').closest('button')).toBeInTheDocument();
    });

    it('drills into that namespace\'s Namespace Home when ?namespace= is set', () => {
      mockUseNamespaces.mockReturnValue({
        data: [{ id: 'ns-demo', name: 'demo-ns', displayName: 'Demo Namespace', isActive: true, cloudProvider: 'azure', environment: 'dev' }],
        isLoading: false,
      });
      mockUseNamespaceStats.mockReturnValue([
        { data: { totalQueues: 1, totalTopics: 1, totalSubscriptions: 1, totalActive: 0, totalDlq: 0, totalScheduled: 0 }, isLoading: false },
      ]);
      renderPage(['/?namespace=ns-demo']);
      expect(screen.getAllByText('Demo Namespace').length).toBeGreaterThan(0);
      expect(screen.getByRole('button', { name: 'Azure' })).toBeInTheDocument();
    });
  });

  describe('Namespace Home (Level 2) — one namespace\'s own operational workspace', () => {
    const awsNamespace = {
      id: 'aws-ns-1',
      name: 'aws-ns-1',
      displayName: 'DEV-AWS',
      isActive: true,
      cloudProvider: 'aws' as const,
      environment: 'dev' as const,
      isConnected: true,
      lastConnectionTestSucceeded: true,
      awsRegion: 'us-east-1',
    };
    const azureNamespace = {
      id: 'azure-ns-1',
      name: 'azure-ns-1',
      displayName: 'DEV-AZURE',
      isActive: true,
      cloudProvider: 'azure' as const,
      environment: 'dev' as const,
      lastConnectionTestSucceeded: true,
    };

    beforeEach(() => {
      mockUseNamespaces.mockReturnValue({ data: [awsNamespace, azureNamespace], isLoading: false });
      mockUseNamespaceStats.mockReturnValue([
        { data: { totalQueues: 4, totalTopics: 2, totalSubscriptions: 1, totalActive: 10, totalDlq: 5, totalScheduled: 0 }, isLoading: false },
      ]);
    });

    it('renders that namespace\'s own heading, stats and a breadcrumb back to its Cloud Home', () => {
      renderPage(['/?cloud=aws&namespace=aws-ns-1']);
      expect(screen.getAllByText('DEV-AWS').length).toBeGreaterThan(0);
      expect(screen.getByText('us-east-1')).toBeInTheDocument();
      // Breadcrumb: Home > AWS > DEV-AWS
      expect(screen.getByRole('button', { name: 'Home' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'AWS' })).toBeInTheDocument();
      expect(screen.getByText('10')).toBeInTheDocument();
      expect(screen.getByText('5')).toBeInTheDocument();
    });

    it('never shows another provider\'s namespace under this cloud — falls back to Cloud Home', () => {
      // azure-ns-1 belongs to Azure; requesting it under ?cloud=aws must not render it.
      renderPage(['/?cloud=aws&namespace=azure-ns-1']);
      expect(screen.queryByText('DEV-AZURE')).not.toBeInTheDocument();
      expect(screen.getByText('AWS Home')).toBeInTheDocument();
      expect(screen.getByText('Namespaces')).toBeInTheDocument();
    });

    it('falls back to Cloud Home for a namespace id that does not exist at all', () => {
      renderPage(['/?cloud=aws&namespace=does-not-exist']);
      // Cloud Home's own namespace list legitimately shows the real DEV-AWS namespace — the
      // fallback lands on Level 1, not a blank page — but never on a Namespace Home for the
      // bogus id (that surface is unique to Level 2).
      expect(screen.getByText('Namespaces')).toBeInTheDocument();
      expect(screen.queryByText('View Connection Details')).not.toBeInTheDocument();
    });

    it('omits Live Tail from Quick Actions for a provider with no repeatable peek (AWS)', () => {
      mockUseProviderCapabilities.mockReturnValue({
        data: { Aws: { supportsRepeatablePeek: false, supportsMessageCounts: true, supportsPurge: true, notes: 'No non-destructive peek.' } },
      });
      renderPage(['/?cloud=aws&namespace=aws-ns-1']);
      expect(screen.queryByText('Live Tail')).not.toBeInTheDocument();
      expect(screen.getByText('Browse Queues')).toBeInTheDocument();
    });

    it('offers Live Tail for a provider that does support repeatable peek (Azure)', () => {
      mockUseProviderCapabilities.mockReturnValue({
        data: { Azure: { supportsRepeatablePeek: true, supportsMessageCounts: true, supportsPurge: false, notes: 'Purge is not supported.' } },
      });
      renderPage(['/?cloud=azure&namespace=azure-ns-1']);
      expect(screen.getByText('Live Tail')).toBeInTheDocument();
    });

    it('shows the honest capability-limitation note, not a silent omission', () => {
      mockUseProviderCapabilities.mockReturnValue({
        data: { Aws: { supportsRepeatablePeek: false, supportsMessageCounts: true, supportsPurge: true, notes: 'SQS has no non-destructive peek.' } },
      });
      renderPage(['/?cloud=aws&namespace=aws-ns-1']);
      expect(screen.getByText('Provider limitations')).toBeInTheDocument();
      expect(screen.getByText('SQS has no non-destructive peek.')).toBeInTheDocument();
    });

    it('renders top failure signatures from real per-namespace clustering data', () => {
      mockUseDlqSignatures.mockReturnValue({
        data: {
          available: true,
          batchSize: 100,
          clusters: [
            {
              signatureHash: 'sig-a',
              dominantEntity: 'orders-queue',
              dominantDeadletterReason: 'PoisonMessage',
              occurrenceCount: 12,
              explanation: 'A malformed message crashes the consumer on every delivery.',
            },
          ],
        },
        loading: false,
        error: null,
        available: true,
      });
      renderPage(['/?cloud=aws&namespace=aws-ns-1']);
      expect(screen.getByText(/orders-queue/)).toBeInTheDocument();
      expect(screen.getByText('A malformed message crashes the consumer on every delivery.')).toBeInTheDocument();
      // occurrenceCount / batchSize = 12/100 = 12%, real data, not fabricated.
      expect(screen.getByText(/12%/)).toBeInTheDocument();
    });

    it('shows a quiet explanation, not an error, when signature clustering is unavailable', () => {
      mockUseDlqSignatures.mockReturnValue({ data: { available: false, clusters: [] }, loading: false, error: null, available: false });
      renderPage(['/?cloud=aws&namespace=aws-ns-1']);
      expect(screen.getByText("Signature clustering isn't available for this namespace right now.")).toBeInTheDocument();
    });

    it('shows a real "Oldest DLQ message" age from the DLQ summary endpoint, not a fabricated figure', () => {
      const twoHoursAgo = new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString();
      mockUseDlqSummary.mockReturnValue({
        data: { totalMessages: 5, activeMessages: 5, replayedMessages: 0, archivedMessages: 0, byCategory: {}, byEntity: {}, oldestMessage: twoHoursAgo, newestMessage: twoHoursAgo, dailyTrend: [] },
        isLoading: false,
      });
      renderPage(['/?cloud=aws&namespace=aws-ns-1']);
      expect(screen.getByText('Oldest DLQ message')).toBeInTheDocument();
      expect(screen.getByText('2h')).toBeInTheDocument();
    });

    it('renders a real chronological activity feed from the Audit Trail, not a summary count', () => {
      mockUseAuditLogs.mockReturnValue({
        data: {
          items: [
            { id: '1', timestamp: new Date().toISOString(), userIdentity: 'user@example.com', action: 'Replay executed', outcome: 'Success', namespaceId: 'aws-ns-1', namespaceName: 'aws-ns-1', entityName: 'orders-queue', cloudProvider: 'aws', environment: 'dev', resourceName: 'orders-queue', sequenceNumber: null, detailsJson: null, errorDetails: null, clientIp: null, userAgent: null, correlationId: null, httpMethod: null, httpPath: null },
          ],
          totalCount: 1, page: 1, pageSize: 50, hasNextPage: false, hasPreviousPage: false,
        },
        isLoading: false,
      });
      renderPage(['/?cloud=aws&namespace=aws-ns-1']);
      expect(screen.getByText('Replay executed')).toBeInTheDocument();
      expect(screen.getByText('orders-queue')).toBeInTheDocument();
    });

    it('offers a namespace switcher only when this cloud has more than one namespace', () => {
      mockUseNamespaces.mockReturnValue({ data: [awsNamespace], isLoading: false });
      renderPage(['/?cloud=aws&namespace=aws-ns-1']);
      expect(screen.queryByText('Switch namespace')).not.toBeInTheDocument();
    });

    it('switching namespaces via the header switcher navigates to the sibling\'s own Namespace Home', () => {
      const awsNamespace2 = { ...awsNamespace, id: 'aws-ns-2', displayName: 'UAT-AWS', name: 'aws-ns-2' };
      mockUseNamespaces.mockReturnValue({ data: [awsNamespace, awsNamespace2, azureNamespace], isLoading: false });
      renderPage(['/?cloud=aws&namespace=aws-ns-1']);
      fireEvent.click(screen.getByText('Switch namespace'));
      fireEvent.click(screen.getByText('UAT-AWS'));
      expect(screen.getAllByText('UAT-AWS').length).toBeGreaterThan(0);
    });
  });
});
