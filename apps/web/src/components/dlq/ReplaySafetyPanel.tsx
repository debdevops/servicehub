import { CheckCircle, AlertTriangle, History } from 'lucide-react';
import { useProviderCapabilities } from '@servicehub/ui-shared/hooks/useCloudBridge';
import { getProviderCapabilities } from '@servicehub/ui-shared/lib/api/cloudBridge';
import { useSignatureReplayHistory } from '@servicehub/ui-shared/hooks/useSignatureReplay';
import { computeReplaySafetyVerdict } from './replaySafetyVerdict';

interface ReplaySafetyPanelProps {
  namespaceId: string;
  signatureHash: string;
  cloudProvider?: string;
  onStartReplay: () => void;
}

const JOB_STATUS_STYLES: Record<string, { bg: string; text: string }> = {
  Completed: { bg: 'bg-green-100', text: 'text-green-700' },
  CompletedWithErrors: { bg: 'bg-orange-100', text: 'text-orange-700' },
  Failed: { bg: 'bg-red-100', text: 'text-red-700' },
  Cancelled: { bg: 'bg-gray-100', text: 'text-gray-600' },
  Running: { bg: 'bg-blue-100', text: 'text-blue-700' },
  Pending: { bg: 'bg-amber-100', text: 'text-amber-700' },
};

function formatDate(ts: string): string {
  return new Date(ts).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

/**
 * Replay Safety & History panel (§6.4) — the one genuinely new panel in the Investigation
 * Workspace. Surfaces the provider's replay-safety caveats and this signature's past replay
 * job outcomes, and points at Start Replay (safe) or the capability note / Root Cause &
 * Knowledge (review) instead of duplicating either flow.
 */
export function ReplaySafetyPanel({ namespaceId, signatureHash, cloudProvider, onStartReplay }: ReplaySafetyPanelProps) {
  const { data: capabilitiesMap } = useProviderCapabilities();
  const capabilities = getProviderCapabilities(capabilitiesMap, cloudProvider);
  const { data: history, isLoading } = useSignatureReplayHistory(namespaceId, signatureHash);

  const mostRecentJob = history?.items[0];
  const verdict = computeReplaySafetyVerdict(capabilities, mostRecentJob);

  return (
    <div className="bg-white border border-gray-200 rounded-xl p-5">
      <h2 className="text-sm font-semibold text-gray-700 uppercase tracking-wider mb-3">Replay Safety &amp; History</h2>

      {capabilities?.notes && (
        <p className="text-sm text-gray-600 bg-gray-50 border border-gray-200 rounded-lg p-3 mb-3">{capabilities.notes}</p>
      )}

      <div
        className={`flex items-center gap-2 rounded-lg p-3 mb-4 text-sm font-medium ${
          verdict === 'safe' ? 'bg-green-50 text-green-700' : 'bg-amber-50 text-amber-700'
        }`}
      >
        {verdict === 'safe' ? (
          <CheckCircle className="w-4 h-4 shrink-0" />
        ) : (
          <AlertTriangle className="w-4 h-4 shrink-0" />
        )}
        <span className="flex-1">
          {verdict === 'safe'
            ? 'Safe to replay'
            : 'Review before replay — see the capability note above and Root Cause & Knowledge for guidance'}
        </span>
        {verdict === 'safe' && (
          <button
            onClick={onStartReplay}
            className="px-2.5 py-1 text-xs font-medium rounded-md bg-white border border-green-300 text-green-700 hover:bg-green-100"
          >
            Start Replay
          </button>
        )}
      </div>

      <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wider mb-2 flex items-center gap-1.5">
        <History className="w-3.5 h-3.5" />
        Past Replay Attempts
      </h3>

      {isLoading ? (
        <p className="text-sm text-gray-500">Loading replay history…</p>
      ) : !history || history.items.length === 0 ? (
        <p className="text-sm text-gray-500">No replay attempts recorded for this signature yet.</p>
      ) : (
        <div className="divide-y divide-gray-100">
          {history.items.map(job => {
            const style = JOB_STATUS_STYLES[job.status] || JOB_STATUS_STYLES.Pending;
            return (
              <div key={job.id} className="py-2 flex items-center justify-between gap-3">
                <div className="min-w-0">
                  <p className="text-sm text-gray-900">
                    {job.successCount}/{job.totalMatched} succeeded
                  </p>
                  <p className="text-xs text-gray-500">{formatDate(job.createdAt)}</p>
                </div>
                <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium shrink-0 ${style.bg} ${style.text}`}>
                  {job.status}
                </span>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
