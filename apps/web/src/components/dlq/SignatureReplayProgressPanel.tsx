import { useState } from 'react';
import { CheckCircle2, ChevronDown, ChevronUp, Loader2, StopCircle, X, XCircle } from 'lucide-react';
import { useCancelSignatureReplayJob, useSignatureReplayJob } from '@servicehub/ui-shared/hooks/useSignatureReplay';
import { isTerminalBulkOperationStatus } from '@servicehub/ui-shared/lib/api/bulkOperations';

interface SignatureReplayProgressPanelProps {
  jobId: string;
  namespaceId: string;
  signatureHash: string;
  onDismiss: () => void;
}

const STATUS_LABEL: Record<string, string> = {
  Pending: 'Queued',
  Running: 'Running',
  Completed: 'Completed',
  CompletedWithErrors: 'Completed with errors',
  Failed: 'Failed',
  Cancelled: 'Cancelled',
};

/**
 * Floating, non-blocking progress card for an in-flight signature-replay job — mirrors
 * BulkOperationProgressPanel's layout, wired to the signature-replay job endpoints instead.
 */
export function SignatureReplayProgressPanel({
  jobId,
  namespaceId,
  signatureHash,
  onDismiss,
}: SignatureReplayProgressPanelProps) {
  const { data: job } = useSignatureReplayJob(jobId, namespaceId, signatureHash);
  const cancelJob = useCancelSignatureReplayJob();
  const [showFailures, setShowFailures] = useState(false);

  if (!job) return null;

  const terminal = isTerminalBulkOperationStatus(job.status);
  const percent = job.totalMatched > 0 ? Math.round((job.processedCount / job.totalMatched) * 100) : 0;

  return (
    <div
      className="fixed bottom-4 right-4 z-40 w-80 bg-white rounded-xl shadow-2xl border border-gray-200 p-4"
      role="status"
      aria-live="polite"
    >
      <div className="flex items-center justify-between mb-2">
        <div className="flex items-center gap-2">
          {!terminal && <Loader2 className="w-4 h-4 text-sky-600 animate-spin" />}
          {job.status === 'Completed' && <CheckCircle2 className="w-4 h-4 text-green-600" />}
          {job.status === 'CompletedWithErrors' && <XCircle className="w-4 h-4 text-amber-600" />}
          {job.status === 'Failed' && <XCircle className="w-4 h-4 text-red-600" />}
          {job.status === 'Cancelled' && <StopCircle className="w-4 h-4 text-gray-500" />}
          <span className="text-sm font-semibold text-gray-900">
            Signature replay — {STATUS_LABEL[job.status] ?? job.status}
          </span>
        </div>
        {terminal && (
          <button
            onClick={onDismiss}
            className="p-1 text-gray-400 hover:text-gray-600 rounded-lg"
            aria-label="Dismiss"
          >
            <X className="w-4 h-4" />
          </button>
        )}
      </div>

      <p className="text-xs text-gray-500 mb-2 truncate">{job.namespaceDisplayName}</p>

      <div className="h-2 bg-gray-100 rounded-full overflow-hidden mb-2">
        <div
          className={`h-full transition-all duration-300 ${
            job.status === 'Failed'
              ? 'bg-red-500'
              : job.status === 'CompletedWithErrors'
                ? 'bg-amber-500'
                : job.status === 'Cancelled'
                  ? 'bg-gray-400'
                  : 'bg-sky-500'
          }`}
          style={{ width: `${percent}%` }}
        />
      </div>

      <div className="flex items-center justify-between text-xs text-gray-600 mb-3">
        <span>
          {job.processedCount.toLocaleString()} / {job.totalMatched.toLocaleString()} processed
        </span>
        <span>{percent}%</span>
      </div>

      <div className="flex items-center gap-3 text-xs mb-3">
        <span className="text-green-700">{job.successCount} succeeded</span>
        {job.failureCount > 0 && <span className="text-red-600">{job.failureCount} failed</span>}
        {job.skippedCount > 0 && <span className="text-gray-500">{job.skippedCount} skipped</span>}
      </div>

      {job.errorSummary && (
        <p className="text-xs text-red-600 mb-3 break-words">{job.errorSummary}</p>
      )}

      {job.failureSample && job.failureSample.length > 0 && (
        <div className="mb-3">
          <button
            onClick={() => setShowFailures((prev) => !prev)}
            className="flex items-center gap-1 text-xs font-medium text-gray-500 hover:text-gray-700"
          >
            {showFailures ? <ChevronUp className="w-3.5 h-3.5" /> : <ChevronDown className="w-3.5 h-3.5" />}
            {showFailures ? 'Hide' : 'View'} failure details ({job.failureSample.length})
          </button>
          {showFailures && (
            <ul className="mt-2 max-h-32 overflow-y-auto space-y-1.5 border-t border-gray-100 pt-2">
              {job.failureSample.map((failure, index) => (
                <li key={`${failure.messageId}-${index}`} className="text-xs">
                  <p className="font-medium text-gray-700 truncate">
                    {failure.entityName} · {failure.messageId}
                  </p>
                  <p className="text-red-600 break-words">{failure.reason}</p>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      {!terminal && job.isCancellable && (
        <button
          onClick={() => cancelJob.mutate(job.id)}
          disabled={cancelJob.isPending}
          className="w-full flex items-center justify-center gap-1.5 px-3 py-1.5 text-xs font-medium text-gray-600 border border-gray-200 rounded-lg hover:bg-gray-50 disabled:opacity-50 transition-colors"
        >
          <StopCircle className="w-3.5 h-3.5" />
          {cancelJob.isPending ? 'Cancelling…' : 'Cancel'}
        </button>
      )}
    </div>
  );
}
