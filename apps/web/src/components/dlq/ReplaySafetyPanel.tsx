import { CheckCircle, AlertTriangle, History } from 'lucide-react';
import { useProviderCapabilities } from '@servicehub/ui-shared/hooks/useCloudBridge';
import { getProviderCapabilities } from '@servicehub/ui-shared/lib/api/cloudBridge';
import { useSignatureReplayHistory } from '@servicehub/ui-shared/hooks/useSignatureReplay';
import { formatRelativeTime } from '@servicehub/ui-shared/lib/utils';
import {
  computeReplaySafetyVerdict,
  summarizeReplayHistory,
  computeReplayRecurrence,
  computeReplayRecommendation,
  CONSECUTIVE_FAILURE_THRESHOLD,
} from './replaySafetyVerdict';
import { JOB_STATUS_STYLES } from './replayStatusStyles';

interface ReplaySafetyPanelProps {
  namespaceId: string;
  signatureHash: string;
  cloudProvider?: string;
  /** The signature's own most recent observed occurrence — compared against the last replay's completion to answer "did it recur after replay." */
  lastSeenAt?: string;
  onStartReplay: () => void;
}

/** Replay Safety & History panel fetches more history than the raw list needs, so the counts and streak below reflect the signature's real history, not just a short page. */
const HISTORY_PAGE_SIZE = 50;

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
 * Workspace. Surfaces the provider's replay-safety caveats, deterministic counts/streak/recurrence
 * evidence over this signature's past replay jobs, and points at Start Replay (safe) or the
 * capability note / Root Cause & Knowledge (review) instead of duplicating either flow.
 */
export function ReplaySafetyPanel({ namespaceId, signatureHash, cloudProvider, lastSeenAt, onStartReplay }: ReplaySafetyPanelProps) {
  const { data: capabilitiesMap } = useProviderCapabilities();
  const capabilities = getProviderCapabilities(capabilitiesMap, cloudProvider);
  const { data: history, isLoading } = useSignatureReplayHistory(namespaceId, signatureHash, 1, HISTORY_PAGE_SIZE);

  const items = history?.items ?? [];
  const mostRecentJob = items[0];
  const verdict = computeReplaySafetyVerdict(capabilities, mostRecentJob);
  const summary = summarizeReplayHistory(items, history?.totalCount ?? 0);
  const recurrence = computeReplayRecurrence(items, lastSeenAt);
  const recommendation = computeReplayRecommendation(verdict, summary);

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

      {!isLoading && items.length > 0 && (
        <div className="bg-gray-50 border border-gray-200 rounded-lg p-3 mb-4 space-y-1.5">
          <p className="text-sm text-gray-800">
            Replay history: <span className="font-medium">{summary.successCount} successful, {summary.failureCount} failed</span>
            {summary.cancelledCount > 0 && <span>, {summary.cancelledCount} cancelled</span>}
            {summary.skippedMessageCount > 0 && <span>, {summary.skippedMessageCount} message(s) skipped</span>}
            {'. '}
            {summary.lastReplayAt && <>Last replay {formatRelativeTime(new Date(summary.lastReplayAt))}.</>}
            {summary.isTruncated && <span className="text-gray-500"> (showing the most recent {items.length})</span>}
          </p>

          {summary.consecutiveFailureCount >= CONSECUTIVE_FAILURE_THRESHOLD && (
            <p className="text-sm text-red-700">
              Replay failed {summary.consecutiveFailureCount} consecutive times.
            </p>
          )}

          {recurrence && (
            <p className="text-sm text-gray-800">
              {recurrence.recurred
                ? `This signature recurred ${formatRelativeTime(new Date(recurrence.replayCompletedAt))} after the last replay attempt.`
                : 'No recurrence since the last replay attempt.'}
            </p>
          )}

          {recommendation && <p className="text-sm font-medium text-gray-900">{recommendation}</p>}
        </div>
      )}

      <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wider mb-2 flex items-center gap-1.5">
        <History className="w-3.5 h-3.5" />
        Past Replay Attempts
      </h3>

      {isLoading ? (
        <p className="text-sm text-gray-500">Loading replay history…</p>
      ) : items.length === 0 ? (
        <p className="text-sm text-gray-500">No replay attempts recorded for this signature yet.</p>
      ) : (
        <div className="divide-y divide-gray-100">
          {items.map(job => {
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
