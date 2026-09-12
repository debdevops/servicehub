import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { useSearchParams, useNavigate } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import {
  Inbox,
  AlertTriangle,
  Globe,
  Plus,
  MessageSquare,
  Radio,
  Search,
  X,
  ChevronDown,
  ChevronRight,
  RefreshCw,
  Layers,
  Hash,
  MoreVertical,
  Copy,
  Clock,
  Download,
  Zap,
} from 'lucide-react';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useQueues, useAllNamespacesQueues } from '@servicehub/ui-shared/hooks/useQueues';
import { useDlqOverview } from '@servicehub/ui-shared/hooks/useDlqOverview';
import { useTopics } from '@servicehub/ui-shared/hooks/useTopics';
import { useSubscriptions } from '@servicehub/ui-shared/hooks/useSubscriptions';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { ProviderBadge, getProviderStyle } from '@servicehub/ui-shared/lib/providerStyles';
import { EmptyState } from '@/components/EmptyState';
import { setThemeProvider } from '@servicehub/ui-shared/lib/providerTheme';
import { useProviderCapabilities } from '@servicehub/ui-shared/hooks/useCloudBridge';
import { getProviderCapabilities } from '@servicehub/ui-shared/lib/api/cloudBridge';
import type { Namespace, CloudProviderType } from '@servicehub/ui-shared/lib/api/types';

// ============================================================================
// MessagesOverviewPage — multi-cloud entry point for Active / Dead-Letter
// browsing. Shows every registered namespace (Azure, AWS, GCP) with its
// queues and topic subscriptions in a scannable table; clicking one jumps
// straight into that entity's message view with the right provider theme.
// Reached from Quick Access "Active Messages" and "Dead-Letter".
//
// Built to stay usable at scale: sections are collapsible, entities are
// sorted by message count (busiest first), each grid scrolls inside a capped
// height, and cloud/namespace/entity-type filters plus a global search narrow
// every section — so even hundreds of queues fit in a single window.
// ============================================================================

type OverviewTab = 'active' | 'deadletter';
type EntityTypeFilter = 'all' | 'queues' | 'topics';

// The Azure Service Bus SDK's real EntityStatus values — the only provider whose queues/topics
// carry a meaningful status string. AWS/GCP entities report an empty status and simply won't
// match a specific filter here (they always show under "All Status").
const ENTITY_STATUS_OPTIONS = ['Active', 'Disabled', 'SendDisabled', 'ReceiveDisabled'];

