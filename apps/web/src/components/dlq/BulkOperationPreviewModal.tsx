import { useEffect } from 'react';
import { createPortal } from 'react-dom';
import { AlertTriangle, RefreshCw, Trash2, X, ShieldAlert } from 'lucide-react';
import { useBulkOperationPreview, useCreateBulkOperation } from '@/hooks/useBulkOperations';
import type { BulkOperationFilter, BulkOperationType } from '@/lib/api/bulkOperations';

interface BulkOperationPreviewModalProps {
  operationType: BulkOperationType;
  filter: BulkOperationFilter;
  onClose: () => void;
  onJobCreated: (jobId: string) => void;
}

/**
 * Dry-run confirmation for "replay/purge every DLQ message matching this filter" — shows the
 * exact count and a sample before anything mutates, per doc 15's "preview first, progress bar,
 * cancel" bar for what separates an operations tool from a message viewer.
 */
export function BulkOperationPreviewModal({
  operationType,
  filter,
  onClose,
  onJobCreated,
}: BulkOperationPreviewModalProps) {
  const preview = useBulkOperationPreview();
  const createJob = useCreateBulkOperation();

  useEffect(() => {
    preview.mutate({ operationType, filter });
    // Only re-run if the operation/filter identity actually changes — mutate() is stable.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [operationType, filter.namespaceId, filter.entityName, filter.status, filter.category, filter.from, filter.to]);

  const isReplay = operationType === 'Replay';
  const data = preview.data;

  const handleConfirm = async () => {
    const job = await createJob.mutateAsync({ operationType, filter });
    onJobCreated(job.id);
  };

  return createPortal(
    <div
      className="fixed inset-0 z-50 flex items-center justify-center"
      role="dialog"
      aria-modal="true"
      aria-label={`Bulk ${operationType.toLowerCase()} preview`}
    >
      <div className="absolute inset-0 bg-black/40" onClick={onClose} />
      <div className="relative bg-white rounded-2xl shadow-2xl w-full max-w-lg mx-4 p-6 max-h-[85vh] overflow-y-auto">
        <div className="flex items-center justify-between mb-4">
          <div className="flex items-center gap-2">
            {isReplay ? (
              <RefreshCw className="w-5 h-5 text-sky-600" />
            ) : (
              <Trash2 className="w-5 h-5 text-red-600" />
            )}
            <h2 className="text-base font-semibold text-gray-900">
              Bulk {isReplay ? 'Replay' : 'Purge'} Preview
            </h2>
          </div>
          <button
            onClick={onClose}
            className="p-1 text-gray-400 hover:text-gray-600 rounded-lg"
            aria-label="Close preview"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {preview.isPending ? (
          <div className="py-10 text-center text-sm text-gray-500">Matching messages…</div>
        ) : preview.isError ? (
          <div className="py-10 text-center text-sm text-red-600">
            Failed to preview this operation. Close and try again.
          </div>
        ) : data ? (
          <>
            <div className="flex items-baseline gap-2 mb-3">
              <span className="text-3xl font-bold text-gray-900">{data.totalMatched.toLocaleString()}</span>
              <span className="text-sm text-gray-500">
                message{data.totalMatched === 1 ? '' : 's'} matched
              </span>
            </div>

            {data.warnings.length > 0 && (
              <div className="mb-4 space-y-2">
                {data.warnings.map((warning, i) => (
                  <div
                    key={i}
                    className="flex items-start gap-2 px-3 py-2 rounded-lg bg-amber-50 border border-amber-200 text-sm text-amber-800"
                  >
                    <AlertTriangle className="w-4 h-4 shrink-0 mt-0.5" />
                    <span>{warning}</span>
                  </div>
                ))}
              </div>
            )}

            {data.sample.length > 0 && (
              <div className="mb-4">
                <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2">
                  Sample {data.sample.length < data.totalMatched && `(first ${data.sample.length})`}
                </h3>
                <div className="border border-gray-200 rounded-lg divide-y divide-gray-100 max-h-56 overflow-y-auto">
                  {data.sample.map((item) => (
                    <div key={item.dlqMessageId} className="px-3 py-2 text-sm">
                      <div className="flex items-center justify-between gap-2">
                        <span className="font-mono text-xs text-gray-500 truncate">{item.messageId}</span>
                        {item.replaySafety === 'Unsafe' && (
                          <span className="inline-flex items-center gap-1 text-xs font-medium text-red-600 shrink-0">
                            <ShieldAlert className="w-3 h-3" /> Unsafe
                          </span>
                        )}
                      </div>
                      <div className="text-gray-700 truncate">{item.entityName}</div>
                      {item.deadLetterReason && (
                        <div className="text-xs text-gray-400 truncate">{item.deadLetterReason}</div>
                      )}
                    </div>
                  ))}
                </div>
              </div>
            )}
          </>
        ) : null}

        <div className="flex items-center justify-end gap-2 mt-6">
          <button
            onClick={onClose}
            disabled={createJob.isPending}
            className="px-4 py-2 text-sm font-medium text-gray-600 hover:text-gray-800 hover:bg-gray-50 rounded-lg transition-colors"
          >
            Cancel
          </button>
          <button
            onClick={handleConfirm}
            disabled={!data?.canExecute || createJob.isPending}
            className={`flex items-center gap-1.5 px-4 py-2 text-sm font-medium rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed ${
              isReplay ? 'bg-sky-600 hover:bg-sky-700 text-white' : 'bg-red-600 hover:bg-red-700 text-white'
            }`}
          >
            {isReplay ? <RefreshCw className="w-3.5 h-3.5" /> : <Trash2 className="w-3.5 h-3.5" />}
            {createJob.isPending
              ? 'Starting…'
              : `${isReplay ? 'Replay' : 'Purge'} ${data?.totalMatched ?? 0} message${data?.totalMatched === 1 ? '' : 's'}`}
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
