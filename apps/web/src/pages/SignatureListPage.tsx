import { useMemo, useState, useEffect } from 'react';
import { useSearchParams, Link } from 'react-router-dom';
import { Sparkles } from 'lucide-react';
import { useDlqSignatures } from '@servicehub/ui-shared/hooks/useDlqSignatures';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { ProviderBadge } from '@servicehub/ui-shared/lib/providerStyles';
import type { DlqClusterSignature } from '@servicehub/ui-shared/lib/api/dlqSignatures';
import { StatusBadge, TrendBadge, FailureInvestigationPanel } from '@/components/dlq';

const STATUS_OPTIONS = ['Active', 'Resolved', 'Reopened', 'Suppressed', 'Archived'] as const;
const TREND_OPTIONS = ['New', 'Recurring', 'Escalating'] as const;

function SignatureListItem({ signature, namespaceId }: { signature: DlqClusterSignature; namespaceId: string }) {
  return (
    <div className="space-y-3">
      <div className="bg-white border border-gray-200 rounded-xl p-4">
        <div className="flex items-start justify-between gap-2 mb-2 flex-wrap">
          <div className="flex items-center gap-2 flex-wrap">
            <StatusBadge status={signature.status} />
            <TrendBadge trend={signature.trend} />
            <span className="text-xs text-gray-500">
              {signature.size} message{signature.size === 1 ? '' : 's'} · {signature.dominantEntity}
            </span>
          </div>
          <Link
            to={`/signatures/${signature.signatureHash}?namespace=${namespaceId}`}
            className="text-xs text-primary-600 hover:text-primary-700 font-medium shrink-0"
          >
            View details →
          </Link>
        </div>

        <p className="text-sm font-medium text-gray-900 mb-1">{signature.dominantDeadletterReason}</p>
        <p className="text-sm text-gray-600">{signature.explanation}</p>
      </div>

      <FailureInvestigationPanel cluster={signature} />
    </div>
  );
}

export function SignatureListPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const urlNamespaceId = searchParams.get('namespace') || undefined;
  const { isDemoMode } = useDemoContext();

  const { data: namespaces } = useNamespaces();
  const activeNamespace = namespaces?.find(ns => ns.isActive);
  const namespaceId = urlNamespaceId || activeNamespace?.id;
  const currentNamespace = namespaces?.find(ns => ns.id === namespaceId);

  useEffect(() => {
    if (namespaceId && namespaceId !== urlNamespaceId && namespaces) {
      setSearchParams({ namespace: namespaceId }, { replace: true });
    }
  }, [namespaceId, urlNamespaceId, namespaces, setSearchParams]);

  const [statusFilter, setStatusFilter] = useState<string | undefined>();
  const [trendFilter, setTrendFilter] = useState<string | undefined>();

  const { data, loading, available } = useDlqSignatures(namespaceId);

  const filteredClusters = useMemo(() => {
    const clusters = data?.clusters ?? [];
    return clusters.filter(c => {
      if (statusFilter && c.status !== statusFilter) return false;
      if (trendFilter && c.trend !== trendFilter) return false;
      return true;
    });
  }, [data, statusFilter, trendFilter]);

  return (
    <div className="p-6 max-w-5xl mx-auto">
      <div className="flex items-center gap-2 mb-4">
        <Sparkles className="w-5 h-5 text-primary-500" />
        <h1 className="text-xl font-semibold text-gray-900">Failure Signatures</h1>
      </div>

      {/* Namespace selector */}
      <div className="flex items-center gap-3 mb-4 flex-wrap">
        <select
          value={namespaceId ?? ''}
          onChange={e => setSearchParams({ namespace: e.target.value })}
          className="px-3 py-1.5 text-sm rounded-lg border border-gray-200 bg-white"
        >
          <option value="" disabled>Select a namespace…</option>
          {namespaces?.map(ns => (
            <option key={ns.id} value={ns.id}>{ns.displayName || ns.name}</option>
          ))}
        </select>
        {currentNamespace?.cloudProvider && <ProviderBadge provider={currentNamespace.cloudProvider} />}
      </div>

      {isDemoMode ? (
        <div className="bg-gray-50 border border-gray-200 rounded-xl p-6 text-center text-sm text-gray-600">
          Signature browsing requires a live connection — try a real namespace.
        </div>
      ) : !namespaceId ? (
        <div className="bg-gray-50 border border-gray-200 rounded-xl p-6 text-center text-sm text-gray-600">
          Select a namespace to view its failure signatures.
        </div>
      ) : (
        <>
          {/* Filters */}
          <div className="flex items-center gap-2 mb-4 flex-wrap text-xs">
            <span className="text-gray-500 font-medium">Status:</span>
            <button
              onClick={() => setStatusFilter(undefined)}
              className={`px-2.5 py-1 rounded-full font-medium border ${!statusFilter ? 'bg-primary-600 text-white border-primary-600' : 'bg-white text-gray-600 border-gray-200'}`}
            >
              All
            </button>
            {STATUS_OPTIONS.map(status => (
              <button
                key={status}
                onClick={() => setStatusFilter(status === statusFilter ? undefined : status)}
                className={`px-2.5 py-1 rounded-full font-medium border ${statusFilter === status ? 'bg-primary-600 text-white border-primary-600' : 'bg-white text-gray-600 border-gray-200'}`}
              >
                {status}
              </button>
            ))}
            <span className="text-gray-500 font-medium ml-3">Trend:</span>
            {TREND_OPTIONS.map(trend => (
              <button
                key={trend}
                onClick={() => setTrendFilter(trend === trendFilter ? undefined : trend)}
                className={`px-2.5 py-1 rounded-full font-medium border ${trendFilter === trend ? 'bg-primary-600 text-white border-primary-600' : 'bg-white text-gray-600 border-gray-200'}`}
              >
                {trend}
              </button>
            ))}
          </div>

          {loading ? (
            <div className="text-sm text-gray-500">Loading signatures…</div>
          ) : !available ? (
            <div className="bg-gray-50 border border-gray-200 rounded-xl p-6 text-center text-sm text-gray-600">
              Signature analysis is unavailable for this namespace right now.
            </div>
          ) : filteredClusters.length === 0 ? (
            <div className="bg-gray-50 border border-gray-200 rounded-xl p-6 text-center text-sm text-gray-600">
              No failure signatures match the current filters.
            </div>
          ) : (
            <div className="space-y-3">
              {filteredClusters.map(signature => (
                <SignatureListItem
                  key={signature.signatureHash}
                  signature={signature}
                  namespaceId={namespaceId}
                />
              ))}
            </div>
          )}
        </>
      )}
    </div>
  );
}

export default SignatureListPage;