// Mirrors HomePage's NamespaceRow connection pill so "Connected" here means the same thing it
// means everywhere else in the app — the namespace's last real connection test, not a guess.
function connectionStatus(namespace: Namespace): { label: string; dot: string } {
  if (!namespace.isActive) return { label: 'Inactive', dot: 'bg-gray-300' };
  if (namespace.lastConnectionTestSucceeded === false) return { label: 'Connection issue', dot: 'bg-amber-500' };
  return { label: 'Connected', dot: 'bg-green-500' };
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

// unsupported = the provider has no message-count API (e.g. GCP Pub/Sub) — the raw value is
// always 0 regardless of real backlog, so a dash is shown instead of a misleading "0".
//
// approximate = AWS SQS's own count API (ApproximateNumberOfMessages) is eventually
// consistent — AWS documents it can take up to a minute to reflect a burst of sends or a
// replay. A "~" prefix and tooltip make that AWS-side delay visible instead of the count
// looking like a ServiceHub bug when it briefly reads 0 right after sending messages.
function CountBadge({
  value,
  tab,
  unsupported,
  approximate,
}: {
  value: number;
  tab: OverviewTab;
  unsupported?: boolean;
  approximate?: boolean;
}) {
  if (unsupported) {
    return (
      <span
        className="px-2 py-0.5 rounded-full text-xs font-bold bg-gray-100 text-gray-400"
        title="This provider has no message-count API — open the subscription to see actual messages"
      >
        —
      </span>
    );
  }
  const approximateTitle =
    'AWS reports this count with a short delay (eventually consistent) — it can take up to a minute to catch up after sending or replaying messages.';
  const display = `${approximate ? '~' : ''}${value.toLocaleString()}`;
  if (tab === 'deadletter') {
    return (
      <span
        className={`px-2 py-0.5 rounded-full text-xs font-bold ${
          value > 0 ? 'bg-red-100 text-red-700' : 'bg-gray-100 text-gray-400'
        }`}
        title={approximate ? approximateTitle : undefined}
      >
        {display}
      </span>
    );
  }
  return (
    <span
      className={`px-2 py-0.5 rounded-full text-xs font-bold ${
        value > 0 ? 'bg-sky-100 text-sky-700' : 'bg-gray-100 text-gray-400'
      }`}
      title={approximate ? approximateTitle : undefined}
    >
      {display}
    </span>
  );
}

function StatTile({
  icon,
  label,
  value,
  tone,
}: {
  icon: ReactNode;
  label: string;
  value: number | string;
  tone: string;
}) {
  return (
    <div className="bg-white rounded-xl border border-gray-200 shadow-sm p-4 flex items-center gap-3">
      <div className={`w-10 h-10 rounded-lg flex items-center justify-center shrink-0 ${tone}`}>{icon}</div>
      <div className="min-w-0">
        <div className="text-2xl font-semibold text-gray-900 leading-none">{value}</div>
        <div className="text-xs text-gray-500 mt-1 truncate">{label}</div>
      </div>
    </div>
  );
}

// ─── Row action menu (kebab) ───────────────────────────────────────────────

interface RowMenuAction {
  label: string;
  icon?: ReactNode;
  onClick: () => void;
}

function RowMenu({ actions, onClose, anchorRect }: { actions: RowMenuAction[]; onClose: () => void; anchorRect: DOMRect }) {
  const top = anchorRect.bottom + 4;
  const right = window.innerWidth - anchorRect.right;

  useEffect(() => {
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', handleEscape);
    return () => window.removeEventListener('keydown', handleEscape);
  }, [onClose]);

  return (
    <>
      <div className="fixed inset-0 z-40" onClick={onClose} />
      <div
        role="menu"
        style={{ position: 'fixed', top, right }}
        className="z-50 w-52 bg-white border border-gray-200 rounded-lg shadow-lg py-1"
      >
        {actions.map((action) => (
          <button
            key={action.label}
            role="menuitem"
            onClick={() => {
              action.onClick();
              onClose();
            }}
            className="w-full flex items-center gap-2 px-3 py-2 text-sm text-left text-gray-700 hover:bg-gray-50"
          >
            {action.icon}
            {action.label}
          </button>
        ))}
      </div>
    </>
  );
}

function QueueRow({
  namespace,
  queue,
  tab,
  isAws,
  onOpen,
}: {
  namespace: Namespace;
  queue: { name: string; activeMessageCount: number; deadLetterMessageCount: number; scheduledMessageCount: number; status: string; deadLetterTargetQueue?: string | null };
  tab: OverviewTab;
  isAws: boolean;
  onOpen: () => void;
}) {
  const navigate = useNavigate();
  const { data: capabilitiesMap } = useProviderCapabilities();
  const supportsCounts = getProviderCapabilities(capabilitiesMap, namespace.cloudProvider)?.supportsMessageCounts ?? true;
  const [menuOpen, setMenuOpen] = useState<DOMRect | null>(null);
  const count = tab === 'deadletter' ? queue.deadLetterMessageCount : queue.activeMessageCount;

  return (
    <tr
      className="border-b border-gray-50 hover:bg-gray-50/80"
      title={tab === 'deadletter' && queue.deadLetterTargetQueue ? `DLQ: ${queue.deadLetterTargetQueue}` : undefined}
    >
      <td className="px-3 py-2">
        <span className="text-sm font-medium text-gray-800 truncate">{queue.name}</span>
      </td>
      <td className="px-3 py-2">
        <CountBadge value={count} tab={tab} unsupported={!supportsCounts} approximate={isAws} />
      </td>
      <td className="px-3 py-2 text-xs text-gray-500">
        {supportsCounts && queue.scheduledMessageCount > 0 ? queue.scheduledMessageCount.toLocaleString() : '—'}
      </td>
      <td className="px-3 py-2 text-xs text-gray-500">{queue.status || '—'}</td>
      <td className="px-3 py-2">
        <div className="flex items-center justify-end gap-1">
          <button
            onClick={onOpen}
            className="px-2.5 py-1 text-xs font-medium text-white bg-sky-600 hover:bg-sky-700 rounded-lg transition-colors"
          >
            View Messages
          </button>
          <button
            onClick={(e) => setMenuOpen((e.currentTarget as HTMLElement).getBoundingClientRect())}
            aria-label={`More actions for ${queue.name}`}
            className="p-1 text-gray-400 hover:text-gray-700 rounded"
          >
            <MoreVertical className="w-4 h-4" />
          </button>
          {menuOpen && (
            <RowMenu
              anchorRect={menuOpen}
              onClose={() => setMenuOpen(null)}
              actions={[
                {
                  label: 'Copy queue name',
                  icon: <Copy className="w-3.5 h-3.5" />,
                  onClick: () => navigator.clipboard?.writeText(queue.name),
                },
                ...(queue.scheduledMessageCount > 0
                  ? [
                      {
                        label: 'View scheduled',
                        icon: <Clock className="w-3.5 h-3.5" />,
                        onClick: () =>
                          navigate(`/scheduled?namespace=${namespace.id}&queue=${encodeURIComponent(queue.name)}`),
                      },
                    ]
                  : []),
              ]}
            />
          )}
        </div>
      </td>
    </tr>
  );
}

function TopicSubscriptions({
  namespace,
  topicName,
  tab,
  navPrefix,
}: {
  namespace: Namespace;
  topicName: string;
  tab: OverviewTab;
  navPrefix: string;
}) {
  const navigate = useNavigate();
  const { data: subscriptions } = useSubscriptions(namespace.id, topicName, false);
  const { data: capabilitiesMap } = useProviderCapabilities();
  const supportsCounts = getProviderCapabilities(capabilitiesMap, namespace.cloudProvider)?.supportsMessageCounts ?? true;

  if (!subscriptions || subscriptions.length === 0) {
    return (
      <p className="text-xs text-gray-400 italic px-3 py-2">
        No subscriptions — publishes to this topic are dropped
      </p>
    );
  }

  return (
    <table className="w-full text-sm">
      <tbody>
        {subscriptions.map((sub) => (
          <tr key={sub.name} className="border-t border-gray-100">
            <td className="pl-6 pr-3 py-1.5 text-sm text-gray-600 truncate">↳ {sub.name}</td>
            <td className="px-3 py-1.5">
              <CountBadge
                value={tab === 'deadletter' ? sub.deadLetterMessageCount : sub.activeMessageCount}
                tab={tab}
                unsupported={!supportsCounts}
              />
            </td>
            <td className="px-3 py-1.5 text-right">
              <button
                onClick={() => {
                  setThemeProvider(namespace.cloudProvider);
                  navigate(
                    `${navPrefix}/messages?namespace=${namespace.id}&topic=${encodeURIComponent(topicName)}&subscription=${encodeURIComponent(sub.name)}&queueType=${tab}`,
                  );
                }}
                className="px-2.5 py-1 text-xs font-medium text-white bg-sky-600 hover:bg-sky-700 rounded-lg transition-colors"
              >
                View Messages
              </button>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function TopicRow({
  namespace,
  topic,
  tab,
  navPrefix,
}: {
  namespace: Namespace;
  topic: { name: string; subscriptionCount: number; status: string };
  tab: OverviewTab;
  navPrefix: string;
}) {
  const navigate = useNavigate();
  const isAws = namespace.cloudProvider === 'aws';
  const [expanded, setExpanded] = useState(false);

  if (isAws) {
    return (
      <tr className="border-b border-gray-50 hover:bg-gray-50/80">
        <td className="px-3 py-2">
          <span className="text-sm font-medium text-gray-800 truncate">📢 {topic.name}</span>
        </td>
        <td className="px-3 py-2 text-xs text-gray-500">{topic.subscriptionCount}</td>
        <td className="px-3 py-2 text-xs text-gray-500">{topic.status || '—'}</td>
        <td className="px-3 py-2 text-right">
          <button
            onClick={() => {
              setThemeProvider(namespace.cloudProvider);
              navigate(`${navPrefix}/messages?namespace=${namespace.id}&topic=${encodeURIComponent(topic.name)}`);
            }}
            className="px-2.5 py-1 text-xs font-medium text-white bg-sky-600 hover:bg-sky-700 rounded-lg transition-colors"
          >
            Fan-out →
          </button>
        </td>
      </tr>
    );
  }

  return (
    <>
      <tr className="border-b border-gray-50 hover:bg-gray-50/80">
        <td className="px-3 py-2">
          <button
            onClick={() => setExpanded((v) => !v)}
            aria-expanded={expanded}
            className="flex items-center gap-1.5 text-sm font-medium text-gray-800 truncate"
          >
            {expanded ? <ChevronDown className="w-3.5 h-3.5 text-gray-400" /> : <ChevronRight className="w-3.5 h-3.5 text-gray-400" />}
            📢 {topic.name}
          </button>
        </td>
        <td className="px-3 py-2 text-xs text-gray-500">{topic.subscriptionCount}</td>
        <td className="px-3 py-2 text-xs text-gray-500">{topic.status || '—'}</td>
        <td className="px-3 py-2 text-right">
          <button
            onClick={() => setExpanded((v) => !v)}
            aria-expanded={expanded}
            className="px-2.5 py-1 text-xs font-medium text-white bg-sky-600 hover:bg-sky-700 rounded-lg transition-colors"
          >
            {expanded ? 'Hide subscriptions' : 'Fan-out →'}
          </button>
        </td>
      </tr>
      {expanded && (
        <tr>
          <td colSpan={4} className="bg-gray-50/50 p-0">
            <TopicSubscriptions namespace={namespace} topicName={topic.name} tab={tab} navPrefix={navPrefix} />
          </td>
        </tr>
      )}
    </>
  );
}

function NamespaceEntitiesSection({
  namespace,
  tab,
  navPrefix,
  filter,
  entityTypeFilter,
  statusFilter,
}: {
  namespace: Namespace;
  tab: OverviewTab;
  navPrefix: string;
  filter: string;
  entityTypeFilter: EntityTypeFilter;
  statusFilter: string;
}) {
  const navigate = useNavigate();
  const style = getProviderStyle(namespace.cloudProvider);
  const { data: queues, isLoading: queuesLoading, isError: queuesError } = useQueues(namespace.id, false);
  const { data: topics } = useTopics(namespace.id, false);
  const isAws = namespace.cloudProvider === 'aws';
  const [collapsed, setCollapsed] = useState(false);

  // A namespace-name match means the whole namespace is what the operator is looking for —
  // show everything inside it rather than requiring every entity name to also match (a
  // search for "orders-prod" used to hide the very namespace it named, because the filter
  // only ever matched against queue/topic names).
  const namespaceLabel = (namespace.displayName || namespace.name).toLowerCase();
  const namespaceMatches = filter !== '' && namespaceLabel.includes(filter);
  const effectiveFilter = namespaceMatches ? '' : filter;

  // While searching, every section stays open so matches are never hidden.
  const isOpen = filter ? true : !collapsed;

  // AWS surfaces each redrive DLQ as its own queue; those are reached via the
  // source queue's DLQ tab, so hide them as standalone widgets.
  const dlqTargets = new Set(
    (queues ?? []).map((q) => q.deadLetterTargetQueue).filter(Boolean) as string[],
  );
  const countOf = (q: { activeMessageCount: number; deadLetterMessageCount: number }) =>
    tab === 'deadletter' ? q.deadLetterMessageCount : q.activeMessageCount;

  const showQueues = entityTypeFilter !== 'topics';
  const showTopics = entityTypeFilter !== 'queues';

  // Busiest entities first, so the ones that matter are visible without scrolling.
  const visibleQueues = showQueues
    ? (queues ?? [])
        .filter((q) => !dlqTargets.has(q.name))
        .filter((q) => !effectiveFilter || q.name.toLowerCase().includes(effectiveFilter))
        .filter((q) => statusFilter === 'all' || q.status === statusFilter)
        .sort((a, b) => countOf(b) - countOf(a) || a.name.localeCompare(b.name))
    : [];

  // SNS topics have no DLQ of their own — hide them on the dead-letter tab.
  const visibleTopics = showTopics
    ? (isAws && tab === 'deadletter' ? [] : (topics ?? []))
        .filter((t) => !effectiveFilter || t.name.toLowerCase().includes(effectiveFilter))
        .filter((t) => statusFilter === 'all' || t.status === statusFilter)
    : [];

  const totalCount = (queues ?? [])
    .filter((q) => !dlqTargets.has(q.name))
    .reduce((sum, q) => sum + countOf(q), 0);
  const entityCount = visibleQueues.length + visibleTopics.length;

  const openQueue = (queueName: string) => {
    setThemeProvider(namespace.cloudProvider);
    navigate(
      `${navPrefix}/messages?namespace=${namespace.id}&queue=${encodeURIComponent(queueName)}&queueType=${tab}`,
    );
  };

  if (filter && entityCount === 0 && !queuesLoading && !queuesError) {
    return null;
  }

  return (
    <section className={`bg-white rounded-xl border ${style.headerBorder} shadow-sm overflow-hidden`}>
      {/* Provider-tinted header — clickable to collapse the section */}
      <button
        onClick={() => setCollapsed((v) => !v)}
        aria-expanded={isOpen}
        className={`w-full flex items-center gap-2 px-5 py-3 ${style.headerBg} border-b ${style.headerBorder} text-left`}
      >
        <ChevronDown
          className={`w-4 h-4 text-gray-400 transition-transform ${isOpen ? '' : '-rotate-90'}`}
        />
        <ProviderBadge provider={namespace.cloudProvider} />
        <h2 className="text-sm font-semibold text-gray-900 truncate">
          {namespace.displayName || namespace.name}
        </h2>
        <span className="px-1.5 py-0.5 rounded text-[10px] font-semibold bg-white/70 text-gray-500 border border-gray-200">
          {entityCount} entit{entityCount === 1 ? 'y' : 'ies'}
        </span>
        <div className="ml-auto flex items-center gap-3">
          <span className={`text-xs font-semibold ${tab === 'deadletter' && totalCount > 0 ? 'text-red-700' : style.accentText}`}>
            {totalCount.toLocaleString()} {tab === 'deadletter' ? 'dead-lettered' : 'active'}
          </span>
          <span className="hidden sm:flex items-center gap-1.5 text-xs text-gray-500">
            <span className={`w-1.5 h-1.5 rounded-full ${connectionStatus(namespace).dot}`} aria-hidden="true" />
            {connectionStatus(namespace).label}
          </span>
        </div>
      </button>

      {isOpen && (
        <div className="p-4 space-y-4">
          {queuesLoading ? (
            <p className="text-sm text-gray-400 flex items-center gap-2">
              <RefreshCw className="w-3.5 h-3.5 animate-spin" />
              Loading entities…
            </p>
          ) : queuesError ? (
            <div className="flex items-center gap-2 text-sm text-red-600">
              <AlertTriangle className="w-4 h-4 shrink-0" />
              Unable to reach this namespace
            </div>
          ) : visibleQueues.length === 0 && visibleTopics.length === 0 ? (
            <p className="text-sm text-gray-400 italic">
              {filter ? 'No entities match your search' : 'No entities in this namespace'}
            </p>
          ) : (
            <>
              {visibleQueues.length > 0 && (
                <div>
                  <h3 className="flex items-center gap-1.5 text-[11px] font-semibold text-gray-500 uppercase tracking-wider mb-2">
                    <MessageSquare className="w-3.5 h-3.5" /> Queues ({visibleQueues.length})
                  </h3>
                  {/* Capped height + inner scroll: many queues never blow up the page */}
                  <div className="max-h-64 overflow-y-auto border border-gray-100 rounded-lg">
                    <table className="w-full text-sm">
                      <thead className="sticky top-0 bg-gray-50 text-[11px] text-gray-500">
                        <tr>
                          <th className="px-3 py-1.5 text-left font-medium">Name</th>
                          <th className="px-3 py-1.5 text-left font-medium">{tab === 'deadletter' ? 'Dead-Letter' : 'Active'}</th>
                          <th className="px-3 py-1.5 text-left font-medium">Scheduled</th>
                          <th className="px-3 py-1.5 text-left font-medium">Status</th>
                          <th className="px-3 py-1.5"></th>
                        </tr>
                      </thead>
                      <tbody>
                        {visibleQueues.map((queue) => (
                          <QueueRow
                            key={queue.name}
                            namespace={namespace}
                            queue={queue}
                            tab={tab}
                            isAws={isAws}
                            onOpen={() => openQueue(queue.name)}
                          />
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}

              {visibleTopics.length > 0 && (
                <div>
                  <h3 className="flex items-center gap-1.5 text-[11px] font-semibold text-gray-500 uppercase tracking-wider mb-2">
                    <Radio className="w-3.5 h-3.5" /> Topics ({visibleTopics.length})
                  </h3>
                  <div className="max-h-64 overflow-y-auto border border-gray-100 rounded-lg">
                    <table className="w-full text-sm">
                      <thead className="sticky top-0 bg-gray-50 text-[11px] text-gray-500">
                        <tr>
                          <th className="px-3 py-1.5 text-left font-medium">Name</th>
                          <th className="px-3 py-1.5 text-left font-medium">Subscribers</th>
                          <th className="px-3 py-1.5 text-left font-medium">Status</th>
                          <th className="px-3 py-1.5"></th>
                        </tr>
                      </thead>
                      <tbody>
                        {visibleTopics.map((topic) => (
                          <TopicRow key={topic.name} namespace={namespace} topic={topic} tab={tab} navPrefix={navPrefix} />
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}
            </>
          )}
        </div>
      )}
    </section>
  );
}

export function MessagesOverviewPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { data: namespaces, isLoading, isFetching, dataUpdatedAt } = useNamespaces();
  const { isDemoMode, cloudProvider } = useDemoContext();
  const navPrefix = isDemoMode && cloudProvider ? `/demo/${cloudProvider}` : '';
  const [search, setSearch] = useState('');
  const filter = search.trim().toLowerCase();
  // Seeded once from ?cloud= so a deep link (e.g. DLQ Overview's per-provider "View All") lands
  // already filtered — mirrors how `tab` is read from the URL just below.
  const [providerFilter, setProviderFilter] = useState<CloudProviderType | 'all'>(
    (searchParams.get('cloud') as CloudProviderType | null) ?? 'all',
  );
  const [namespaceFilter, setNamespaceFilter] = useState<string>('');
  const [entityTypeFilter, setEntityTypeFilter] = useState<EntityTypeFilter>('all');
  const [statusFilter, setStatusFilter] = useState<string>('all');

  const tab: OverviewTab = searchParams.get('tab') === 'deadletter' ? 'deadletter' : 'active';

  const setTab = (next: OverviewTab) => {
    setSearchParams({ tab: next }, { replace: true });
  };

  const isDeadLetter = tab === 'deadletter';

  const visibleNamespaces = useMemo(
    () =>
      (namespaces ?? []).filter((ns) => {
        if (providerFilter !== 'all' && ns.cloudProvider !== providerFilter) return false;
        if (namespaceFilter && ns.id !== namespaceFilter) return false;
        return true;
      }),
    [namespaces, providerFilter, namespaceFilter],
  );

  // Fleet-wide rollup for the metrics row — shares the same cached ['queues', id] /
  // ['namespace-stats', id] queries every NamespaceEntitiesSection below already fetches, so
  // this adds no extra network cost.
  const liveStats = useAllNamespacesQueues((namespaces ?? []).map((ns) => ns.id), false);

  // The Dead-Letter tab's headline KPIs (Total Dead-Letter Messages, Namespaces with Messages)
  // must NOT be derived purely from liveStats: those are per-namespace live provider queries,
  // and any namespace whose live connection is degraded/unreachable silently contributes 0 —
  // collapsing a real, already-known DLQ total to a misleading zero instead of reporting it as
  // unknown. /api/v1/dlq/overview reads the persisted DLQ ledger (the same source DLQ
  // Intelligence, Incident Center and Fleet Overview already use) and stays correct regardless
  // of live connectivity. Queues/Topics counts have no ledger equivalent and stay live-derived.
  const { data: dlqOverview } = useDlqOverview({}, isDeadLetter);

  const aggregate = useMemo(() => {
    let totalQueues = 0;
    let totalTopics = 0;
    let liveTotalMessages = 0;
    let liveNamespacesWithMessages = 0;
    for (const s of liveStats) {
      const relevant = tab === 'deadletter' ? s.totalDlq : s.totalActive;
      liveTotalMessages += relevant;
      totalQueues += s.totalQueues;
      totalTopics += s.totalTopics;
      if (relevant > 0) liveNamespacesWithMessages++;
    }

    const totalMessages =
      isDeadLetter && dlqOverview ? dlqOverview.totals.totalDeadLettered : liveTotalMessages;
    const namespacesWithMessages =
      isDeadLetter && dlqOverview ? dlqOverview.totals.namespacesWithDlq : liveNamespacesWithMessages;

    return { totalMessages, totalQueues, totalTopics, namespacesWithMessages };
  }, [liveStats, tab, isDeadLetter, dlqOverview]);

  const clearFilters = () => {
    setSearch('');
    setProviderFilter('all');
    setNamespaceFilter('');
    setEntityTypeFilter('all');
    setStatusFilter('all');
  };
  const filtersActive =
    search !== '' || providerFilter !== 'all' || namespaceFilter !== '' || entityTypeFilter !== 'all' || statusFilter !== 'all';

  const handleRefresh = () => {
    for (const key of ['queues', 'namespace-stats', 'topics', 'subscriptions']) {
      queryClient.invalidateQueries({ queryKey: [key], refetchType: 'active' });
    }
  };

  const exportCsv = () => {
    const header = ['Provider', 'Namespace', 'Queues', 'Topics', isDeadLetter ? 'Dead-Letter' : 'Active', 'Connection'];
    const rows = visibleNamespaces.map((ns) => {
      const stats = liveStats.find((s) => s.namespaceId === ns.id);
      return [
        getProviderStyle(ns.cloudProvider).label,
        ns.displayName || ns.name,
        stats?.totalQueues ?? 0,
        stats?.totalTopics ?? 0,
        (isDeadLetter ? stats?.totalDlq : stats?.totalActive) ?? 0,
        connectionStatus(ns).label,
      ];
    });
    downloadCsv(`${isDeadLetter ? 'dead-letter' : 'active-messages'}-overview-${new Date().toISOString().slice(0, 10)}.csv`, [
      header,
      ...rows,
    ]);
  };

  return (
    <div className="flex-1 overflow-y-auto min-w-0">
      <div className="p-6 max-w-7xl mx-auto space-y-6">
        {/* Header */}
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <div className={`w-10 h-10 rounded-lg flex items-center justify-center shrink-0 ${isDeadLetter ? 'bg-red-50' : 'bg-sky-50'}`}>
              {isDeadLetter ? (
                <AlertTriangle className="w-5 h-5 text-red-600" />
              ) : (
                <Inbox className="w-5 h-5 text-sky-600" />
              )}
            </div>
            <div>
              <p className="text-xs text-gray-400 mb-0.5">Messages / {isDeadLetter ? 'Dead-Letter' : 'Active Messages'}</p>
              <h1 className="text-xl font-bold text-gray-900">
                {isDeadLetter ? 'Dead-Letter Overview' : 'Active Messages Overview'}
              </h1>
              <p className="text-sm text-gray-500">
                All connected clouds — pick an entity to open its {isDeadLetter ? 'DLQ' : 'messages'}
              </p>
            </div>
          </div>

          <div className="flex items-center gap-2 flex-wrap">
            {dataUpdatedAt > 0 && (
              <span className="text-xs text-gray-400 hidden md:inline">
                Last updated: {new Date(dataUpdatedAt).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })}
              </span>
            )}
            {/* Tab toggle */}
            <div className="flex rounded-lg overflow-hidden border border-gray-300">
              <button
                onClick={() => setTab('active')}
                className={`px-4 py-1.5 text-sm font-medium transition-colors ${
                  !isDeadLetter ? 'bg-sky-600 text-white' : 'bg-white text-gray-600 hover:bg-gray-50'
                }`}
              >
                Active
              </button>
              <button
                onClick={() => setTab('deadletter')}
                className={`px-4 py-1.5 text-sm font-medium transition-colors ${
                  isDeadLetter ? 'bg-red-600 text-white' : 'bg-white text-gray-600 hover:bg-gray-50'
                }`}
              >
                Dead-Letter
              </button>
            </div>
            <button
              onClick={handleRefresh}
              disabled={isFetching}
              className="inline-flex items-center gap-1.5 px-3 py-2 text-sm font-medium bg-white border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-50 transition-colors"
            >
              <RefreshCw className={`w-4 h-4 ${isFetching ? 'animate-spin' : ''}`} />
              Refresh
            </button>
            <button
              onClick={exportCsv}
              disabled={!namespaces || namespaces.length === 0}
              className="inline-flex items-center gap-1.5 px-3 py-2 text-sm font-medium bg-white border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              <Download className="w-4 h-4" />
              Export
            </button>
            <button
              onClick={handleRefresh}
              title="Re-scan the current data — refreshes every queue, topic and namespace count on this page"
              className={`inline-flex items-center gap-1.5 px-3 py-2 text-sm font-semibold text-white rounded-lg transition-colors ${
                isDeadLetter ? 'bg-red-600 hover:bg-red-700' : 'bg-sky-600 hover:bg-sky-700'
              }`}
            >
              <Zap className="w-4 h-4" />
              Scan Now
            </button>
          </div>
        </div>

        {isLoading ? (
          <div className="flex items-center justify-center py-20 text-gray-500 gap-2 text-sm">
            <RefreshCw className="w-4 h-4 animate-spin" />
            Loading namespaces…
          </div>
        ) : !namespaces || namespaces.length === 0 ? (
          <EmptyState
            icon={Globe}
            heading="No namespaces connected"
            subtext="Connect Azure, AWS, or GCP to browse messages."
            action={{ label: 'Connect a namespace', icon: Plus, onClick: () => navigate('/connect') }}
          />
        ) : (
          <>
            {/* Key metrics */}
            <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
              <StatTile
                icon={isDeadLetter ? <AlertTriangle className="w-5 h-5 text-red-600" /> : <Inbox className="w-5 h-5 text-sky-600" />}
                label={isDeadLetter ? 'Total Dead-Letter Messages' : 'Total Active Messages'}
                value={aggregate.totalMessages.toLocaleString()}
                tone={isDeadLetter ? 'bg-red-50' : 'bg-sky-50'}
              />
              <StatTile
                icon={<MessageSquare className="w-5 h-5 text-indigo-600" />}
                label="Queues"
                value={aggregate.totalQueues}
                tone="bg-indigo-50"
              />
              <StatTile
                icon={<Radio className="w-5 h-5 text-purple-600" />}
                label="Topics"
                value={aggregate.totalTopics}
                tone="bg-purple-50"
              />
              <StatTile
                icon={<Layers className="w-5 h-5 text-emerald-600" />}
                label="Namespaces with Messages"
                value={`${aggregate.namespacesWithMessages} / ${namespaces.length}`}
                tone="bg-emerald-50"
              />
            </div>

            {/* Search + filters */}
            <div className="bg-white border border-gray-200 rounded-xl shadow-sm p-3 flex flex-wrap items-center gap-2">
              <div className="relative flex-1 min-w-[220px]">
                <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
                <input
                  type="text"
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  placeholder="Search queues, topics or namespaces…"
                  aria-label="Search entities"
                  className="w-full pl-9 pr-8 py-2 rounded-lg text-sm bg-white border border-gray-300 text-gray-700 placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-sky-500 focus:border-sky-500"
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
                value={providerFilter}
                onChange={(e) => {
                  const next = e.target.value as CloudProviderType | 'all';
                  setProviderFilter(next);
                  // A namespace selected under the old cloud filter may no longer be an
                  // option in the dropdown above — drop it rather than leaving the page
                  // stuck filtered to a namespace it no longer lets the user pick.
                  const selectedNs = namespaces?.find((ns) => ns.id === namespaceFilter);
                  if (selectedNs && next !== 'all' && selectedNs.cloudProvider !== next) {
                    setNamespaceFilter('');
                  }
                }}
                aria-label="Filter by cloud"
                className="px-3 py-2 rounded-lg text-sm border border-gray-300 text-gray-700 bg-white focus:outline-none focus:ring-2 focus:ring-sky-500"
              >
                <option value="all">All Clouds</option>
                <option value="azure">Azure</option>
                <option value="aws">AWS</option>
                <option value="gcp">GCP</option>
              </select>

              <select
                value={namespaceFilter}
                onChange={(e) => setNamespaceFilter(e.target.value)}
                aria-label="Filter by namespace"
                className="px-3 py-2 rounded-lg text-sm border border-gray-300 text-gray-700 bg-white focus:outline-none focus:ring-2 focus:ring-sky-500 max-w-[200px]"
              >
                <option value="">All Namespaces</option>
                {namespaces
                  .filter((ns) => providerFilter === 'all' || ns.cloudProvider === providerFilter)
                  .map((ns) => (
                    <option key={ns.id} value={ns.id}>
                      {ns.displayName || ns.name}
                    </option>
                  ))}
              </select>

              <select
                value={entityTypeFilter}
                onChange={(e) => setEntityTypeFilter(e.target.value as EntityTypeFilter)}
                aria-label="Filter by entity type"
                className="px-3 py-2 rounded-lg text-sm border border-gray-300 text-gray-700 bg-white focus:outline-none focus:ring-2 focus:ring-sky-500"
              >
                <option value="all">All Entity Types</option>
                <option value="queues">Queues only</option>
                <option value="topics">Topics only</option>
              </select>

              <select
                value={statusFilter}
                onChange={(e) => setStatusFilter(e.target.value)}
                aria-label="Filter by status"
                title="Azure reports a real entity status; AWS and GCP entities have none and always match All Status"
                className="px-3 py-2 rounded-lg text-sm border border-gray-300 text-gray-700 bg-white focus:outline-none focus:ring-2 focus:ring-sky-500"
              >
                <option value="all">All Status</option>
                {ENTITY_STATUS_OPTIONS.map((s) => (
                  <option key={s} value={s}>
                    {s}
                  </option>
                ))}
              </select>

              {filtersActive && (
                <button
                  onClick={clearFilters}
                  className="flex items-center gap-1 text-xs font-medium text-gray-500 hover:text-gray-700"
                >
                  <X className="w-3.5 h-3.5" />
                  Clear filters
                </button>
              )}
            </div>

            {visibleNamespaces.length === 0 ? (
              <div className="bg-white border border-gray-200 rounded-xl p-8 text-center text-sm text-gray-400">
                No namespaces match the current cloud/namespace filter.
              </div>
            ) : (
              visibleNamespaces.map((ns) => (
                <NamespaceEntitiesSection
                  key={ns.id}
                  namespace={ns}
                  tab={tab}
                  navPrefix={navPrefix}
                  filter={filter}
                  entityTypeFilter={entityTypeFilter}
                  statusFilter={statusFilter}
                />
              ))
            )}

            {/* Guidance footer */}
            <div className="flex flex-wrap items-center justify-between gap-4 bg-sky-50 border border-sky-100 rounded-xl px-5 py-4">
              <div className="flex items-center gap-3">
                <Hash className="w-5 h-5 text-sky-500 shrink-0" />
                <div>
                  <p className="text-sm font-semibold text-sky-900">Monitor your live messages</p>
                  <p className="text-xs text-sky-700">
                    Track message buildup, identify hotspots and take action before it impacts your applications.
                  </p>
                </div>
              </div>
              <button
                onClick={() => navigate('/fleet')}
                className="shrink-0 flex items-center gap-1.5 px-4 py-2 text-sm font-medium text-white bg-sky-600 hover:bg-sky-700 rounded-lg transition-colors"
              >
                Go to Fleet Overview
                <ChevronRight className="w-3.5 h-3.5" />
              </button>
            </div>
          </>
        )}
      </div>
    </div>
  );
}

export default MessagesOverviewPage;
