import { useState } from 'react';
import { Cloud, RefreshCw, AlertCircle, ChevronDown, ChevronRight, Layers } from 'lucide-react';
import { useProviderStatus, useCloudEntities } from '@/hooks/useCloudBridge';
import { useNamespaces } from '@/hooks/useNamespaces';
import type { CloudEntity } from '@/lib/api/cloudBridge';

const PROVIDER_LABELS: Record<string, string> = {
  Aws: 'AWS SQS / SNS',
  Gcp: 'GCP Pub/Sub',
};

function EntityRow({ entity }: { entity: CloudEntity }) {
  return (
    <tr className="border-b border-gray-100 hover:bg-gray-50 text-sm">
      <td className="px-4 py-2 font-mono">{entity.name}</td>
      <td className="px-4 py-2 text-gray-500">{entity.entityType}</td>
      <td className="px-4 py-2 text-right tabular-nums">
        {entity.messageCount != null ? entity.messageCount.toLocaleString() : '—'}
      </td>
      <td className="px-4 py-2 text-right tabular-nums text-red-600">
        {entity.dlqMessageCount != null ? entity.dlqMessageCount.toLocaleString() : '—'}
      </td>
    </tr>
  );
}

function EntitiesPanel({
  namespaceId,
  provider,
}: {
  namespaceId: string;
  provider: string;
}) {
  const { data, isLoading, error, refetch } = useCloudEntities({ namespaceId, provider });

  if (isLoading) {
    return (
      <div className="flex items-center gap-2 py-6 justify-center text-sm text-gray-400">
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
        <span>{msg}</span>
        <button
          className="ml-auto text-xs underline"
          onClick={() => refetch()}
        >
          Retry
        </button>
      </div>
    );
  }

  if (!data || data.length === 0) {
    return (
      <p className="py-6 text-center text-sm text-gray-400">
        No entities found for this namespace.
      </p>
    );
  }

  return (
    <div className="overflow-x-auto rounded-lg border border-gray-200">
      <table className="w-full text-left">
        <thead className="bg-gray-50 text-xs font-medium text-gray-500 uppercase tracking-wider">
          <tr>
            <th className="px-4 py-2">Name</th>
            <th className="px-4 py-2">Type</th>
            <th className="px-4 py-2 text-right">Messages</th>
            <th className="px-4 py-2 text-right">DLQ</th>
          </tr>
        </thead>
        <tbody>
          {data.map((entity) => (
            <EntityRow key={`${entity.name}-${entity.entityType}`} entity={entity} />
          ))}
        </tbody>
      </table>
    </div>
  );
}

function ProviderSection({
  provider,
  label,
  namespaceId,
}: {
  provider: string;
  label: string;
  namespaceId: string;
}) {
  const [expanded, setExpanded] = useState(false);

  return (
    <div className="rounded-xl border border-gray-200 bg-white shadow-sm overflow-hidden">
      <button
        className="flex w-full items-center gap-3 px-6 py-4 hover:bg-gray-50 transition-colors"
        onClick={() => setExpanded((v) => !v)}
        aria-expanded={expanded}
      >
        {expanded ? (
          <ChevronDown className="w-4 h-4 text-gray-400" />
        ) : (
          <ChevronRight className="w-4 h-4 text-gray-400" />
        )}
        <Layers className="w-5 h-5 text-blue-500" />
        <span className="font-medium text-gray-800">{label}</span>
      </button>
      {expanded && (
        <div className="px-6 pb-6">
          <EntitiesPanel namespaceId={namespaceId} provider={provider} />
        </div>
      )}
    </div>
  );
}

export function CloudBridgePage() {
  const { data: providerStatus, isLoading: statusLoading } = useProviderStatus();
  const { data: namespaces } = useNamespaces();
  const [selectedNamespaceId, setSelectedNamespaceId] = useState<string>('');

  const enabledProviders = providerStatus
    ? Object.entries(providerStatus).filter(([, enabled]) => enabled)
    : [];

  const hasEnabledProviders = enabledProviders.length > 0;

  return (
    <div className="p-6 max-w-5xl mx-auto space-y-6">
      {/* Header */}
      <div className="flex items-center gap-3">
        <Cloud className="w-7 h-7 text-blue-500" />
        <div>
          <h1 className="text-2xl font-semibold text-gray-900">Cloud Bridge</h1>
          <p className="text-sm text-gray-500">
            Browse AWS SQS / SNS and GCP Pub/Sub entities alongside Azure Service Bus.
          </p>
        </div>
      </div>

      {/* Provider status */}
      <div className="rounded-xl border border-gray-200 bg-white shadow-sm p-6">
        <h2 className="text-sm font-semibold text-gray-700 uppercase tracking-wider mb-4">
          Provider Status
        </h2>
        {statusLoading ? (
          <div className="flex items-center gap-2 text-sm text-gray-400">
            <RefreshCw className="w-4 h-4 animate-spin" />
            Checking providers…
          </div>
        ) : (
          <div className="flex flex-wrap gap-4">
            {Object.entries(PROVIDER_LABELS).map(([key, label]) => {
              const enabled = providerStatus?.[key] ?? false;
              return (
                <div
                  key={key}
                  className={`flex items-center gap-2 rounded-lg px-4 py-2 text-sm font-medium border ${
                    enabled
                      ? 'bg-green-50 text-green-700 border-green-200'
                      : 'bg-gray-50 text-gray-400 border-gray-200'
                  }`}
                >
                  <span
                    className={`w-2 h-2 rounded-full ${enabled ? 'bg-green-500' : 'bg-gray-300'}`}
                  />
                  {label}
                  <span className="text-xs opacity-70">{enabled ? 'Active' : 'Disabled'}</span>
                </div>
              );
            })}
          </div>
        )}
        {!statusLoading && !hasEnabledProviders && (
          <p className="mt-4 text-sm text-amber-600 bg-amber-50 border border-amber-200 rounded-lg px-4 py-3">
            No cloud providers are currently enabled. Set{' '}
            <code className="font-mono text-xs">CloudProviders:Aws:Enabled=true</code> or{' '}
            <code className="font-mono text-xs">CloudProviders:Gcp:Enabled=true</code> in your
            configuration to activate them.
          </p>
        )}
      </div>

      {/* Entity browser */}
      {hasEnabledProviders && (
        <div className="space-y-4">
          <div className="flex items-center gap-4">
            <label htmlFor="namespace-select" className="text-sm font-medium text-gray-700 shrink-0">
              Namespace
            </label>
            <select
              id="namespace-select"
              className="flex-1 rounded-lg border border-gray-200 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
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

          {selectedNamespaceId &&
            enabledProviders.map(([key]) => (
              <ProviderSection
                key={key}
                provider={key}
                label={PROVIDER_LABELS[key] ?? key}
                namespaceId={selectedNamespaceId}
              />
            ))}

          {!selectedNamespaceId && (
            <p className="text-center text-sm text-gray-400 py-8">
              Select a namespace above to browse its entities.
            </p>
          )}
        </div>
      )}
    </div>
  );
}
