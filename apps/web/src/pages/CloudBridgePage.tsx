import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { Cloud, RefreshCw, AlertCircle, AlertTriangle, ChevronLeft, ChevronRight, Inbox, Radio, GitBranch } from 'lucide-react';
import { useProviderStatus, useCloudEntities, useProviderCapabilities } from '@servicehub/ui-shared/hooks/useCloudBridge';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useNamespaceStats } from '@servicehub/ui-shared/hooks/useQueues';
import { getProviderStyle, PROVIDER_STATE_STYLES } from '@servicehub/ui-shared/lib/providerStyles';
import { getProviderConnectionState, type ProviderInstallState } from '@servicehub/ui-shared/lib/providerConnectionState';
import { ProviderIcon } from '@servicehub/ui-shared/components/ProviderIcon';
import { EmptyState } from '@/components/EmptyState';
import { getProviderCapabilities, type CloudEntity, type ProviderCapabilitiesMap } from '@servicehub/ui-shared/lib/api/cloudBridge';
import type { CloudProviderType } from '@servicehub/ui-shared/lib/api/types';

const PROVIDER_LABELS: Record<string, string> = {
  Azure: 'Azure Service Bus',
  Aws: 'AWS SQS / SNS',
  Gcp: 'GCP Pub/Sub',
};

const PROVIDER_KEY_TO_TYPE: Record<string, CloudProviderType> = {
  Azure: 'azure',
  Aws: 'aws',
  Gcp: 'gcp',
};

const ROWS_PER_PAGE_OPTIONS = [10, 25, 50, 100];

function classifyEntity(entityType: string): 'Queue' | 'Topic' | 'Subscription' | 'Other' {
  if (entityType === 'Queue') return 'Queue';
  if (entityType === 'Subscription') return 'Subscription';
  if (entityType.endsWith('Topic')) return 'Topic';
  return 'Other';
}

function EntityRow({ entity }: { entity: CloudEntity }) {
  const kind = classifyEntity(entity.entityType);
  return (
    <tr className="border-b border-gray-100 hover:bg-gray-50 text-sm">
      <td className="px-4 py-3 font-mono text-gray-800">{entity.name}</td>
      <td className="px-4 py-3">
        <span className="inline-flex items-center gap-1.5 text-xs font-medium text-gray-500">
          <span className={`w-5 h-5 rounded-md flex items-center justify-center shrink-0 ${
            kind === 'Queue' ? 'bg-sky-50 text-sky-600' : kind === 'Topic' ? 'bg-violet-50 text-violet-600' : 'bg-amber-50 text-amber-600'
          }`}>
            {kind === 'Queue' && <Inbox className="w-3 h-3" />}
            {kind === 'Topic' && <Radio className="w-3 h-3" />}
            {kind === 'Subscription' && <GitBranch className="w-3 h-3" />}
          </span>
          {entity.entityType}
        </span>
      </td>
      <td className="px-4 py-3 text-right tabular-nums">
        {entity.messageCount == null ? (
          <span className="text-gray-300">—</span>
        ) : entity.messageCount < 0 ? (
          <span title="This provider does not expose a message count API" className="text-gray-400">N/A</span>
        ) : (
          entity.messageCount.toLocaleString()
        )}
      </td>
      <td className="px-4 py-3 text-right tabular-nums">
        {entity.dlqMessageCount == null ? (
          <span className="text-gray-300">—</span>
        ) : entity.dlqMessageCount < 0 ? (
          <span title="This provider does not expose a DLQ count API" className="text-gray-400">N/A</span>
        ) : entity.dlqMessageCount > 0 ? (
          <span className="inline-flex items-center gap-1 font-semibold text-red-600">
            <AlertTriangle className="w-3 h-3" />
            {entity.dlqMessageCount.toLocaleString()}
          </span>
        ) : (
          <span className="text-gray-300">0</span>
        )}
      </td>
    </tr>
  );
}

