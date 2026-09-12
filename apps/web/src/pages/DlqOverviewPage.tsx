import { useMemo, useState, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  AlertTriangle,
  Layers,
  Boxes,
  Radio,
  RefreshCw,
  Search,
  X,
  ChevronDown,
  ChevronUp,
  Download,
  ExternalLink,
  Lightbulb,
  Zap,
  ArrowUpRight,
  ArrowDownRight,
  Mail,
  Archive,
  Eye,
  BarChart3,
  Shield,
  ShieldAlert,
  Globe2,
} from 'lucide-react';
import { LineChart, Line, XAxis, Tooltip, ResponsiveContainer } from 'recharts';
import { useDlqOverview } from '@servicehub/ui-shared/hooks/useDlqOverview';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { ProviderBadge, getProviderStyle } from '@servicehub/ui-shared/lib/providerStyles';
import { EnvironmentBadge } from '@/components/EnvironmentBadge';
import { CategoryBadge } from '@/components/dlq';
import type { CloudProviderType } from '@servicehub/ui-shared/lib/api/types';
import type {
  DlqFailureCategory,
  DlqOverviewEnvironment,
  DlqOverviewNamespace,
  DlqProviderOverview,
  DlqOverviewStatus,
  DlqReplaySafety,
  DlqRecurringPattern,
} from '@servicehub/ui-shared/lib/api/dlqOverview';

const DAYS_OPTIONS = [
  { label: 'Last 24 hours', days: 1 },
  { label: 'Last 7 days', days: 7 },
  { label: 'Last 30 days', days: 30 },
];

// Human-readable labels for the backend's FailureCategory enum (camelCase wire values).
const REASON_LABELS: Record<DlqFailureCategory, string> = {
  unknown: 'Unknown',
  transient: 'Transient',
  maxDelivery: 'Max Delivery',
  expired: 'Expired',
  dataQuality: 'Data Quality',
  authorization: 'Authorization',
  processingError: 'Processing Error',
  resourceNotFound: 'Resource Not Found',
  quotaExceeded: 'Quota Exceeded',
};

const REASON_ORDER: DlqFailureCategory[] = [
  'processingError',
  'maxDelivery',
  'dataQuality',
  'transient',
  'authorization',
  'resourceNotFound',
  'quotaExceeded',
  'expired',
  'unknown',
];

// Matches PROVIDER_STYLES' dot accent hexes (providerStyles.tsx) — Recharts needs a literal
// color, not a Tailwind class, so this stays in sync with that file's brand accents by hand.
const PROVIDER_HEX: Record<CloudProviderType, string> = {
  azure: '#0078D4',
  aws: '#FF9900',
  gcp: '#34A853',
};

const REASON_BAR_HEX = ['#ef4444', '#f59e0b', '#8b5cf6', '#0ea5e9', '#10b981', '#9ca3af'];

// The backend's category query param binds an enum by name (case-insensitively), not the
// camelCase JSON wire value — this recovers the PascalCase form for deep links into DLQ Message
// History's category filter, and for CategoryBadge (which every other DLQ surface feeds PascalCase).
const CATEGORY_WIRE_TO_PASCAL: Record<DlqFailureCategory, string> = {
  unknown: 'Unknown',
  transient: 'Transient',
  maxDelivery: 'MaxDelivery',
  expired: 'Expired',
  dataQuality: 'DataQuality',
  authorization: 'Authorization',
  processingError: 'ProcessingError',
  resourceNotFound: 'ResourceNotFound',
  quotaExceeded: 'QuotaExceeded',
};

const STATUS_OPTIONS: DlqOverviewStatus[] = ['active', 'replayed', 'archived', 'discarded', 'replayFailed', 'resolved'];
const STATUS_LABELS: Record<DlqOverviewStatus, string> = {
  active: 'Active',
  replayed: 'Replayed',
  archived: 'Archived',
  discarded: 'Discarded',
  replayFailed: 'Replay Failed',
  resolved: 'Resolved',
};

const REPLAY_SAFETY_OPTIONS: DlqReplaySafety[] = ['Safe', 'RequiresReview', 'Unsafe'];
const REPLAY_SAFETY_LABELS: Record<DlqReplaySafety, string> = {
  Safe: 'Safe',
  RequiresReview: 'Requires Review',
  Unsafe: 'Unsafe',
};
const REPLAY_SAFETY_STYLES: Record<DlqReplaySafety, string> = {
  Safe: 'bg-green-50 text-green-700',
  RequiresReview: 'bg-amber-50 text-amber-700',
  Unsafe: 'bg-red-50 text-red-700',
};

const CONFIDENCE_STYLES: Record<string, string> = {
  High: 'bg-emerald-50 text-emerald-700 border-emerald-200',
  Medium: 'bg-amber-50 text-amber-700 border-amber-200',
  Low: 'bg-gray-100 text-gray-500 border-gray-200',
};

const INITIAL_PATTERN_ROWS = 5;

