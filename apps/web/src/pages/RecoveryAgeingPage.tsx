import { Link } from 'react-router-dom';
import { Clock, AlertCircle, RefreshCw, Info } from 'lucide-react';
import { useRecoveryAgeing } from '@servicehub/ui-shared/hooks/useRecoveryAgeing';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { RecoveryStateBadge } from '@/components/recovery/RecoveryStateBadge';

function ageInDays(begunAt: string): number {
  return Math.floor((Date.now() - new Date(begunAt).getTime()) / 86_400_000);
}

/**
 * `/recovery/ageing` — every currently open (non-terminal) recovery ledger entry, oldest first.
 * The falsifiable form of "nothing is silently lost" (roadmap §7.2): an entry stays here,
 * unconditionally, for as long as it remains non-terminal — through the ageing worker flagging it
 * and, eventually, expiring it if nothing resolves it first.
 */
export default function RecoveryAgeingPage() {
  const { isDemoMode, cloudProvider } = useDemoContext();
  const navPrefix = isDemoMode && cloudProvider ? `/demo/${cloudProvider}` : '';
  const { data: entries, isLoading, isError, refetch, isFetching } = useRecoveryAgeing();

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      <div className="bg-white border-b border-gray-200 px-6 py-4 shrink-0">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-xl font-bold text-gray-900 flex items-center gap-2">
              <Clock className="w-5 h-5 text-teal-600" />
              Recovery Ageing Report
            </h1>
            <p className="text-sm text-gray-500 mt-0.5">
              Open recovery entries that have not yet reached a terminal outcome — nothing here
              disappears; it is either resolved, flagged, or eventually expired, all as durable
              evidence.
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
            Demo Mode — the curated story's entries all reach a terminal outcome, so this page is
            empty by design here.
          </div>
        )}
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
              <p className="text-gray-600 font-medium">Failed to load the ageing report</p>
            </div>
          </div>
        ) : !entries || entries.length === 0 ? (
          <div className="flex items-center justify-center h-64">
            <div className="text-center">
              <Clock className="w-10 h-10 text-gray-300 mx-auto mb-3" />
              <p className="text-gray-500 font-medium">No open recovery entries</p>
              <p className="text-sm text-gray-400 mt-1">Every entry has reached a terminal outcome.</p>
            </div>
          </div>
        ) : (
          <table className="w-full text-sm" aria-label="Open recovery ledger entries">
            <thead className="bg-gray-50 border-b border-gray-200 sticky top-0 z-10">
              <tr>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500">Target</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500 w-24">State</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500 w-24">Age</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500">Namespace</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500 w-32">Operation</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {entries.map(entry => {
                const days = ageInDays(entry.begunAt);
                return (
                  <tr key={entry.id} className={days >= 7 ? 'bg-amber-50/40' : undefined}>
                    <td className="px-4 py-3 text-gray-700 font-mono text-xs">{entry.targetEntity}</td>
                    <td className="px-4 py-3"><RecoveryStateBadge state={entry.state} /></td>
                    <td className="px-4 py-3 text-gray-600 text-xs">
                      {days}d{days >= 7 && <span className="ml-1 text-amber-600 font-medium">· flagged</span>}
                    </td>
                    <td className="px-4 py-3 text-gray-500 text-xs">{entry.namespaceNameSnapshot ?? '—'}</td>
                    <td className="px-4 py-3">
                      <Link to={`${navPrefix}/recovery/${entry.operationId}`} className="text-xs text-teal-600 hover:underline">
                        View operation
                      </Link>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
