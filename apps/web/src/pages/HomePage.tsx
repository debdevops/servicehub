import { useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { LineChart, Line, ResponsiveContainer } from 'recharts';
import {
  CheckCircle2,
  AlertTriangle,
  TrendingUp,
  Clock,
  RefreshCw,
  ShieldCheck,
  ShieldOff,
  Bot,
  Ban,
  Plug,
  ChevronRight,
  Inbox,
  Radio,
  Layers,
  Zap,
  CheckCircle,
  ArrowRight,
  MapPin,
  BarChart3,
  Info,
  ChevronDown,
} from 'lucide-react';
import { useAttentionQueue, type AttentionQueueItem } from '@servicehub/ui-shared/hooks/useAttentionQueue';
import { useOutcomeMetrics } from '@servicehub/ui-shared/hooks/useRecoveryLedger';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useNamespaceStats } from '@servicehub/ui-shared/hooks/useQueues';
import { useFleetOverview } from '@servicehub/ui-shared/hooks/useFleet';
import { useProviderCapabilities } from '@servicehub/ui-shared/hooks/useCloudBridge';
import { getProviderCapabilities } from '@servicehub/ui-shared/lib/api/cloudBridge';
import { useDlqSignatures } from '@servicehub/ui-shared/hooks/useDlqSignatures';
import { useDlqHistory, useDlqTrend, useDlqSummary } from '@servicehub/ui-shared/hooks/useDlqHistory';
import { useAuditLogs } from '@servicehub/ui-shared/hooks/useAudit';
import type { AuditLogItem } from '@servicehub/ui-shared/lib/api/audit';
import { getProviderStyle } from '@servicehub/ui-shared/lib/providerStyles';
import { ProviderIcon } from '@servicehub/ui-shared/components/ProviderIcon';
import { setThemeProvider } from '@servicehub/ui-shared/lib/providerTheme';
import { formatRelativeTime } from '@servicehub/ui-shared/lib/utils';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import type { CloudProviderType, Namespace } from '@servicehub/ui-shared/lib/api/types';
import type { FleetNamespaceHealth } from '@servicehub/ui-shared/lib/api/fleet';
import { EmptyState } from '@/components/EmptyState';
import { EnvironmentBadge } from '@/components/EnvironmentBadge';

/** "1h 24m", "3d 2h", "42s" — coarsest two units, never more precise than seconds. */
function formatDuration(totalSeconds: number): string {
  const seconds = Math.max(0, Math.round(totalSeconds));
  const units: [string, number][] = [
    ['d', 86400],
    ['h', 3600],
    ['m', 60],
  ];
  for (let i = 0; i < units.length; i++) {
    const [label, size] = units[i];
    if (seconds >= size) {
      const whole = Math.floor(seconds / size);
      const remainder = seconds % size;
      const [nextLabel, nextSize] = units[i + 1] ?? ['s', 1];
      const nextWhole = Math.floor(remainder / nextSize);
      return nextWhole > 0 ? `${whole}${label} ${nextWhole}${nextLabel}` : `${whole}${label}`;
    }
  }
  return `${seconds}s`;
}

/**
 * What ServiceHub achieved this week for this cloud (roadmap next-chapter M4.1), never how
 * autonomous it is. Every figure comes straight from the outcome-metrics endpoint, scoped to
 * `provider` server-side — see OutcomeMetricsController/Service's provider filter. Renders
 * nothing (not even a zero-state) until this cloud has actually recovered or abandoned something
 * in the window, since an all-zero row reads as "broken" rather than "quiet" on a fresh install.
 */
