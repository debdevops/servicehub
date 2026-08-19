import { useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { ShieldCheck, AlertCircle, RefreshCw, Info } from 'lucide-react';
import { useRecoveryOperations } from '@servicehub/ui-shared/hooks/useRecoveryLedger';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { ProviderBadge } from '@servicehub/ui-shared/lib/providerStyles';
import { EnvironmentBadge } from '@/components/EnvironmentBadge';
import type { CloudProviderType } from '@servicehub/ui-shared/lib/api/types';
import type { RecoveryOperation } from '@servicehub/ui-shared/lib/api/recovery';

const KNOWN_PROVIDERS: readonly CloudProviderType[] = ['azure', 'aws', 'gcp'];

function CloudBadge({ provider }: { provider: string | null }) {
  if (!provider) return null;
  const normalized = provider.toLowerCase() as CloudProviderType;
  if (!KNOWN_PROVIDERS.includes(normalized)) return null;
  return <ProviderBadge provider={normalized} />;
}

function KindBadge({ kind }: { kind: RecoveryOperation['kind'] }) {
  return kind === 'Purge' ? (
    <span className="px-2 py-0.5 text-xs font-medium rounded-full bg-red-100 text-red-700">Purge</span>
  ) : (
    <span className="px-2 py-0.5 text-xs font-medium rounded-full bg-blue-100 text-blue-700">Replay</span>
  );
}

/**
 * `/recovery` — filterable list of Recovery Evidence Ledger operations, most recently opened
 * first. Each row links to `/recovery/:operationId` for the full per-entry breakdown and event
 * chain; this list intentionally stays to what one query already returns (no per-operation
 * entry-count rollup here — that would mean an N+1 query per row for a list page).
 */
export default function RecoveryLedgerPage() {
  const { isDemoMode, cloudProvider } = useDemoContext();
  const navPrefix = isDemoMode && cloudProvider ? `/demo/${cloudProvider}` : '';
  const [searchParams] = useSearchParams();
  const namespaceId = searchParams.get('namespace') || undefined;
  const [kindFilter, setKindFilter] = useState<'' | 'Replay' | 'Purge'>('');

  const { data: operations, isLoading, isError, refetch, isFetching } = useRecoveryOperations(namespaceId);

  const filtered = useMemo(
    () => (operations ?? []).filter(op => !kindFilter || op.kind === kindFilter),
    [operations, kindFilter],
  );

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      <div className="bg-white border-b border-gray-200 px-6 py-4 shrink-0">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-xl font-bold text-gray-900 flex items-center gap-2">
              <ShieldCheck className="w-5 h-5 text-teal-600" />
              Recovery Evidence Ledger
            </h1>
            <p className="text-sm text-gray-500 mt-0.5">
              Every recovery decision ServiceHub made — who acted, what it asked the provider to
              do, and what it subsequently observed.
            </p>
          </div>
          <button
            onClick={() => refetch()}
            disabled={isFetching || isDemoMode}
            className="flex items-center gap-1.5 px-3 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-200 rounded-lg hover:bg-gray-50 shadow-sm transition-all disabled:opacity-50"
          >
            <RefreshCw className={`w-4 h-4 ${isFetching ? 'animate-spin' : ''}`} />
            Refresh
          </button>
        </div>

        {isDemoMode && (
          <div className="mt-3 flex items-center gap-2 px-3 py-2 rounded-lg bg-amber-50 border border-amber-200 text-xs text-amber-800">
            <Info className="w-4 h-4 shrink-0" />
            Demo Mode — this is fixture data illustrating the evidence model, not a real recovery record.
          </div>
        )}

        <div className="mt-3 flex items-center gap-2">
          <select
            value={kindFilter}
            onChange={e => setKindFilter(e.target.value as '' | 'Replay' | 'Purge')}
            aria-label="Filter by operation kind"
            className="text-sm border border-gray-200 rounded-lg px-3 py-1.5 bg-white text-gray-700 focus:outline-none focus:ring-2 focus:ring-teal-500"
          >
            <option value="">All Kinds</option>
            <option value="Replay">Replay</option>
            <option value="Purge">Purge</option>
          </select>
        </div>
      </div>

      <div className="flex-1 overflow-auto">
        {isLoading ? (
          <div className="flex items-center justify-center h-64">
            <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-teal-600" />
          </div>
        ) : isError ? (
          <div className="flex items-center justify-center h-64">
            <div className="text-center">
              <AlertCircle className="w-10 h-10 text-red-400 mx-auto mb-3" />
              <p className="text-gray-600 font-medium">Failed to load recovery operations</p>
              <button onClick={() => refetch()} className="mt-3 px-4 py-2 text-sm text-teal-600 hover:text-teal-700 border border-teal-300 rounded-lg hover:bg-teal-50">
                Try Again
              </button>
            </div>
          </div>
        ) : filtered.length === 0 ? (
          <div className="flex items-center justify-center h-64">
            <div className="text-center">
              <ShieldCheck className="w-10 h-10 text-gray-300 mx-auto mb-3" />
              <p className="text-gray-500 font-medium">No recovery operations recorded</p>
              <p className="text-sm text-gray-500 mt-1">
                Replay and purge operations will appear here as they happen.
              </p>
            </div>
          </div>
        ) : (
          <table className="w-full text-sm" aria-label="Recovery operations">
            <thead className="bg-gray-50 border-b border-gray-200 sticky top-0 z-10">
              <tr>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500 w-40">Opened</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500">Actor</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500 w-24">Kind</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500">Scope</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500">Cloud / Env</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500 w-20">Targets</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {filtered.map(op => (
                <tr key={op.id} className="hover:bg-teal-50/40 transition-colors">
                  <td className="px-4 py-3 text-gray-500 whitespace-nowrap font-mono text-xs">
                    {new Date(op.openedAt).toLocaleString(undefined, {
                      month: 'short', day: '2-digit', hour: '2-digit', minute: '2-digit',
                    })}
                  </td>
                  <td className="px-4 py-3">
                    <Link to={`${navPrefix}/recovery/${op.id}`} className="text-gray-800 font-medium text-xs hover:text-teal-700 hover:underline">
                      {op.actorIdentity}
                    </Link>
                    <div className="text-xs text-gray-500">{op.trigger}</div>
                  </td>
                  <td className="px-4 py-3"><KindBadge kind={op.kind} /></td>
                  <td className="px-4 py-3 text-gray-600 text-xs font-mono truncate max-w-[280px]">{op.scopeDescription}</td>
                  <td className="px-4 py-3">
                    <div className="flex items-center gap-1 flex-wrap">
                      <CloudBadge provider={op.providerSnapshot} />
                      <EnvironmentBadge env={op.environmentSnapshot} />
                      {op.namespaceNameSnapshot && (
                        <span className="text-xs text-gray-500 bg-gray-100 px-1.5 py-0.5 rounded">{op.namespaceNameSnapshot}</span>
                      )}
                    </div>
                  </td>
                  <td className="px-4 py-3 text-gray-700 font-medium">{op.targetCount}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