function formatAge(iso: string | null): string {
  if (!iso) return '—';
  const ms = Date.now() - new Date(iso).getTime();
  if (ms < 0) return 'just now';
  const totalMinutes = Math.floor(ms / 60000);
  const days = Math.floor(totalMinutes / 1440);
  const hours = Math.floor((totalMinutes % 1440) / 60);
  if (days > 0) return `${days}d ${hours}h`;
  const minutes = totalMinutes % 60;
  if (hours > 0) return `${hours}h ${minutes}m`;
  return totalMinutes <= 0 ? 'just now' : `${totalMinutes}m`;
}

function csvCell(value: string | number): string {
  const s = String(value);
  return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
}

function downloadCsv(filename: string, rows: (string | number)[][]) {
  const content = rows.map((row) => row.map(csvCell).join(',')).join('\n');
  const blob = new Blob([content], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

// `goodDirection` says which direction of change is favorable — 'down' for backlog-shaped
// metrics (fewer active dead letters is good, the default) and 'up' for activity-shaped ones
// (more replays completed is good). The arrow always shows the real direction; only its color
// flips.
function ChangeBadge({ percent, goodDirection = 'down' }: { percent: number | null; goodDirection?: 'up' | 'down' }) {
  if (percent === null) {
    return <span className="text-[11px] font-semibold text-gray-400">New</span>;
  }
  if (percent === 0) {
    return <span className="text-[11px] font-semibold text-gray-400">flat</span>;
  }
  const up = percent > 0;
  const isGood = up ? goodDirection === 'up' : goodDirection === 'down';
  return (
    <span className={`inline-flex items-center gap-0.5 text-[11px] font-semibold ${isGood ? 'text-emerald-600' : 'text-red-600'}`}>
      {up ? <ArrowUpRight className="w-3 h-3" /> : <ArrowDownRight className="w-3 h-3" />}
      {Math.abs(percent).toFixed(percent % 1 === 0 ? 0 : 1)}%
    </span>
  );
}

function StatTile({
  icon,
  label,
  value,
  tone,
  sub,
}: {
  icon: ReactNode;
  label: string;
  value: number | string;
  tone: string;
  sub?: ReactNode;
}) {
  return (
    <div className="bg-white rounded-xl border border-gray-200 shadow-sm p-4 flex items-center gap-3">
      <div className={`w-10 h-10 rounded-lg flex items-center justify-center shrink-0 ${tone}`}>{icon}</div>
      <div className="min-w-0">
        <div className="flex items-baseline gap-1.5">
          <span className="text-2xl font-semibold text-gray-900 leading-none">{value}</span>
          {sub}
        </div>
        <div className="text-xs text-gray-500 mt-1 truncate">{label}</div>
      </div>
    </div>
  );
}

/** Compact "what am I looking at" line — the provider/namespace/message counts the top-level
 * KPI grid used to carry as its own tile before this page grew a Recurring Signatures section
 * that needed the room more. */
function ScopeStrip({
  providerCount,
  namespaceCount,
  totalDeadLettered,
}: {
  providerCount: number;
  namespaceCount: number;
  totalDeadLettered: number;
}) {
  return (
    <div className="flex items-center gap-2 text-sm text-gray-600 bg-white border border-gray-200 rounded-xl px-4 py-2.5 shadow-sm w-fit">
      <Globe2 className="w-4 h-4 text-primary-500 shrink-0" />
      <span className="font-medium text-gray-900">All connected namespaces</span>
      <span className="text-gray-300">·</span>
      <span>{providerCount} provider{providerCount === 1 ? '' : 's'}</span>
      <span className="text-gray-300">·</span>
      <span>{namespaceCount} namespace{namespaceCount === 1 ? '' : 's'}</span>
      <span className="text-gray-300">·</span>
      <span>{totalDeadLettered.toLocaleString()} DLQ messages</span>
    </div>
  );
}

/** All-providers trend + top-reasons rollup, summed client-side from each provider's own
 * (already-fetched) daily trend and reason breakdown — no separate backend query, since the
 * per-provider numbers already partition the fleet total. */
function AllProvidersRollup({ providers }: { providers: DlqProviderOverview[] }) {
  const trend = useMemo(() => {
    const byDate = new Map<string, number>();
    for (const p of providers) {
      for (const point of p.dailyTrend) {
        byDate.set(point.date, (byDate.get(point.date) ?? 0) + point.count);
      }
    }
    return [...byDate.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([date, count]) => ({ date, count }));
  }, [providers]);

  const reasons = useMemo(() => {
    const byCategory = new Map<DlqFailureCategory, number>();
    let total = 0;
    for (const p of providers) {
      for (const r of p.topReasons) {
        byCategory.set(r.category, (byCategory.get(r.category) ?? 0) + r.count);
        total += r.count;
      }
    }
    const sorted = [...byCategory.entries()].sort((a, b) => b[1] - a[1]);
    const top = sorted.slice(0, 5);
    const othersCount = sorted.slice(5).reduce((sum, [, count]) => sum + count, 0);
    const rows = top.map(([category, count]) => ({
      label: REASON_LABELS[category],
      count,
      percent: total > 0 ? Math.round((count / total) * 1000) / 10 : 0,
    }));
    if (othersCount > 0) {
      rows.push({ label: 'Others', count: othersCount, percent: total > 0 ? Math.round((othersCount / total) * 1000) / 10 : 0 });
    }
    return rows;
  }, [providers]);

  const maxReasonCount = reasons[0]?.count ?? 0;

  return (
    <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
      <div className="bg-white rounded-xl border border-gray-200 shadow-sm p-4">
        <h3 className="text-sm font-semibold text-gray-700 mb-3 flex items-center gap-1.5">
          <BarChart3 className="w-4 h-4 text-gray-400" />
          DLQ Trend (All Providers)
        </h3>
        {trend.length > 0 ? (
          <div style={{ width: '100%', height: 160 }}>
            <ResponsiveContainer>
              <LineChart data={trend} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
                <XAxis
                  dataKey="date"
                  tickFormatter={(d: string) => new Date(d).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}
                  tick={{ fontSize: 10, fill: '#9ca3af' }}
                />
                <Tooltip
                  labelFormatter={(d) => new Date(d as string).toLocaleDateString()}
                  formatter={(value) => [value as number, 'Active backlog']}
                />
                <Line type="monotone" dataKey="count" stroke="#dc2626" strokeWidth={2} dot={false} />
              </LineChart>
            </ResponsiveContainer>
          </div>
        ) : (
          <p className="text-xs text-gray-400 italic py-10 text-center">No trend data for this window</p>
        )}
      </div>

      <div className="bg-white rounded-xl border border-gray-200 shadow-sm p-4">
        <h3 className="text-sm font-semibold text-gray-700 mb-3 flex items-center gap-1.5">
          <AlertTriangle className="w-4 h-4 text-gray-400" />
          Top Failure Reasons (All Providers)
        </h3>
        {reasons.length > 0 ? (
          <div className="space-y-2.5">
            {reasons.map((reason, i) => (
              <div key={reason.label} className="flex items-center gap-2 text-xs">
                <span className="w-28 shrink-0 text-gray-600 truncate">{reason.label}</span>
                <div className="flex-1 h-2.5 bg-gray-100 rounded-full overflow-hidden">
                  <div
                    className="h-full rounded-full"
                    style={{
                      width: maxReasonCount > 0 ? `${(reason.count / maxReasonCount) * 100}%` : '0%',
                      backgroundColor: REASON_BAR_HEX[i % REASON_BAR_HEX.length],
                    }}
                  />
                </div>
                <span className="w-12 shrink-0 text-right font-semibold text-gray-700">{reason.count.toLocaleString()}</span>
                <span className="w-10 shrink-0 text-right text-gray-400">{reason.percent}%</span>
              </div>
            ))}
          </div>
        ) : (
          <p className="text-xs text-gray-400 italic py-10 text-center">No dead-lettered messages</p>
        )}
      </div>
    </div>
  );
}

function ProviderSection({
  section,
  search,
  onNavigateNamespace,
}: {
  section: DlqProviderOverview;
  search: string;
  onNavigateNamespace: (namespace: DlqOverviewNamespace) => void;
}) {
  const [collapsed, setCollapsed] = useState(false);
  const style = getProviderStyle(section.provider);
  const isOpen = search ? true : !collapsed;

  const searchLower = search.trim().toLowerCase();
  const visibleNamespaces = searchLower
    ? section.namespaces.filter((n) => n.namespaceName.toLowerCase().includes(searchLower))
    : section.namespaces;

  const topReasons = [...section.topReasons].sort((a, b) => b.count - a.count);
  const maxReasonCount = topReasons[0]?.count ?? 0;

  return (
    <section className={`bg-white rounded-xl border ${style.headerBorder} shadow-sm overflow-hidden`}>
      <button
        onClick={() => setCollapsed((v) => !v)}
        aria-expanded={isOpen}
        className={`w-full flex flex-wrap items-center gap-3 px-5 py-3 ${style.headerBg} border-b ${style.headerBorder} text-left`}
      >
        <ChevronDown className={`w-4 h-4 text-gray-400 transition-transform ${isOpen ? '' : '-rotate-90'}`} />
        <ProviderBadge provider={section.provider} />
        <span className="px-1.5 py-0.5 rounded text-[10px] font-semibold bg-white/70 text-gray-500 border border-gray-200">
          {section.namespacesWithDlq} namespace{section.namespacesWithDlq === 1 ? '' : 's'} with DLQ
        </span>
        <div className="ml-auto flex items-center gap-4">
          <div className="text-right">
            <div className={`text-lg font-bold ${section.totalDeadLettered > 0 ? 'text-red-700' : 'text-gray-400'}`}>
              {section.totalDeadLettered.toLocaleString()}
            </div>
            <div className="text-[10px] text-gray-500 -mt-0.5">Dead-Lettered</div>
          </div>
          <span
            onClick={(e) => {
              e.stopPropagation();
            }}
            className="hidden sm:block"
          >
            <ChangeBadge percent={section.changePercent} />
          </span>
        </div>
      </button>

      {isOpen && (
        <div className="p-4 space-y-4">
          <div className="flex flex-wrap items-center gap-4 text-xs text-gray-500">
            <span className="flex items-center gap-1.5">
              <Boxes className="w-3.5 h-3.5" /> {section.affectedQueues} queue{section.affectedQueues === 1 ? '' : 's'}
            </span>
            <span className="flex items-center gap-1.5">
              <Radio className="w-3.5 h-3.5" /> {section.affectedTopics} topic{section.affectedTopics === 1 ? '' : 's'}
            </span>
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
            <div className="border border-gray-100 rounded-lg p-3">
              <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2">
                DLQ Trend ({style.label})
              </h3>
              {section.dailyTrend.length > 0 ? (
                <div style={{ width: '100%', height: 140 }}>
                  <ResponsiveContainer>
                    <LineChart data={section.dailyTrend} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
                      <XAxis
                        dataKey="date"
                        tickFormatter={(d: string) => new Date(d).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}
                        tick={{ fontSize: 10, fill: '#9ca3af' }}
                      />
                      <Tooltip
                        labelFormatter={(d) => new Date(d as string).toLocaleDateString()}
                        formatter={(value) => [value as number, 'Active backlog']}
                      />
                      <Line type="monotone" dataKey="count" stroke={PROVIDER_HEX[section.provider]} strokeWidth={2} dot={false} />
                    </LineChart>
                  </ResponsiveContainer>
                </div>
              ) : (
                <p className="text-xs text-gray-400 italic py-8 text-center">No trend data for this window</p>
              )}
            </div>

            <div className="border border-gray-100 rounded-lg p-3">
              <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2">Top DLQ Reasons</h3>
              {topReasons.length > 0 ? (
                <div className="space-y-2">
                  {topReasons.map((reason, i) => (
                    <div key={reason.category} className="flex items-center gap-2 text-xs">
                      <span className="w-28 shrink-0 text-gray-600 truncate">{REASON_LABELS[reason.category]}</span>
                      <div className="flex-1 h-2.5 bg-gray-100 rounded-full overflow-hidden">
                        <div
                          className="h-full rounded-full"
                          style={{
                            width: maxReasonCount > 0 ? `${(reason.count / maxReasonCount) * 100}%` : '0%',
                            backgroundColor: REASON_BAR_HEX[i % REASON_BAR_HEX.length],
                          }}
                        />
                      </div>
                      <span className="w-10 shrink-0 text-right font-semibold text-gray-700">{reason.count}</span>
                      <span className="w-10 shrink-0 text-right text-gray-400">{reason.percent}%</span>
                    </div>
                  ))}
                </div>
              ) : (
                <p className="text-xs text-gray-400 italic py-8 text-center">No dead-lettered messages</p>
              )}
            </div>
          </div>

          <div>
            <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2">
              Namespaces ({style.label})
            </h3>
            {visibleNamespaces.length === 0 ? (
              <p className="text-sm text-gray-400 italic py-4 text-center border border-gray-100 rounded-lg">
                {search ? 'No namespaces match your search' : 'No namespaces with dead-lettered messages'}
              </p>
            ) : (
              <div className="border border-gray-100 rounded-lg overflow-hidden">
                <table className="w-full text-sm">
                  <thead className="bg-gray-50 text-[11px] text-gray-500">
                    <tr>
                      <th className="px-3 py-1.5 text-left font-medium">Namespace</th>
                      <th className="px-3 py-1.5 text-left font-medium">Environment</th>
                      <th className="px-3 py-1.5 text-left font-medium">Queues (DLQ)</th>
                      <th className="px-3 py-1.5 text-left font-medium">Topics (DLQ)</th>
                      <th className="px-3 py-1.5 text-left font-medium">DLQ Count</th>
                      <th className="px-3 py-1.5 text-left font-medium">Oldest</th>
                      <th className="px-3 py-1.5"></th>
                    </tr>
                  </thead>
                  <tbody>
                    {visibleNamespaces.map((ns) => (
                      <tr key={ns.namespaceId} className="border-t border-gray-100 hover:bg-gray-50">
                        <td className="px-3 py-2 font-medium text-gray-900">{ns.namespaceName}</td>
                        <td className="px-3 py-2">
                          <EnvironmentBadge env={ns.environment} />
                        </td>
                        <td className="px-3 py-2 text-gray-600">{ns.queuesWithDlq}</td>
                        <td className="px-3 py-2 text-gray-600">{ns.topicsWithDlq}</td>
                        <td className="px-3 py-2 font-semibold text-red-700">{ns.dlqCount.toLocaleString()}</td>
                        <td className="px-3 py-2 text-gray-500">{formatAge(ns.oldestDetectedAt)}</td>
                        <td className="px-3 py-2 text-right">
                          <button
                            onClick={() => onNavigateNamespace(ns)}
                            className="px-2.5 py-1 text-xs font-medium text-indigo-700 bg-indigo-50 hover:bg-indigo-100 border border-indigo-200 rounded-md transition-colors"
                          >
                            View
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </div>
      )}
    </section>
  );
}

/**
 * Fleet-wide recurring failure patterns — see `DlqRecurringPattern`'s backend doc comment for why
 * this is grouped by (category, root cause) rather than the AI signature-clustering pipeline: an
 * overview page cannot afford one AI round trip per connected namespace on every load.
 * "Investigate" deep-links into DLQ Message History for the pattern's single largest namespace,
 * pre-filtered to its category — a real, working destination, not a fabricated signature page.
 */
function RecurringPatternsPanel({
  patterns,
  navPrefix,
}: {
  patterns: DlqRecurringPattern[];
  navPrefix: string;
}) {
  const navigate = useNavigate();
  const [expanded, setExpanded] = useState(false);

  if (patterns.length === 0) {
    return (
      <div className="bg-white rounded-xl border border-gray-200 shadow-sm p-8 text-center text-sm text-gray-400">
        No recurring failure patterns in the current backlog.
      </div>
    );
  }

  const visible = expanded ? patterns : patterns.slice(0, INITIAL_PATTERN_ROWS);

  const investigate = (p: DlqRecurringPattern) =>
    navigate(
      `${navPrefix}/dlq-history?namespace=${p.representativeNamespaceId}&category=${CATEGORY_WIRE_TO_PASCAL[p.category]}`,
    );

  return (
    <section className="bg-white rounded-xl border border-gray-200 shadow-sm overflow-hidden">
      <div className="flex items-center justify-between gap-3 px-5 py-3 border-b border-gray-200">
        <div>
          <h2 className="text-sm font-semibold text-gray-900">Recurring Failure Signatures (All Providers)</h2>
          <p className="text-xs text-gray-500 mt-0.5">Patterns detected across every connected provider and namespace</p>
        </div>
      </div>

      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="bg-gray-50 text-[11px] text-gray-500">
            <tr>
              <th className="px-3 py-2 text-left font-medium">#</th>
              <th className="px-3 py-2 text-left font-medium">Signature / Pattern</th>
              <th className="px-3 py-2 text-left font-medium">Providers</th>
              <th className="px-3 py-2 text-left font-medium">Namespaces</th>
              <th className="px-3 py-2 text-right font-medium">Occurrences</th>
              <th className="px-3 py-2 text-right font-medium">% of Total</th>
              <th className="px-3 py-2 text-left font-medium">First Seen</th>
              <th className="px-3 py-2 text-left font-medium">Last Seen</th>
              <th className="px-3 py-2 text-left font-medium">Confidence</th>
              <th className="px-3 py-2 text-left font-medium">Category</th>
              <th className="px-3 py-2 text-left font-medium">Replay Safety</th>
              <th className="px-3 py-2 text-left font-medium">Suggested Action</th>
              <th className="px-3 py-2"></th>
            </tr>
          </thead>
          <tbody>
            {visible.map((p, i) => (
              <tr key={p.patternKey} className="border-t border-gray-100 hover:bg-gray-50 align-top">
                <td className="px-3 py-2.5 text-gray-400">{i + 1}</td>
                <td className="px-3 py-2.5 font-medium text-gray-900 max-w-xs">{p.rootCauseSummary}</td>
                <td className="px-3 py-2.5">
                  <div className="flex items-center gap-1">
                    {p.affectedProviders.map((prov) => (
                      <ProviderBadge key={prov} provider={prov} />
                    ))}
                  </div>
                </td>
                <td className="px-3 py-2.5 text-gray-600">{p.namespaceCount}</td>
                <td className="px-3 py-2.5 text-right font-semibold text-gray-900">{p.occurrences.toLocaleString()}</td>
                <td className="px-3 py-2.5 text-right text-gray-500">{p.percentOfTotal}%</td>
                <td className="px-3 py-2.5 text-gray-500 whitespace-nowrap">{new Date(p.firstSeenAt).toLocaleDateString()}</td>
                <td className="px-3 py-2.5 text-gray-500 whitespace-nowrap">{new Date(p.lastSeenAt).toLocaleDateString()}</td>
                <td className="px-3 py-2.5">
                  <span
                    className={`inline-block px-2 py-0.5 rounded-full text-xs font-medium border ${CONFIDENCE_STYLES[p.confidence]}`}
                    title={`Based on ${p.occurrences} occurrence(s) across ${p.namespaceCount} namespace(s) — not an AI-generated score`}
                  >
                    {p.confidence} ({Math.round(p.averageConfidence * 100)}%)
                  </span>
                </td>
                <td className="px-3 py-2.5">
                  <CategoryBadge category={CATEGORY_WIRE_TO_PASCAL[p.category]} />
                </td>
                <td className="px-3 py-2.5">
                  <span className={`inline-flex items-center gap-1 text-xs font-medium px-2 py-0.5 rounded-full ${REPLAY_SAFETY_STYLES[p.replaySafety]}`}>
                    {p.replaySafety === 'Unsafe' ? <ShieldAlert className="w-3 h-3" /> : <Shield className="w-3 h-3" />}
                    {REPLAY_SAFETY_LABELS[p.replaySafety]}
                  </span>
                </td>
                <td className="px-3 py-2.5 text-gray-600 max-w-[200px]">{p.suggestedAction}</td>
                <td className="px-3 py-2.5">
                  <div className="flex items-center gap-1.5 justify-end">
                    <button
                      onClick={() => investigate(p)}
                      className="flex items-center gap-1 px-2.5 py-1 text-xs font-medium text-primary-700 bg-primary-50 hover:bg-primary-100 border border-primary-200 rounded-md transition-colors whitespace-nowrap"
                    >
                      <Eye className="w-3 h-3" />
                      Investigate
                    </button>
                    <button
                      onClick={() => navigate(`${navPrefix}/rules`)}
                      className="flex items-center gap-1 px-2.5 py-1 text-xs font-medium text-amber-700 bg-amber-50 hover:bg-amber-100 border border-amber-200 rounded-md transition-colors whitespace-nowrap"
                      title="Open Auto-Replay Rules to build a rule for this pattern"
                    >
                      <Zap className="w-3 h-3" />
                      Create Rule
                    </button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {patterns.length > INITIAL_PATTERN_ROWS && (
        <div className="border-t border-gray-100 px-5 py-2.5">
          <button
            onClick={() => setExpanded((v) => !v)}
            className="flex items-center gap-1 text-xs font-medium text-primary-700 hover:text-primary-800"
          >
            {expanded ? (
              <>
                <ChevronUp className="w-3.5 h-3.5" />
                Show fewer
              </>
            ) : (
              <>
                <ChevronDown className="w-3.5 h-3.5" />
                Show all {patterns.length} signatures
              </>
            )}
          </button>
        </div>
      )}
    </section>
  );
}

export default function DlqOverviewPage() {
  const navigate = useNavigate();
  const { isDemoMode, cloudProvider } = useDemoContext();
  const navPrefix = isDemoMode && cloudProvider ? `/demo/${cloudProvider}` : '';
  const [days, setDays] = useState(7);
  const [cloud, setCloud] = useState<CloudProviderType | 'all'>('all');
  const [environment, setEnvironment] = useState<DlqOverviewEnvironment | 'all'>('all');
  const [namespaceId, setNamespaceId] = useState('');
  const [reason, setReason] = useState<DlqFailureCategory | 'all'>('all');
  const [entityName, setEntityName] = useState('');
  const [status, setStatus] = useState<DlqOverviewStatus | 'all'>('all');
  const [replaySafety, setReplaySafety] = useState<DlqReplaySafety | 'all'>('all');
  const [search, setSearch] = useState('');

  const { data: namespaces } = useNamespaces();

  const { data, isLoading, isError, refetch, isFetching } = useDlqOverview({
    days,
    cloud: cloud === 'all' ? undefined : cloud,
    environment: environment === 'all' ? undefined : environment,
    namespaceId: namespaceId || undefined,
    reason: reason === 'all' ? undefined : reason,
    entityName: entityName || undefined,
    status: status === 'all' ? undefined : status,
    replaySafety: replaySafety === 'all' ? undefined : replaySafety,
  });

  const filtersActive =
    cloud !== 'all' ||
    environment !== 'all' ||
    namespaceId !== '' ||
    reason !== 'all' ||
    entityName !== '' ||
    status !== 'all' ||
    replaySafety !== 'all' ||
    search !== '';

  const clearFilters = () => {
    setCloud('all');
    setEnvironment('all');
    setNamespaceId('');
    setReason('all');
    setEntityName('');
    setStatus('all');
    setReplaySafety('all');
    setSearch('');
  };

  const namespaceOptions = useMemo(
    () => (namespaces ?? []).filter((ns) => cloud === 'all' || ns.cloudProvider === cloud),
    [namespaces, cloud],
  );

  const goToNamespace = (ns: DlqOverviewNamespace) => navigate(`${navPrefix}/dlq-history?namespace=${ns.namespaceId}`);

  const exportCsv = () => {
    const header = ['Provider', 'Namespace', 'Environment', 'Queues (DLQ)', 'Topics (DLQ)', 'DLQ Count', 'Oldest Detected'];
    const rows = (data?.providers ?? []).flatMap((p) =>
      p.namespaces
        .filter((n) => !search || n.namespaceName.toLowerCase().includes(search.toLowerCase()))
        .map((n) => [
          getProviderStyle(p.provider).label,
          n.namespaceName,
          n.environment,
          n.queuesWithDlq,
          n.topicsWithDlq,
          n.dlqCount,
          n.oldestDetectedAt ?? '',
        ]),
    );
    downloadCsv(`dlq-overview-${new Date().toISOString().slice(0, 10)}.csv`, [header, ...rows]);
  };

  return (
    <div className="flex-1 overflow-y-auto min-w-0">
      <div className="p-6 max-w-7xl mx-auto space-y-6">
        {/* Header */}
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-lg bg-red-50 flex items-center justify-center">
              <AlertTriangle className="w-5 h-5 text-red-600" />
            </div>
            <div>
              <h1 className="text-xl font-bold text-gray-900">DLQ Intelligence</h1>
              <p className="text-sm text-gray-500">
                Analyze dead-letter messages across all connected namespaces. Identify recurring patterns and take action.
              </p>
            </div>
          </div>
          <div className="flex items-center gap-2 flex-wrap">
            {data && (
              <span className="text-xs text-gray-400 hidden sm:inline">
                Last updated: {new Date(data.generatedAt).toLocaleString(undefined, {
                  day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit',
                })}
              </span>
            )}
            <select
              value={days}
              onChange={(e) => setDays(Number(e.target.value))}
              aria-label="Time range"
              className="px-3 py-2 rounded-lg text-sm border border-gray-300 text-gray-700 bg-white focus:outline-none focus:ring-2 focus:ring-primary-500"
            >
              {DAYS_OPTIONS.map((opt) => (
                <option key={opt.days} value={opt.days}>
                  {opt.label}
                </option>
              ))}
            </select>
            <button
              onClick={() => refetch()}
              className="p-2 rounded-lg border border-gray-200 bg-white text-gray-600 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-1"
              aria-label="Refresh DLQ overview"
              title="Refresh"
            >
              <RefreshCw className={`w-4 h-4 ${isFetching ? 'animate-spin' : ''}`} />
            </button>
            <button
              onClick={exportCsv}
              disabled={!data}
              className="inline-flex items-center gap-1.5 px-3 py-2 text-sm font-medium bg-white border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              <Download className="w-4 h-4" />
              Export
            </button>
            <button
              onClick={() => refetch()}
              title="Re-scan the current data — refreshes every metric, chart and signature on this page"
              className="inline-flex items-center gap-1.5 px-3 py-2 text-sm font-semibold bg-primary-600 text-white rounded-lg hover:bg-primary-700 transition-colors"
            >
              <Zap className="w-4 h-4" />
              Scan Now
            </button>
          </div>
        </div>

        {isError && (
          <div className="flex items-center justify-between gap-3 bg-red-50 border border-red-200 text-red-700 rounded-lg p-4 text-sm">
            <span>Failed to load the DLQ overview.</span>
            <button
              onClick={() => refetch()}
              className="shrink-0 px-3 py-1.5 text-xs font-medium text-red-700 bg-white border border-red-300 rounded-lg hover:bg-red-100 transition-colors focus:outline-none focus:ring-2 focus:ring-red-400 focus:ring-offset-1"
            >
              Try Again
            </button>
          </div>
        )}

        {isLoading && !data && (
          <div className="flex flex-col items-center justify-center gap-3 py-20 text-gray-400">
            <RefreshCw className="w-6 h-6 animate-spin text-red-400" />
            <p className="text-sm text-gray-500">Loading DLQ overview…</p>
          </div>
        )}

        {data && (
          <>
            <ScopeStrip
              providerCount={data.providers.length}
              namespaceCount={data.totals.namespacesTotal}
              totalDeadLettered={data.totals.totalDeadLettered}
            />

            {/* Key metrics */}
            <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-4">
              <StatTile
                icon={<Mail className="w-5 h-5 text-red-600" />}
                label="Active in DLQ"
                value={data.totals.totalDeadLettered.toLocaleString()}
                tone="bg-red-50"
                sub={<ChangeBadge percent={data.totals.changePercent} />}
              />
              <StatTile
                icon={<RefreshCw className="w-5 h-5 text-emerald-600" />}
                label="Replayed"
                value={data.totals.replayedCount.toLocaleString()}
                tone="bg-emerald-50"
                sub={<ChangeBadge percent={data.totals.replayedChangePercent} goodDirection="up" />}
              />
              <StatTile
                icon={<Archive className="w-5 h-5 text-gray-500" />}
                label="Archived"
                value={data.totals.archivedCount.toLocaleString()}
                tone="bg-gray-100"
              />
              <StatTile
                icon={<BarChart3 className="w-5 h-5 text-indigo-600" />}
                label="Total Observed"
                value={data.totals.totalObserved.toLocaleString()}
                tone="bg-indigo-50"
              />
              <StatTile
                icon={<Layers className="w-5 h-5 text-purple-600" />}
                label="Recurring Signatures"
                value={data.totals.recurringPatternCount.toLocaleString()}
                tone="bg-purple-50"
              />
              <StatTile
                icon={<Eye className="w-5 h-5 text-amber-600" />}
                label="Needs Investigation"
                value={data.totals.needsInvestigationCount.toLocaleString()}
                tone="bg-amber-50"
              />
            </div>

            {/* All-providers trend + top failure reasons */}
            <AllProvidersRollup providers={data.providers} />

            {/* Search + filters */}
            <div className="bg-white border border-gray-200 rounded-xl shadow-sm p-3 flex flex-wrap items-center gap-2">
              <div className="relative flex-1 min-w-[220px]">
                <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
                <input
                  type="text"
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  placeholder="Search namespaces, queues, topics…"
                  aria-label="Search namespaces"
                  className="w-full pl-9 pr-8 py-2 rounded-lg text-sm bg-white border border-gray-300 text-gray-700 placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-red-500 focus:border-red-500"
                />
                {search && (
                  <button
                    onClick={() => setSearch('')}
                    aria-label="Clear search"
                    className="absolute right-2.5 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
                  >
                    <X className="w-4 h-4" />
                  </button>
                )}
              </div>

              <select
                value={cloud}
                onChange={(e) => {
                  const next = e.target.value as CloudProviderType | 'all';
                  setCloud(next);
                  const selectedNs = namespaces?.find((ns) => ns.id === namespaceId);
                  if (selectedNs && next !== 'all' && selectedNs.cloudProvider !== next) {
                    setNamespaceId('');
                  }
                }}
                aria-label="Filter by cloud"
                className="px-3 py-2 rounded-lg text-sm border border-gray-300 text-gray-700 bg-white focus:outline-none focus:ring-2 focus:ring-red-500"
              >
                <option value="all">All Clouds</option>
                <option value="azure">Azure</option>
                <option value="aws">AWS</option>
                <option value="gcp">GCP</option>
              </select>

              <select
                value={environment}
                onChange={(e) => setEnvironment(e.target.value as DlqOverviewEnvironment | 'all')}
                aria-label="Filter by environment"
                className="px-3 py-2 rounded-lg text-sm border border-gray-300 text-gray-700 bg-white focus:outline-none focus:ring-2 focus:ring-red-500"
              >
                <option value="all">All Environments</option>
                <option value="dev">Dev</option>
                <option value="uat">UAT</option>
                <option value="prod">Prod</option>
              </select>

              <select
                value={namespaceId}
                onChange={(e) => setNamespaceId(e.target.value)}
                aria-label="Filter by namespace"
                className="px-3 py-2 rounded-lg text-sm border border-gray-300 text-gray-700 bg-white focus:outline-none focus:ring-2 focus:ring-red-500 max-w-[200px]"
              >
                <option value="">All Namespaces</option>
                {namespaceOptions.map((ns) => (
                  <option key={ns.id} value={ns.id}>
                    {ns.displayName || ns.name}
                  </option>
                ))}
              </select>

              <select
                value={reason}
                onChange={(e) => setReason(e.target.value as DlqFailureCategory | 'all')}
                aria-label="Filter by DLQ reason"
                className="px-3 py-2 rounded-lg text-sm border border-gray-300 text-gray-700 bg-white focus:outline-none focus:ring-2 focus:ring-red-500"
              >
                <option value="all">All DLQ Reasons</option>
                {REASON_ORDER.map((r) => (
                  <option key={r} value={r}>
                    {REASON_LABELS[r]}
                  </option>
                ))}
              </select>

              <input
                type="text"
                value={entityName}
                onChange={(e) => setEntityName(e.target.value)}
                placeholder="Entity name…"
                aria-label="Filter by entity name"
                className="w-32 px-3 py-2 rounded-lg text-sm border border-gray-300 text-gray-700 placeholder-gray-400 bg-white focus:outline-none focus:ring-2 focus:ring-red-500"
              />

              <select
                value={status}
                onChange={(e) => setStatus(e.target.value as DlqOverviewStatus | 'all')}
                aria-label="Filter by status"
                className="px-3 py-2 rounded-lg text-sm border border-gray-300 text-gray-700 bg-white focus:outline-none focus:ring-2 focus:ring-red-500"
              >
                <option value="all">All Statuses</option>
                {STATUS_OPTIONS.map((s) => (
                  <option key={s} value={s}>
                    {STATUS_LABELS[s]}
                  </option>
                ))}
              </select>

              <select
                value={replaySafety}
                onChange={(e) => setReplaySafety(e.target.value as DlqReplaySafety | 'all')}
                aria-label="Filter by replay safety"
                className="px-3 py-2 rounded-lg text-sm border border-gray-300 text-gray-700 bg-white focus:outline-none focus:ring-2 focus:ring-red-500"
              >
                <option value="all">All Replay Safety</option>
                {REPLAY_SAFETY_OPTIONS.map((s) => (
                  <option key={s} value={s}>
                    {REPLAY_SAFETY_LABELS[s]}
                  </option>
                ))}
              </select>

              {filtersActive && (
                <button
                  onClick={clearFilters}
                  className="ml-auto flex items-center gap-1 text-xs font-medium text-gray-500 hover:text-gray-700"
                >
                  <X className="w-3.5 h-3.5" />
                  Clear filters
                </button>
              )}
            </div>

            {/* Provider sections */}
            {data.providers.length === 0 ? (
              <div className="bg-white border border-gray-200 rounded-xl p-8 text-center text-sm text-gray-400">
                No namespaces match the current filters.
              </div>
            ) : (
              <div className="space-y-4">
                {data.providers.map((section) => (
                  <ProviderSection
                    key={section.provider}
                    section={section}
                    search={search}
                    onNavigateNamespace={goToNamespace}
                  />
                ))}
              </div>
            )}

            {/* Recurring failure signatures */}
            <RecurringPatternsPanel patterns={data.recurringPatterns} navPrefix={navPrefix} />

            {/* Guidance banner */}
            <div className="flex flex-wrap items-center gap-4 bg-amber-50 border border-amber-200 rounded-xl p-4">
              <div className="w-10 h-10 rounded-lg bg-amber-100 flex items-center justify-center shrink-0">
                <Lightbulb className="w-5 h-5 text-amber-600" />
              </div>
              <div className="flex-1 min-w-[220px]">
                <h3 className="text-sm font-semibold text-gray-900">Take action to reduce dead-lettered messages</h3>
                <p className="text-xs text-gray-600 mt-0.5">
                  Use the insights above to identify root causes, replay messages, or configure auto-replay rules.
                </p>
              </div>
              <div className="flex items-center gap-2">
                <button
                  onClick={() => navigate(`${navPrefix}/rules`)}
                  className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-amber-700 bg-white hover:bg-amber-100 border border-amber-300 rounded-lg transition-colors"
                >
                  <Zap className="w-3.5 h-3.5" />
                  Go to Auto-Replay Rules
                </button>
                <button
                  onClick={() => navigate(`${navPrefix}/dlq-history`)}
                  className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-white bg-red-600 hover:bg-red-700 rounded-lg transition-colors"
                >
                  Investigate Now
                  <ExternalLink className="w-3.5 h-3.5" />
                </button>
              </div>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