function OutcomesThisWeek({ provider }: { provider: CloudProviderType }) {
  const { data, isLoading, isError } = useOutcomeMetrics(7, provider);

  if (isLoading || isError || !data) return null;
  if (data.messagesRecovered === 0 && data.messagesAbandoned === 0 && data.gateRefusals === 0) return null;

  const tiles = [
    {
      icon: ShieldCheck,
      color: 'text-emerald-600 bg-emerald-50',
      label: 'Recovered',
      value: data.messagesRecovered.toLocaleString(),
    },
    {
      icon: ShieldOff,
      color: 'text-gray-600 bg-gray-100',
      label: 'Written off',
      value: data.messagesAbandoned.toLocaleString(),
    },
    {
      icon: Clock,
      color: 'text-sky-600 bg-sky-50',
      label: 'Median time to recovered',
      value: data.medianSecondsToVerifiedRecovery != null ? formatDuration(data.medianSecondsToVerifiedRecovery) : '—',
    },
    {
      icon: Bot,
      color: 'text-indigo-600 bg-indigo-50',
      label: 'No human approval needed',
      value: data.autonomousRecoveries.toLocaleString(),
    },
    {
      icon: Ban,
      color: 'text-red-600 bg-red-50',
      label: 'Bad replays refused',
      value: data.gateRefusals.toLocaleString(),
    },
  ];

  return (
    <div className="mb-6 bg-white border border-gray-200 rounded-lg p-4">
      <p className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-3">This week</p>
      <div className="grid grid-cols-2 sm:grid-cols-5 gap-4">
        {tiles.map((tile) => (
          <div key={tile.label} className="flex items-start gap-2.5">
            <span className={`shrink-0 w-8 h-8 rounded-lg flex items-center justify-center ${tile.color}`}>
              <tile.icon className="w-4 h-4" />
            </span>
            <div className="min-w-0">
              <p className="text-lg font-semibold text-gray-900 leading-tight">{tile.value}</p>
              <p className="text-xs text-gray-500 leading-tight">{tile.label}</p>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

const SEVERITY_STYLES: Record<AttentionQueueItem['severity'], { bg: string; text: string; border: string; dot: string }> = {
  Critical: { bg: 'bg-red-50', text: 'text-red-700', border: 'border-red-200', dot: 'bg-red-500' },
  Warning: { bg: 'bg-amber-50', text: 'text-amber-700', border: 'border-amber-200', dot: 'bg-amber-500' },
  Healthy: { bg: 'bg-green-50', text: 'text-green-700', border: 'border-green-200', dot: 'bg-green-500' },
  Unknown: { bg: 'bg-gray-50', text: 'text-gray-600', border: 'border-gray-200', dot: 'bg-gray-400' },
};

function AttentionCard({ item, navPrefix }: { item: AttentionQueueItem; navPrefix: string }) {
  const navigate = useNavigate();
  const severity = SEVERITY_STYLES[item.severity] ?? SEVERITY_STYLES.Unknown;
  const isBlocked = item.pendingDecisionCount > 0;

  return (
    <button
      onClick={() => navigate(`${navPrefix}/incidents/${item.signatureHash}?namespace=${item.namespaceId}`)}
      className={`group text-left w-full bg-white border-2 rounded-lg p-5 hover:shadow-md hover:border-primary-400 transition-all focus:outline-none focus:ring-2 focus:ring-primary-500 ${
        isBlocked ? 'border-primary-300' : severity.border
      }`}
    >
      <div className="flex items-start justify-between gap-3 mb-3">
        <span className={`inline-flex items-center gap-1.5 px-2 py-1 rounded-full text-xs font-semibold ${severity.bg} ${severity.text}`}>
          <span className={`w-1.5 h-1.5 rounded-full ${severity.dot}`} />
          {item.severity}
        </span>
        {isBlocked && (
          <span className="inline-flex items-center gap-1 px-2 py-1 rounded-full text-xs font-semibold bg-primary-100 text-primary-700">
            {item.pendingDecisionCount} pending {item.pendingDecisionCount === 1 ? 'decision' : 'decisions'}
          </span>
        )}
      </div>

      <h3 className="font-semibold text-gray-900 mb-1 line-clamp-2">{item.displayName}</h3>
      {item.namespaceName && <p className="text-xs text-gray-500 mb-3">{item.namespaceName}</p>}

      <div className="flex items-center gap-4 text-xs text-gray-500 mb-4">
        <span className="flex items-center gap-1">
          <AlertTriangle className="w-3.5 h-3.5" />
          {item.blastRadius} message{item.blastRadius === 1 ? '' : 's'}
        </span>
        {item.isRecurring && (
          <span className="flex items-center gap-1 text-red-600 font-medium">
            <TrendingUp className="w-3.5 h-3.5" />
            Recurring
          </span>
        )}
        <span className="flex items-center gap-1">
          <Clock className="w-3.5 h-3.5" />
          {formatRelativeTime(new Date(item.lastSeenAt))}
        </span>
      </div>

      <div className="pt-3 border-t border-gray-100 flex items-end justify-between gap-3">
        <div className="min-w-0">
          <p className="text-xs font-semibold text-gray-500 mb-0.5">Recommended</p>
          <p className="text-sm font-medium text-gray-900 truncate">{item.recommendedAction}</p>
        </div>
        <span className="shrink-0 flex items-center gap-0.5 text-xs font-medium text-primary-600 group-hover:text-primary-700">
          View
          <ChevronRight className="w-3.5 h-3.5 transition-transform group-hover:translate-x-0.5" />
        </span>
      </div>
    </button>
  );
}

function KpiTile({ label, value, hint }: { label: string; value: string; hint?: string }) {
  return (
    <div className="bg-white border border-gray-200 rounded-lg p-4" title={hint}>
      <p className="text-2xl font-semibold text-gray-900 leading-none">{value}</p>
      <p className="text-xs text-gray-500 mt-1.5">{label}</p>
    </div>
  );
}

const COVERAGE_LABELS: Record<FleetNamespaceHealth['coverage'], string> = {
  scanned: 'Scanned',
  notMonitored: 'Not monitored',
  providerNotRegistered: 'Provider not registered',
};

/** One namespace's line in "Recent DLQ activity" — real FleetOverview data (the same source
 * Fleet Health renders from), filtered to this cloud, never a fabricated message-level feed.
 * Clicking drills into that namespace's own Namespace Home, same destination as clicking it in
 * the "Namespaces" list above — this list must never look clickable without behaving that way. */
function RecentDlqRow({ health, href }: { health: FleetNamespaceHealth; href: string }) {
  const navigate = useNavigate();
  const isScanned = health.coverage === 'scanned';
  return (
    <button
      type="button"
      onClick={() => navigate(href)}
      className="group w-full flex items-center justify-between gap-3 py-2.5 px-1 border-b border-gray-100 last:border-b-0 text-left hover:bg-gray-50 transition-colors focus:outline-none focus:ring-2 focus:ring-inset focus:ring-primary-500 rounded"
    >
      <div className="min-w-0">
        <p className="text-sm font-medium text-gray-900 truncate">{health.namespaceName}</p>
        <p className="text-xs text-gray-500 truncate">
          {isScanned
            ? health.topEntity
              ? `${health.topEntity}${health.topCategory ? ` · ${health.topCategory}` : ''}`
              : 'No dead-letters observed'
            : (health.coverageNote ?? COVERAGE_LABELS[health.coverage])}
        </p>
      </div>
      <div className="flex items-center gap-2 shrink-0">
        {isScanned && health.newInWindow > 0 && (
          <span className="px-2 py-0.5 text-xs font-semibold rounded-full bg-red-100 text-red-700">+{health.newInWindow} new</span>
        )}
        {isScanned && (
          <span
            className={`px-2 py-0.5 text-xs font-semibold rounded-full ${
              health.activeCount > 0 ? 'bg-amber-100 text-amber-700' : 'bg-gray-100 text-gray-500'
            }`}
          >
            {health.activeCount} active
          </span>
        )}
        <ChevronRight className="w-4 h-4 text-gray-400 transition-all group-hover:text-primary-600 group-hover:translate-x-0.5" />
      </div>
    </button>
  );
}

interface QuickActionLink {
  label: string;
  to: string;
  icon: typeof Inbox;
}

function QuickActions({ links }: { links: QuickActionLink[] }) {
  const navigate = useNavigate();
  return (
    <div className="bg-white border border-gray-200 rounded-lg divide-y divide-gray-100">
      {links.map((link) => (
        <button
          key={link.label}
          onClick={() => navigate(link.to)}
          className="group w-full flex items-center gap-3 px-4 py-3 text-left hover:bg-gray-50 transition-colors focus:outline-none focus:ring-2 focus:ring-inset focus:ring-primary-500"
        >
          <link.icon className="w-4 h-4 text-gray-400 shrink-0" />
          <span className="flex-1 text-sm text-gray-800">{link.label}</span>
          <ArrowRight className="w-3.5 h-3.5 text-gray-400 shrink-0 transition-all group-hover:text-primary-600 group-hover:translate-x-0.5" />
        </button>
      ))}
    </div>
  );
}

interface CloudHomeProps {
  provider: CloudProviderType;
  namespaces: Namespace[];
  navPrefix: string;
  /** Other connected providers, for the inline switcher — empty (and the switcher hidden)
   * whenever this installation only has one cloud connected, matching the reference's "don't
   * show a picker with nothing to pick" behaviour. */
  otherProviders: CloudProviderType[];
  onSwitch?: (provider: CloudProviderType) => void;
  /** Builds the URL for one namespace's dedicated Namespace Home (Level 2) — real app and Demo
   * Mode encode the cloud differently (`?cloud=` query param vs. the `/demo/{provider}` route
   * prefix), so the parent HomePage decides the shape, not this component. */
  namespaceHref: (namespaceId: string) => string;
}

/** One namespace's row in Cloud Home's namespace list — real per-namespace counts (the same
 * `useNamespaceStats` query CloudHome's own KPI strip is built from), never a fabricated
 * rollup. Clicking enters that namespace's dedicated Namespace Home (Level 2). */
function NamespaceRow({
  namespace,
  stats,
  statsLoading,
  supportsCounts,
  href,
}: {
  namespace: Namespace;
  stats: { totalActive: number; totalDlq: number } | undefined;
  statsLoading: boolean;
  supportsCounts: boolean;
  href: string;
}) {
  const navigate = useNavigate();
  const connection = !namespace.isActive
    ? { label: 'Inactive', dot: 'bg-gray-300' }
    : namespace.lastConnectionTestSucceeded === false
      ? { label: 'Connection issue', dot: 'bg-amber-500' }
      : { label: 'Connected', dot: 'bg-green-500' };

  return (
    <button
      onClick={() => navigate(href)}
      className="group w-full flex items-center justify-between gap-3 px-4 py-3 text-left hover:bg-gray-50 transition-colors border-b border-gray-100 last:border-b-0 focus:outline-none focus:ring-2 focus:ring-inset focus:ring-primary-500"
    >
      <div className="min-w-0 flex items-center gap-2.5">
        <span className={`w-1.5 h-1.5 rounded-full shrink-0 ${connection.dot}`} title={connection.label} aria-hidden="true" />
        <div className="min-w-0">
          <p className="text-sm font-medium text-gray-900 truncate">{namespace.displayName || namespace.name}</p>
          <p className="text-xs text-gray-500 truncate">{namespace.name}</p>
        </div>
        <EnvironmentBadge env={namespace.environment} />
      </div>
      <div className="flex items-center gap-3 shrink-0">
        {supportsCounts && (
          <span className="text-xs text-gray-500">
            {statsLoading ? '…' : `${(stats?.totalActive ?? 0).toLocaleString()} active`}
          </span>
        )}
        {supportsCounts && (stats?.totalDlq ?? 0) > 0 && (
          <span className="px-2 py-0.5 text-xs font-semibold rounded-full bg-red-100 text-red-700">
            {statsLoading ? '…' : `${stats?.totalDlq.toLocaleString()} DLQ`}
          </span>
        )}
        <ChevronRight className="w-5 h-5 text-gray-400 transition-all group-hover:text-primary-600 group-hover:translate-x-0.5" />
      </div>
    </button>
  );
}

/**
 * One cloud's operational front door (Home redesign, roadmap next-chapter). Never blends
 * Azure/AWS/GCP data together — every figure below is filtered to `provider` before rendering,
 * either server-side (attention queue, outcomes) or client-side against a response that already
 * carries a provider per row (Fleet Overview, namespace stats), so a GCP operator never sees an
 * Azure number under a GCP heading.
 */
function CloudHome({ provider, namespaces, navPrefix, otherProviders, onSwitch, namespaceHref }: CloudHomeProps) {
  const navigate = useNavigate();
  const style = getProviderStyle(provider);
  const providerNamespaces = namespaces.filter((ns) => ns.cloudProvider === provider);
  const namespaceIds = providerNamespaces.map((ns) => ns.id);
  const primaryNamespaceId = providerNamespaces[0]?.id;

  const attentionQueue = useAttentionQueue(provider);
  const { data: capabilitiesMap } = useProviderCapabilities();
  const capabilities = getProviderCapabilities(capabilitiesMap, provider);
  const statsResults = useNamespaceStats(namespaceIds);
  const { data: fleetOverview } = useFleetOverview(24);

  const providerHealth = (fleetOverview?.namespaces ?? []).filter(
    (n) => n.provider.toLowerCase() === provider,
  );
  const recentDlq = [...providerHealth]
    .sort((a, b) => b.newInWindow - a.newInWindow || b.activeCount - a.activeCount)
    .slice(0, 5);

  const totals = statsResults.reduce(
    (acc, r) => ({
      active: acc.active + (r.data?.totalActive ?? 0),
      dlq: acc.dlq + (r.data?.totalDlq ?? 0),
      queues: acc.queues + (r.data?.totalQueues ?? 0),
      topics: acc.topics + (r.data?.totalTopics ?? 0),
    }),
    { active: 0, dlq: 0, queues: 0, topics: 0 },
  );
  const statsLoaded = statsResults.length === 0 || statsResults.every((r) => !r.isLoading);

  const quickActions: QuickActionLink[] = [
    { label: 'Browse DLQ Messages', to: `${navPrefix}/dlq-history${primaryNamespaceId ? `?namespace=${primaryNamespaceId}` : ''}`, icon: Inbox },
    // Live Tail relies on a non-destructive, repeatable peek — omitted entirely (not shown
    // disabled) for a provider where every "peek" is actually a receive that can dead-letter a
    // message by accident (ProviderCapabilities.SupportsRepeatablePeek). Matches NamespaceHome.
    ...(capabilities?.supportsRepeatablePeek
      ? [{ label: 'Live Tail', to: `${navPrefix}/live-tail${primaryNamespaceId ? `?namespace=${primaryNamespaceId}` : ''}`, icon: Radio }]
      : []),
    { label: 'Auto-Replay Rules', to: `${navPrefix}/rules`, icon: Zap },
    { label: 'Approval Queue', to: `${navPrefix}/approval-queue`, icon: CheckCircle },
    { label: 'Fleet Overview', to: `${navPrefix}/fleet`, icon: Layers },
  ];

  const isDemoRoute = navPrefix.startsWith('/demo/');

  return (
    <div className="flex-1 overflow-auto p-6">
      {!isDemoRoute && (
        <div className="flex items-center gap-1.5 text-sm text-gray-500 mb-4">
          <button type="button" onClick={() => navigate('/home')} className="hover:text-gray-800 transition-colors">
            Home
          </button>
          <ChevronRight className="w-3.5 h-3.5 text-gray-300" />
          <span className="text-gray-800 font-medium">{style.label}</span>
        </div>
      )}

      <div className="flex flex-wrap items-start justify-between gap-4 mb-6">
        <div className="flex items-center gap-3">
          <div className="w-11 h-11 rounded-xl border border-gray-200 bg-white flex items-center justify-center overflow-hidden shadow-sm shrink-0">
            <ProviderIcon provider={provider} className="w-full h-full" />
          </div>
          <div>
            <h1 className="text-xl font-semibold text-gray-900">{style.label} Home</h1>
            <p className="text-sm text-gray-500">
              {providerNamespaces.length} namespace{providerNamespaces.length === 1 ? '' : 's'} connected
              {providerNamespaces.length > 0 && (
                <span className="ml-2 inline-flex gap-1 align-middle">
                  {Array.from(new Set(providerNamespaces.map((ns) => ns.environment ?? 'dev'))).map((env) => (
                    <EnvironmentBadge key={env} env={env} />
                  ))}
                </span>
              )}
            </p>
          </div>
        </div>

        {otherProviders.length > 0 && onSwitch && (
          <div className="flex items-center gap-1.5">
            <span className="text-xs text-gray-400 mr-1">Switch cloud:</span>
            {otherProviders.map((p) => {
              const pStyle = getProviderStyle(p);
              return (
                <button
                  key={p}
                  onClick={() => onSwitch(p)}
                  className="flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg border border-gray-200 bg-white hover:bg-gray-50 text-xs font-medium text-gray-700 transition-colors"
                >
                  <ProviderIcon provider={p} className="w-3.5 h-3.5 shrink-0" />
                  {pStyle.label}
                </button>
              );
            })}
          </div>
        )}
      </div>

      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 mb-6">
        <KpiTile
          label="Active messages"
          value={capabilities?.supportsMessageCounts === false ? '—' : statsLoaded ? totals.active.toLocaleString() : '…'}
          hint={capabilities?.supportsMessageCounts === false ? capabilities.notes : undefined}
        />
        <KpiTile
          label="DLQ messages"
          value={capabilities?.supportsMessageCounts === false ? '—' : statsLoaded ? totals.dlq.toLocaleString() : '…'}
          hint={capabilities?.supportsMessageCounts === false ? capabilities.notes : undefined}
        />
        <KpiTile label="Queues monitored" value={statsLoaded ? totals.queues.toLocaleString() : '…'} />
        <KpiTile label="Topics monitored" value={statsLoaded ? totals.topics.toLocaleString() : '…'} />
      </div>

      {providerNamespaces.length > 0 && (
        <div className="mb-6">
          <h2 className="text-sm font-semibold text-gray-700 mb-2">Namespaces</h2>
          <div className="bg-white border border-gray-200 rounded-lg overflow-hidden">
            {providerNamespaces.map((ns, i) => (
              <NamespaceRow
                key={ns.id}
                namespace={ns}
                stats={statsResults[i]?.data}
                statsLoading={statsResults[i]?.isLoading ?? false}
                supportsCounts={capabilities?.supportsMessageCounts ?? true}
                href={namespaceHref(ns.id)}
              />
            ))}
          </div>
        </div>
      )}

      <OutcomesThisWeek provider={provider} />

      <div className="mb-2">
        <div className="flex items-center justify-between mb-3">
          <h2 className="text-sm font-semibold text-gray-700">Needs your attention</h2>
          <button
            onClick={() => attentionQueue.refetch()}
            disabled={attentionQueue.isFetching}
            className="flex items-center gap-1.5 px-2.5 py-1 text-xs text-gray-600 hover:text-gray-900 border border-gray-200 rounded-lg hover:bg-gray-50 disabled:opacity-50"
          >
            <RefreshCw className={`w-3 h-3 ${attentionQueue.isFetching ? 'animate-spin' : ''}`} />
            Refresh
          </button>
        </div>

        {attentionQueue.isLoading && (
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-6">
            {[0, 1, 2].map((i) => (
              <div key={i} className="h-40 bg-gray-100 rounded-lg animate-pulse" />
            ))}
          </div>
        )}

        {attentionQueue.isError && (
          <EmptyState
            icon={AlertTriangle}
            heading="Couldn't load the attention queue"
            subtext="Something went wrong fetching what needs your attention. Try again."
            action={{ label: 'Retry', onClick: () => attentionQueue.refetch(), icon: RefreshCw }}
            fillHeight={false}
          />
        )}

        {!attentionQueue.isLoading && !attentionQueue.isError && attentionQueue.data?.isEmpty && (
          <EmptyState
            icon={CheckCircle2}
            heading={`${style.label} looks healthy`}
            subtext="No failure signatures across this cloud's namespaces need attention right now."
            fillHeight={false}
          />
        )}

        {!attentionQueue.isLoading && !attentionQueue.isError && attentionQueue.data && !attentionQueue.data.isEmpty && (
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-6">
            {attentionQueue.data.items.map((item) => (
              <AttentionCard key={`${item.namespaceId}-${item.signatureHash}`} item={item} navPrefix={navPrefix} />
            ))}
          </div>
        )}
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        <div className="lg:col-span-2 bg-white border border-gray-200 rounded-lg p-4">
          <h2 className="text-sm font-semibold text-gray-700 mb-1">Recent DLQ activity</h2>
          {recentDlq.length === 0 ? (
            <p className="text-sm text-gray-500 py-4">No dead-letter activity recorded for this cloud in the last 24 hours.</p>
          ) : (
            <div>
              {recentDlq.map((health) => (
                <RecentDlqRow key={health.namespaceId} health={health} href={namespaceHref(health.namespaceId)} />
              ))}
            </div>
          )}
        </div>

        <div>
          <h2 className="text-sm font-semibold text-gray-700 mb-1">Quick actions</h2>
          <QuickActions links={quickActions} />
        </div>
      </div>
    </div>
  );
}

const ACTIVITY_OUTCOME_STYLES: Record<AuditLogItem['outcome'], { icon: typeof CheckCircle2; color: string }> = {
  Success: { icon: CheckCircle2, color: 'text-emerald-600 bg-emerald-50' },
  Failure: { icon: AlertTriangle, color: 'text-red-600 bg-red-50' },
  Partial: { icon: Info, color: 'text-amber-600 bg-amber-50' },
};

/** One real Audit Trail event — never a synthesized "N events" summary. */
function ActivityRow({ item }: { item: AuditLogItem }) {
  const { icon: Icon, color } = ACTIVITY_OUTCOME_STYLES[item.outcome] ?? ACTIVITY_OUTCOME_STYLES.Success;
  return (
    <div className="flex items-start gap-2.5 py-2.5">
      <span className={`shrink-0 w-6 h-6 rounded-full flex items-center justify-center ${color}`}>
        <Icon className="w-3.5 h-3.5" />
      </span>
      <div className="min-w-0 flex-1">
        <p className="text-sm text-gray-900 truncate">{item.action}</p>
        {(item.resourceName || item.entityName) && (
          <p className="text-xs text-gray-500 truncate">{item.resourceName ?? item.entityName}</p>
        )}
      </div>
      <span className="text-xs text-gray-400 shrink-0">{formatRelativeTime(new Date(item.timestamp))}</span>
    </div>
  );
}

interface NamespaceHomeProps {
  namespace: Namespace;
  navPrefix: string;
  /** Where the back link and breadcrumb return to — that namespace's Cloud Home (Level 1). */
  cloudHomeHref: string;
  /** Every namespace under this same provider, for the header's namespace switcher — empty (and
   * the switcher hidden) when this is the only one, matching the reference's "don't show a
   * picker with nothing to pick" behaviour. Includes `namespace` itself. */
  siblingNamespaces: Namespace[];
  /** Builds another sibling namespace's Namespace Home URL. */
  namespaceHref: (namespaceId: string) => string;
}

/**
 * One namespace's operational workspace (Home redesign Level 2, roadmap next-chapter). Every
 * figure is scoped to this single namespace via the same `?namespace=` query-param convention
 * Messages/DLQ Intelligence/Live Tail/Audit Trail/Recovery Evidence already use — no other
 * namespace's data is ever fetched here, so there's nothing to leak across a cloud/namespace
 * switch. Reuses the real per-namespace endpoints those pages already call (DLQ signatures,
 * DLQ history, DLQ trend, audit summary, namespace stats) rather than inventing new aggregates.
 */
function NamespaceHome({ namespace, navPrefix, cloudHomeHref, siblingNamespaces, namespaceHref }: NamespaceHomeProps) {
  const navigate = useNavigate();
  const [switcherOpen, setSwitcherOpen] = useState(false);
  const { isDemoMode } = useDemoContext();
  const style = getProviderStyle(namespace.cloudProvider);
  const { data: capabilitiesMap } = useProviderCapabilities();
  const capabilities = getProviderCapabilities(capabilitiesMap, namespace.cloudProvider);
  const supportsCounts = capabilities?.supportsMessageCounts ?? true;

  const [{ data: stats, isLoading: statsLoading }] = useNamespaceStats([namespace.id]);
  const { data: signatures, loading: signaturesLoading, available: signaturesAvailable } = useDlqSignatures(namespace.id);
  const { data: recentDlq } = useDlqHistory({ namespaceId: namespace.id, pageSize: 5 });
  const { data: dlqSummary } = useDlqSummary(namespace.id);
  const { data: recentActivity } = useAuditLogs({ namespaceId: namespace.id, page: 1 }, true);
  const { data: trend } = useDlqTrend(namespace.id, 7);

  const region = namespace.awsRegion || namespace.gcpProjectId || undefined;
  const connection = !namespace.isActive
    ? { label: 'Inactive', dot: 'bg-gray-300' }
    : namespace.lastConnectionTestSucceeded === false
      ? { label: 'Connection issue', dot: 'bg-amber-500' }
      : { label: 'Connected', dot: 'bg-green-500' };

  const topClusters = (signatures?.clusters ?? []).slice(0, 5);
  const dlqItems = recentDlq?.items ?? [];
  const activityItems = recentActivity?.items?.slice(0, 5) ?? [];
  const oldestDlqAgeSeconds = dlqSummary?.oldestMessage
    ? Math.max(0, (Date.now() - new Date(dlqSummary.oldestMessage).getTime()) / 1000)
    : null;
  const otherNamespaces = siblingNamespaces.filter((ns) => ns.id !== namespace.id);

  // Only surfaced when this provider actually falls short of something — Azure, with no gaps
  // in the capabilities below, shows nothing here rather than an empty reassurance panel.
  const limitations = [
    !capabilities?.supportsMessageCounts && 'message counts',
    !capabilities?.supportsManualDeadLetter && 'manual dead-lettering',
    !capabilities?.supportsScheduledMessages && 'scheduled messages',
    !capabilities?.supportsRepeatablePeek && 'repeatable/live polling',
    !capabilities?.supportsPurge && 'single-message purge',
  ].filter(Boolean) as string[];

  const quickActions: QuickActionLink[] = [
    { label: 'Browse Queues', to: `${navPrefix}/messages-overview?tab=active&namespace=${namespace.id}`, icon: Inbox },
    // Live Tail relies on a non-destructive, repeatable peek — omitted entirely (not shown
    // disabled) for a provider where every "peek" is actually a receive that can dead-letter a
    // message by accident (ProviderCapabilities.SupportsRepeatablePeek).
    ...(capabilities?.supportsRepeatablePeek
      ? [{ label: 'Live Tail', to: `${navPrefix}/live-tail?namespace=${namespace.id}`, icon: Radio }]
      : []),
    { label: 'DLQ Intelligence', to: `${navPrefix}/dlq-history?namespace=${namespace.id}`, icon: BarChart3 },
    { label: 'Auto-Replay Rules', to: `${navPrefix}/rules`, icon: Zap },
    { label: 'Approval Queue', to: `${navPrefix}/approval-queue`, icon: CheckCircle },
    { label: 'Recovery Evidence', to: `${navPrefix}/recovery?namespace=${namespace.id}`, icon: ShieldCheck },
  ];

  return (
    <div className="flex-1 overflow-auto p-6">
      <div className="flex items-center gap-1.5 text-sm text-gray-500 mb-4">
        <button type="button" onClick={() => navigate(cloudHomeHref.split('?')[0])} className="hover:text-gray-800 transition-colors">
          Home
        </button>
        <ChevronRight className="w-3.5 h-3.5 text-gray-300" />
        <button type="button" onClick={() => navigate(cloudHomeHref)} className="hover:text-gray-800 transition-colors">
          {style.label}
        </button>
        <ChevronRight className="w-3.5 h-3.5 text-gray-300" />
        <span className="text-gray-800 font-medium truncate">{namespace.displayName || namespace.name}</span>
      </div>

      <div className="flex flex-wrap items-start justify-between gap-4 mb-6">
        <div className="flex items-center gap-3 min-w-0">
          <div className="w-11 h-11 rounded-xl border border-gray-200 bg-white flex items-center justify-center overflow-hidden shadow-sm shrink-0">
            <ProviderIcon provider={namespace.cloudProvider} className="w-full h-full" />
          </div>
          <div className="min-w-0">
            <div className="flex items-center gap-2 flex-wrap relative">
              <h1 className="text-xl font-semibold text-gray-900 truncate" title={namespace.displayName || namespace.name}>
                {namespace.displayName || namespace.name}
              </h1>
              <EnvironmentBadge env={namespace.environment} />
              {otherNamespaces.length > 0 && (
                <>
                  <button
                    type="button"
                    onClick={() => setSwitcherOpen((v) => !v)}
                    className="flex items-center gap-1 px-2 py-0.5 text-xs font-medium text-gray-500 hover:text-gray-800 border border-gray-200 rounded-full hover:bg-gray-50 transition-colors"
                    aria-haspopup="listbox"
                    aria-expanded={switcherOpen}
                  >
                    Switch namespace
                    <ChevronDown className="w-3 h-3" />
                  </button>
                  {switcherOpen && (
                    <div className="absolute top-full left-0 mt-1 z-10 bg-white border border-gray-200 rounded-lg shadow-lg py-1 min-w-[220px]" role="listbox">
                      {siblingNamespaces.map((ns) => (
                        <button
                          type="button"
                          key={ns.id}
                          role="option"
                          aria-selected={ns.id === namespace.id}
                          onClick={() => {
                            setSwitcherOpen(false);
                            if (ns.id !== namespace.id) navigate(namespaceHref(ns.id));
                          }}
                          className={`w-full flex items-center justify-between gap-2 px-3 py-1.5 text-left text-sm hover:bg-gray-50 transition-colors ${
                            ns.id === namespace.id ? 'font-semibold text-primary-700 bg-primary-50' : 'text-gray-700'
                          }`}
                        >
                          <span className="truncate">{ns.displayName || ns.name}</span>
                          <EnvironmentBadge env={ns.environment} />
                        </button>
                      ))}
                    </div>
                  )}
                </>
              )}
            </div>
            <div className="flex items-center gap-3 text-xs text-gray-500 mt-0.5 flex-wrap">
              <span className="truncate">{namespace.name}</span>
              <span className="flex items-center gap-1 shrink-0">
                <span className={`w-1.5 h-1.5 rounded-full ${connection.dot}`} aria-hidden="true" />
                {connection.label}
              </span>
              {region && (
                <span className="flex items-center gap-1 shrink-0">
                  <MapPin className="w-3 h-3" />
                  {region}
                </span>
              )}
            </div>
          </div>
        </div>
        <button
          type="button"
          onClick={() => navigate('/connect')}
          className="text-xs font-medium text-primary-600 hover:text-primary-700 border border-gray-200 rounded-lg px-3 py-1.5 hover:bg-gray-50 transition-colors shrink-0"
        >
          View Connection Details
        </button>
      </div>

      <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-3 mb-6">
        <KpiTile
          label="Active messages"
          value={!supportsCounts ? '—' : statsLoading ? '…' : (stats?.totalActive ?? 0).toLocaleString()}
          hint={!supportsCounts ? capabilities?.notes : undefined}
        />
        <KpiTile
          label="DLQ messages"
          value={!supportsCounts ? '—' : statsLoading ? '…' : (stats?.totalDlq ?? 0).toLocaleString()}
        />
        <KpiTile
          label="Oldest DLQ message"
          value={!supportsCounts ? '—' : oldestDlqAgeSeconds == null ? '—' : formatDuration(oldestDlqAgeSeconds)}
          hint={
            !supportsCounts
              ? capabilities?.notes
              : isDemoMode
                ? 'Not available in Demo Mode'
                : oldestDlqAgeSeconds == null
                  ? 'No dead-letter messages on record'
                  : undefined
          }
        />
        <KpiTile label="Queues" value={statsLoading ? '…' : (stats?.totalQueues ?? 0).toLocaleString()} />
        <KpiTile label="Topics" value={statsLoading ? '…' : (stats?.totalTopics ?? 0).toLocaleString()} />
        <KpiTile label="Subscriptions" value={statsLoading ? '…' : (stats?.totalSubscriptions ?? 0).toLocaleString()} />
      </div>

      <div className="bg-white border border-gray-200 rounded-lg p-4 mb-6">
        <h2 className="text-sm font-semibold text-gray-700 mb-2">DLQ trend — last 7 days</h2>
        {isDemoMode ? (
          <p className="text-sm text-gray-500 py-2">Not available in Demo Mode.</p>
        ) : trend && trend.length >= 2 ? (
          <>
            <div className="flex items-center gap-4 text-xs text-gray-500 mb-2">
              <span className="flex items-center gap-1.5">
                <span className="w-2 h-2 rounded-full bg-red-500" /> New
              </span>
              <span className="flex items-center gap-1.5">
                <span className="w-2 h-2 rounded-full bg-emerald-500" /> Resolved
              </span>
            </div>
            <ResponsiveContainer width="100%" height={100}>
              <LineChart data={trend}>
                <Line type="monotone" dataKey="newCount" stroke="#ef4444" strokeWidth={1.5} dot={false} />
                <Line type="monotone" dataKey="resolvedCount" stroke="#22c55e" strokeWidth={1.5} dot={false} />
              </LineChart>
            </ResponsiveContainer>
          </>
        ) : (
          <p className="text-sm text-gray-500 py-2">No trend data yet.</p>
        )}
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        <div className="lg:col-span-2 space-y-4">
          <div className="bg-white border border-gray-200 rounded-lg p-4">
            <div className="flex items-center justify-between mb-2">
              <h2 className="text-sm font-semibold text-gray-700">Top failure signatures</h2>
              <button
                type="button"
                onClick={() => navigate(`${navPrefix}/dlq-history?namespace=${namespace.id}`)}
                className="text-xs font-medium text-primary-600 hover:text-primary-700"
              >
                View all →
              </button>
            </div>
            {signaturesLoading ? (
              <p className="text-sm text-gray-500 py-2">Loading…</p>
            ) : !signaturesAvailable ? (
              <p className="text-sm text-gray-500 py-2">Signature clustering isn't available for this namespace right now.</p>
            ) : topClusters.length === 0 ? (
              <p className="text-sm text-gray-500 py-2">No recurring failure patterns detected.</p>
            ) : (
              <div className="divide-y divide-gray-100">
                {topClusters.map((cluster) => {
                  const pct = signatures && signatures.batchSize > 0
                    ? Math.round((cluster.occurrenceCount / signatures.batchSize) * 100)
                    : null;
                  return (
                    <button
                      type="button"
                      key={cluster.signatureHash}
                      onClick={() => navigate(`${navPrefix}/incidents/${cluster.signatureHash}?namespace=${namespace.id}`)}
                      className="w-full py-2.5 text-left hover:bg-gray-50 -mx-1 px-1 rounded transition-colors"
                    >
                      <div className="flex items-center justify-between gap-3 mb-1">
                        <p className="text-sm font-medium text-gray-900 truncate">
                          {cluster.dominantEntity} · {cluster.dominantDeadletterReason}
                        </p>
                        <span className="shrink-0 px-2 py-0.5 text-xs font-semibold rounded-full bg-red-100 text-red-700">
                          {cluster.occurrenceCount}
                          {pct != null && <span className="text-red-400 font-normal"> · {pct}%</span>}
                        </span>
                      </div>
                      {pct != null && (
                        <div className="h-1 bg-gray-100 rounded-full overflow-hidden mb-1">
                          <div className="h-full bg-red-400 rounded-full" style={{ width: `${Math.min(100, pct)}%` }} />
                        </div>
                      )}
                      <p className="text-xs text-gray-500 truncate">{cluster.explanation}</p>
                    </button>
                  );
                })}
              </div>
            )}
          </div>

          <div className="bg-white border border-gray-200 rounded-lg p-4">
            <div className="flex items-center justify-between mb-2">
              <h2 className="text-sm font-semibold text-gray-700">Recent DLQ messages</h2>
              <button
                type="button"
                onClick={() => navigate(`${navPrefix}/dlq-history?namespace=${namespace.id}`)}
                className="text-xs font-medium text-primary-600 hover:text-primary-700"
              >
                View all →
              </button>
            </div>
            {dlqItems.length === 0 ? (
              <p className="text-sm text-gray-500 py-2">No dead-letter messages recorded.</p>
            ) : (
              <div className="overflow-x-auto -mx-1">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="text-xs text-gray-500 text-left">
                      <th className="font-medium px-1 py-1.5">Time</th>
                      <th className="font-medium px-1 py-1.5">Queue / Topic</th>
                      <th className="font-medium px-1 py-1.5">Error</th>
                      <th className="font-medium px-1 py-1.5 text-right">Receive Count</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100">
                    {dlqItems.map((item) => (
                      <tr key={item.id} className="hover:bg-gray-50">
                        <td className="px-1 py-2 text-xs text-gray-500 whitespace-nowrap">{formatRelativeTime(new Date(item.detectedAtUtc))}</td>
                        <td className="px-1 py-2 text-gray-900 truncate max-w-[160px]">{item.entityName}</td>
                        <td className="px-1 py-2 text-gray-600 truncate max-w-[180px]">{item.deadLetterReason ?? item.failureCategory}</td>
                        <td className="px-1 py-2 text-right text-gray-700">{item.deliveryCount}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </div>

        <div className="space-y-4">
          <div className="bg-white border border-gray-200 rounded-lg p-4">
            <div className="flex items-center justify-between mb-2">
              <h2 className="text-sm font-semibold text-gray-700">Recent activity</h2>
              <button
                type="button"
                onClick={() => navigate(`${navPrefix}/audit?namespace=${namespace.id}`)}
                className="text-xs font-medium text-primary-600 hover:text-primary-700"
              >
                View all →
              </button>
            </div>
            {isDemoMode ? (
              <p className="text-sm text-gray-500 py-2">Not available in Demo Mode.</p>
            ) : activityItems.length === 0 ? (
              <p className="text-sm text-gray-500 py-2">No activity recorded yet.</p>
            ) : (
              <div className="divide-y divide-gray-100">
                {activityItems.map((item) => (
                  <ActivityRow key={item.id} item={item} />
                ))}
              </div>
            )}
          </div>

          <div>
            <h2 className="text-sm font-semibold text-gray-700 mb-2">Quick actions</h2>
            <QuickActions links={quickActions} />
          </div>

          {limitations.length > 0 && (
            <div className="bg-amber-50 border border-amber-200 rounded-lg p-4">
              <div className="flex items-center gap-2 mb-1">
                <Info className="w-4 h-4 text-amber-600 shrink-0" />
                <h2 className="text-sm font-semibold text-amber-800">Provider limitations</h2>
              </div>
              <p className="text-xs text-amber-700">{capabilities?.notes}</p>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function HomeLoadingSkeleton() {
  return (
    <div className="flex-1 overflow-auto p-6">
      <div className="h-8 w-48 bg-gray-100 rounded animate-pulse mb-6" />
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        {[0, 1, 2].map((i) => (
          <div key={i} className="h-40 bg-gray-100 rounded-lg animate-pulse" />
        ))}
      </div>
    </div>
  );
}

/** Chosen when ≥2 providers are connected and no `?cloud=` is set yet — a cloud picker, never a
 * blended dashboard (section 6: "Do not create a single dashboard that mixes AWS + Azure + GCP
 * operational data together"). Each card's own numbers come from Fleet Overview filtered to that
 * provider, same source CloudHome uses. */
function CloudPicker({
  providers,
  namespaces,
  onSelect,
}: {
  providers: CloudProviderType[];
  namespaces: Namespace[];
  onSelect: (provider: CloudProviderType) => void;
}) {
  const { data: fleetOverview } = useFleetOverview(24);

  return (
    <div className="flex-1 overflow-auto p-6">
      <div className="mb-6">
        <h1 className="text-xl font-semibold text-gray-900">Home</h1>
        <p className="text-sm text-gray-500">Choose a cloud to see what needs your attention.</p>
      </div>
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
        {providers.map((provider) => {
          const style = getProviderStyle(provider);
          const providerNamespaces = namespaces.filter((ns) => ns.cloudProvider === provider);
          const activeDlq = fleetOverview
            ? fleetOverview.namespaces
                .filter((n) => n.provider.toLowerCase() === provider)
                .reduce((sum, n) => sum + n.activeCount, 0)
            : null;
          return (
            <button
              key={provider}
              onClick={() => onSelect(provider)}
              className={`text-left bg-white border-2 rounded-xl p-5 hover:shadow-md transition-shadow focus:outline-none focus:ring-2 focus:ring-primary-500 ${style.headerBorder}`}
            >
              <div className="flex items-center gap-3 mb-3">
                <div className="w-10 h-10 rounded-lg border border-gray-200 bg-white flex items-center justify-center overflow-hidden shadow-sm shrink-0">
                  <ProviderIcon provider={provider} className="w-full h-full" />
                </div>
                <div>
                  <p className="font-semibold text-gray-900">{style.label}</p>
                  <p className="text-xs text-gray-500">
                    {providerNamespaces.length} namespace{providerNamespaces.length === 1 ? '' : 's'}
                  </p>
                </div>
              </div>
              {activeDlq !== null && (
                <p className={`text-sm font-medium ${activeDlq > 0 ? 'text-red-600' : 'text-gray-500'}`}>
                  {activeDlq > 0 ? `${activeDlq} active dead-letter message${activeDlq === 1 ? '' : 's'}` : 'No active dead-letters'}
                </p>
              )}
              <div className="mt-3 flex items-center gap-1 text-sm font-medium text-primary-600">
                Open {style.label} Home <ChevronRight className="w-4 h-4" />
              </div>
            </button>
          );
        })}
      </div>
    </div>
  );
}

/**
 * Home — the operational front door, in three levels (Home redesign, roadmap next-chapter):
 * a cloud picker when more than one provider is connected, each cloud's own Cloud Home
 * (Level 1, never blending providers), and — drilling into one of that cloud's namespaces —
 * a dedicated Namespace Home (Level 2, strictly that namespace's own data). The real (non-demo)
 * app can have namespaces spanning multiple providers at once, so Home reads
 * `?cloud=azure|aws|gcp&namespace={id}` to know which level it's showing; Demo Mode already
 * fixes the provider via its own route prefix (`/demo/{provider}`), so only `?namespace=` matters
 * there, and the cloud picker never appears.
 */
export function HomePage() {
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const { isDemoMode, cloudProvider: demoCloudProvider } = useDemoContext();
  const { data: namespaces, isLoading: namespacesLoading } = useNamespaces();
  const namespaceParam = searchParams.get('namespace');

  // Switching clouds replaces the whole operational context — a namespace id from the cloud
  // being left behind must never survive into the new one (section 12: no stale namespace/DLQ/
  // signature data across a cloud switch).
  const selectCloud = (provider: CloudProviderType) => {
    setThemeProvider(provider);
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      next.set('cloud', provider);
      next.delete('namespace');
      return next;
    });
  };

  if (isDemoMode && demoCloudProvider) {
    const demoNavPrefix = `/demo/${demoCloudProvider}`;
    const demoNamespace = (namespaces ?? []).find((ns) => ns.id === namespaceParam);
    if (demoNamespace) {
      return (
        <NamespaceHome
          namespace={demoNamespace}
          navPrefix={demoNavPrefix}
          cloudHomeHref={`${demoNavPrefix}/home`}
          siblingNamespaces={namespaces ?? []}
          namespaceHref={(id) => `${demoNavPrefix}/home?namespace=${id}`}
        />
      );
    }
    return (
      <CloudHome
        provider={demoCloudProvider}
        namespaces={namespaces ?? []}
        navPrefix={demoNavPrefix}
        otherProviders={[]}
        namespaceHref={(id) => `${demoNavPrefix}/home?namespace=${id}`}
      />
    );
  }

  if (namespacesLoading) {
    return <HomeLoadingSkeleton />;
  }

  if (!namespaces || namespaces.length === 0) {
    return (
      <div className="flex-1 overflow-auto p-6">
        <EmptyState
          icon={Plug}
          heading="Connect a cloud to get started"
          subtext="ServiceHub needs at least one Azure, AWS, or GCP connection before it can show you anything."
          action={{ label: 'Add Connection', onClick: () => navigate('/connect'), icon: Plug }}
        />
      </div>
    );
  }

  const connectedProviders = Array.from(
    new Set(namespaces.map((ns) => ns.cloudProvider).filter((p): p is CloudProviderType => !!p)),
  );

  const cloudParam = searchParams.get('cloud') as CloudProviderType | null;
  const activeCloud: CloudProviderType | undefined =
    cloudParam && connectedProviders.includes(cloudParam)
      ? cloudParam
      : connectedProviders.length === 1
        ? connectedProviders[0]
        : undefined;

  if (!activeCloud) {
    return <CloudPicker providers={connectedProviders} namespaces={namespaces} onSelect={selectCloud} />;
  }

  // A namespace id is only honoured when it actually belongs to the active cloud — a stale or
  // tampered `?namespace=` from another provider silently falls back to that cloud's own Home
  // instead of ever rendering another cloud's namespace under this heading.
  const activeNamespace = namespaceParam
    ? namespaces.find((ns) => ns.id === namespaceParam && ns.cloudProvider === activeCloud)
    : undefined;

  if (activeNamespace) {
    return (
      <NamespaceHome
        namespace={activeNamespace}
        navPrefix=""
        cloudHomeHref={`/home?cloud=${activeCloud}`}
        siblingNamespaces={namespaces.filter((ns) => ns.cloudProvider === activeCloud)}
        namespaceHref={(id) => `/home?cloud=${activeCloud}&namespace=${id}`}
      />
    );
  }

  return (
    <CloudHome
      provider={activeCloud}
      namespaces={namespaces}
      navPrefix=""
      otherProviders={connectedProviders.filter((p) => p !== activeCloud)}
      onSwitch={selectCloud}
      namespaceHref={(id) => `/home?cloud=${activeCloud}&namespace=${id}`}
    />
  );
}

export default HomePage;