function EntityTable({ namespaceId, provider }: { namespaceId: string; provider: string }) {
  const { data, isLoading, error, refetch } = useCloudEntities({ namespaceId, provider });
  const [page, setPage] = useState(1);
  const [rowsPerPage, setRowsPerPage] = useState(10);
  const style = getProviderStyle(PROVIDER_KEY_TO_TYPE[provider] ?? 'azure');

  const counts = useMemo(() => {
    const acc = { Queue: 0, Topic: 0, Subscription: 0 };
    for (const e of data ?? []) {
      const kind = classifyEntity(e.entityType);
      if (kind !== 'Other') acc[kind] += 1;
    }
    return acc;
  }, [data]);

  const sorted = useMemo(
    () => [...(data ?? [])].sort((a, b) => a.name.localeCompare(b.name)),
    [data],
  );
  const totalPages = Math.max(1, Math.ceil(sorted.length / rowsPerPage));
  const currentPage = Math.min(page, totalPages);
  const pageItems = sorted.slice((currentPage - 1) * rowsPerPage, currentPage * rowsPerPage);

  if (isLoading) {
    return (
      <div className="h-full flex items-center gap-2 justify-center text-sm text-gray-400">
        <RefreshCw className="w-4 h-4 animate-spin" />
        Loading entities…
      </div>
    );
  }

  if (error) {
    const msg =
      error?.response?.data?.detail ??
      error?.response?.data?.message ??
      error?.message ??
      'Failed to load entities.';
    return (
      <div className="flex items-start gap-2 rounded-lg bg-red-50 border border-red-200 p-4 text-sm text-red-700">
        <AlertCircle className="w-4 h-4 mt-0.5 shrink-0" />
        <span className="flex-1">{msg}</span>
        <button className="text-xs font-medium underline shrink-0" onClick={() => refetch()}>
          Try Again
        </button>
      </div>
    );
  }

  if (!data || data.length === 0) {
    return (
      <EmptyState
        icon={Inbox}
        heading="No entities found"
        subtext="This namespace has no queues, topics, or subscriptions to display."
      />
    );
  }

  return (
    // min-h floor: the provider-status cards above this can leave very little flex space
    // in a short viewport — without a floor, h-full collapses to a couple pixels and
    // overflow-hidden silently clips the whole table (provider bar, rows, pagination too),
    // with no scrollbar anywhere to reach it. The parent wrapper's overflow-y-auto (see
    // CloudBridgePage below) is what turns that shortfall into an actual scroll instead.
    <div className="h-full min-h-[360px] rounded-xl border border-gray-200 bg-white shadow-sm overflow-hidden flex flex-col">
      {/* Provider summary bar */}
      <div className={`shrink-0 flex flex-wrap items-center gap-x-5 gap-y-2 px-5 py-3.5 border-b ${style.headerBorder} ${style.headerBg}`}>
        <span className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg text-sm font-semibold border ${style.badge}`}>
          <ProviderIcon provider={PROVIDER_KEY_TO_TYPE[provider]} className="w-4 h-4" />
          {PROVIDER_LABELS[provider] ?? provider}
        </span>
        <span className="inline-flex items-center gap-1.5 text-xs text-gray-600">
          <Inbox className="w-3.5 h-3.5 text-gray-400" />
          Queues <span className="font-bold text-gray-900">{counts.Queue.toLocaleString()}</span>
        </span>
        <span className="inline-flex items-center gap-1.5 text-xs text-gray-600">
          <Radio className="w-3.5 h-3.5 text-gray-400" />
          Topics <span className="font-bold text-gray-900">{counts.Topic.toLocaleString()}</span>
        </span>
        <span className="inline-flex items-center gap-1.5 text-xs text-gray-600">
          <GitBranch className="w-3.5 h-3.5 text-gray-400" />
          Subscriptions <span className="font-bold text-gray-900">{counts.Subscription.toLocaleString()}</span>
        </span>
      </div>

      {/* Table — sticky header, independently scrollable body filling remaining height */}
      <div className="flex-1 min-h-0 overflow-y-auto">
        <table className="w-full text-left border-collapse" aria-label="Cloud Bridge entities">
          <thead className="sticky top-0 z-10 bg-gray-50 text-xs font-semibold text-gray-500 uppercase tracking-wider shadow-sm">
            <tr>
              <th scope="col" className="px-4 py-2.5">Name</th>
              <th scope="col" className="px-4 py-2.5">Type</th>
              <th scope="col" className="px-4 py-2.5 text-right">Messages</th>
              <th scope="col" className="px-4 py-2.5 text-right">DLQ</th>
            </tr>
          </thead>
          <tbody>
            {pageItems.map((entity) => (
              <EntityRow key={`${entity.name}-${entity.entityType}`} entity={entity} />
            ))}
          </tbody>
        </table>
      </div>

      {/* Pagination */}
      <div className="shrink-0 flex items-center justify-between gap-4 px-4 py-3 border-t border-gray-100 text-sm">
        <div className="flex items-center gap-2 text-gray-500">
          <span>Rows per page:</span>
          <select
            value={rowsPerPage}
            onChange={(e) => {
              setRowsPerPage(Number(e.target.value));
              setPage(1);
            }}
            className="rounded-lg border border-gray-200 px-2 py-1 text-sm focus:outline-none focus:ring-2 focus:ring-primary-400"
          >
            {ROWS_PER_PAGE_OPTIONS.map((n) => (
              <option key={n} value={n}>{n}</option>
            ))}
          </select>
        </div>
        <div className="flex items-center gap-1">
          <button
            onClick={() => setPage((p) => Math.max(1, p - 1))}
            disabled={currentPage <= 1}
            className="p-1.5 rounded-lg border border-gray-200 text-gray-500 hover:bg-gray-50 disabled:opacity-40 disabled:cursor-not-allowed"
            aria-label="Previous page"
          >
            <ChevronLeft className="w-4 h-4" />
          </button>
          <span className="px-3 py-1 rounded-lg bg-primary-500 text-white text-xs font-semibold min-w-[2rem] text-center">
            {currentPage}
          </span>
          <span className="text-gray-400 text-xs px-1">of {totalPages}</span>
          <button
            onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
            disabled={currentPage >= totalPages}
            className="p-1.5 rounded-lg border border-gray-200 text-gray-500 hover:bg-gray-50 disabled:opacity-40 disabled:cursor-not-allowed"
            aria-label="Next page"
          >
            <ChevronRight className="w-4 h-4" />
          </button>
        </div>
      </div>
    </div>
  );
}

/** Per-provider status card — provider identity, install/connection state, and (once
 * connected) a quick roll-up of how many namespaces are connected and whether any
 * have a DLQ backlog. "Not configured" and "not available on this server" are kept
 * visually distinct: both are inert, but only the latter is outside the operator's
 * control from inside the app. */
function ProviderStatusCard({
  providerKey,
  state,
  namespaceCount,
  dlqCount,
  capabilitiesMap,
}: {
  providerKey: string;
  state: ProviderInstallState;
  namespaceCount: number;
  dlqCount: number;
  capabilitiesMap: ProviderCapabilitiesMap | undefined;
}) {
  const providerType = PROVIDER_KEY_TO_TYPE[providerKey];
  const style = getProviderStyle(providerType);
  const stateStyle = PROVIDER_STATE_STYLES[state];
  const label = PROVIDER_LABELS[providerKey] ?? providerKey;
  const isUnavailable = state === 'unavailable';
  const hasStats = state === 'connected' || state === 'connection-issue';
  // GCP Pub/Sub has no count API — its DLQ total normalises to 0, indistinguishable
  // from a confirmed-empty DLQ, so "No backlog" would be an unearned claim. Default to false
  // (not true) while capabilities are still unknown/loading, for the same reason — an unknown
  // provider must never be assumed to support live counts.
  const supportsCounts = getProviderCapabilities(capabilitiesMap, providerType)?.supportsMessageCounts ?? false;

  return (
    <div
      className={`flex-1 min-w-[220px] rounded-xl border p-4 transition-colors ${
        isUnavailable ? 'bg-gray-50 border-gray-200' : `bg-white ${style.headerBorder} border-l-4 ${style.accentBorder}`
      }`}
    >
      <div className="flex items-center gap-3">
        <div className={`w-9 h-9 rounded-lg flex items-center justify-center shrink-0 border overflow-hidden border-gray-200 bg-white shadow-sm ${stateStyle.iconClass}`}>
          <ProviderIcon provider={providerType} className="w-full h-full" />
        </div>
        <div className="min-w-0 flex-1">
          <p className="text-sm font-semibold text-gray-900 truncate">{label}</p>
          <span className={`inline-flex items-center gap-1 text-xs font-medium ${stateStyle.textClass}`}>
            {stateStyle.dotClass && <span className={`w-1.5 h-1.5 rounded-full ${stateStyle.dotClass}`} />}
            {stateStyle.label}
          </span>
        </div>
      </div>

      {hasStats && (
        <div className="mt-3 pt-3 border-t border-gray-100 flex items-center justify-between text-xs">
          <span className="text-gray-500">
            <span className="font-bold text-gray-800">{namespaceCount}</span> namespace{namespaceCount === 1 ? '' : 's'} connected
          </span>
          {dlqCount > 0 ? (
            <span className="inline-flex items-center gap-1 font-semibold text-red-600">
              <AlertTriangle className="w-3 h-3" />
              {dlqCount.toLocaleString()} dead-lettered
            </span>
          ) : supportsCounts ? (
            <span className="text-gray-400">No backlog</span>
          ) : (
            <span className="text-gray-400">No live count available</span>
          )}
        </div>
      )}

      {state === 'available-unconfigured' && (
        <div className="mt-3 pt-3 border-t border-gray-100">
          <Link to="/connect" className="text-xs font-medium text-primary-600 hover:text-primary-700">
            + Add a connection
          </Link>
        </div>
      )}
    </div>
  );
}

export function CloudBridgePage() {
  const { data: providerStatus, isLoading: statusLoading } = useProviderStatus();
  const { data: capabilitiesMap } = useProviderCapabilities();
  const { data: namespaces } = useNamespaces();
  const [selectedNamespaceId, setSelectedNamespaceId] = useState<string>('');

  const enabledProviders = providerStatus
    ? Object.entries(providerStatus).filter(([, enabled]) => enabled)
    : [];
  const hasEnabledProviders = enabledProviders.length > 0;

  // A namespace belongs to exactly one cloud — only query that cloud's provider for it.
  const selectedNamespace = namespaces?.find((ns) => ns.id === selectedNamespaceId);
  const providerForNamespace = selectedNamespace
    ? Object.keys(PROVIDER_LABELS).find((key) => key.toLowerCase() === selectedNamespace.cloudProvider)
    : undefined;

  // DLQ rollup per namespace — same cached query the Header/Quick Access already warm,
  // so this adds no meaningful extra network cost.
  const namespaceIds = namespaces?.map((ns) => ns.id) ?? [];
  const statsResults = useNamespaceStats(namespaceIds);
  const dlqByNamespaceId = new Map(namespaceIds.map((id, i) => [id, statsResults[i]?.data?.totalDlq ?? 0]));

  return (
    <div className="flex-1 flex flex-col overflow-hidden bg-gray-50">
      {/* Fixed header area — title, provider status, namespace selector. Only the
          entity grid below scrolls; nothing here moves once a namespace is picked. */}
      <div className="shrink-0 p-6 pb-4 max-w-6xl w-full mx-auto space-y-4">
        <div className="flex items-center gap-3">
          <div className="w-11 h-11 rounded-xl bg-primary-50 border border-primary-100 flex items-center justify-center shrink-0">
            <Cloud className="w-6 h-6 text-primary-600" />
          </div>
          <div>
            <h1 className="text-xl font-bold text-gray-900">Cloud Bridge</h1>
            <p className="text-sm text-gray-500">
              Browse queues, topics and subscriptions across Azure Service Bus, AWS SQS / SNS, and GCP Pub/Sub.
            </p>
          </div>
        </div>

        {/* Provider status */}
        <div className="rounded-xl border border-gray-200 bg-white shadow-sm p-5">
          <h2 className="text-xs font-bold text-gray-500 uppercase tracking-wider mb-3">Provider Status</h2>
          {statusLoading ? (
            <div className="flex items-center gap-2 text-sm text-gray-400">
              <RefreshCw className="w-4 h-4 animate-spin" />
              Checking providers…
            </div>
          ) : (
            <div className="flex flex-wrap gap-3">
              {Object.keys(PROVIDER_LABELS).map((key) => {
                const providerType = PROVIDER_KEY_TO_TYPE[key];
                const state = getProviderConnectionState(providerStatus, namespaces, providerType);
                const nsForProvider = namespaces?.filter((ns) => ns.cloudProvider === providerType) ?? [];
                const dlqForProvider = nsForProvider.reduce((sum, ns) => sum + (dlqByNamespaceId.get(ns.id) ?? 0), 0);
                return (
                  <ProviderStatusCard
                    key={key}
                    providerKey={key}
                    state={state}
                    namespaceCount={nsForProvider.length}
                    dlqCount={dlqForProvider}
                    capabilitiesMap={capabilitiesMap}
                  />
                );
              })}
            </div>
          )}
          {!statusLoading && !hasEnabledProviders && (
            <p className="mt-4 text-sm text-amber-600 bg-amber-50 border border-amber-200 rounded-lg px-4 py-3">
              No cloud providers are currently enabled. Set{' '}
              <code className="font-mono text-xs">CloudProviders:Aws:Enabled=true</code> or{' '}
              <code className="font-mono text-xs">CloudProviders:Gcp:Enabled=true</code> in your configuration to
              activate them.
            </p>
          )}
        </div>

        {hasEnabledProviders && (
          <div className="rounded-xl border border-gray-200 bg-white shadow-sm p-4 flex items-center gap-4">
            <label htmlFor="namespace-select" className="text-sm font-semibold text-gray-700 shrink-0">
              Namespace
            </label>
            <select
              id="namespace-select"
              className="flex-1 rounded-lg border border-gray-200 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary-400"
              value={selectedNamespaceId}
              onChange={(e) => setSelectedNamespaceId(e.target.value)}
            >
              <option value="">Select a namespace…</option>
              {namespaces?.map((ns) => (
                <option key={ns.id} value={ns.id}>
                  {ns.displayName ?? ns.name}
                </option>
              ))}
            </select>
          </div>
        )}
      </div>

      {/* Scrollable entity grid — fills all remaining vertical space. overflow-y-auto is the
          fallback for a short viewport: EntityTable's min-h-[360px] floor keeps it from being
          crushed to nothing, and this scroll is what makes anything past that floor reachable
          when the fixed header above leaves less than 360px of room. */}
      {hasEnabledProviders && (
        <div className="flex-1 min-h-0 overflow-y-auto px-6 pb-6 max-w-6xl w-full mx-auto">
          {selectedNamespaceId && providerForNamespace ? (
            <EntityTable namespaceId={selectedNamespaceId} provider={providerForNamespace} />
          ) : (
            <p className="h-full flex items-center justify-center text-center text-sm text-gray-400">
              Select a namespace above to browse its entities.
            </p>
          )}
        </div>
      )}
    </div>
  );
}
