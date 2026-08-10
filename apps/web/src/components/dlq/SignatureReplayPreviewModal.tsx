import { useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import { AlertTriangle, RefreshCw, ShieldAlert, X } from 'lucide-react';
import { useSignatureReplayPreview, useStartSignatureReplay } from '@servicehub/ui-shared/hooks/useSignatureReplay';
import type { SignatureReplayScope } from '@servicehub/ui-shared/lib/api/signatureReplay';
import { useFocusTrap } from '@servicehub/ui-shared/hooks/useFocusTrap';

interface SignatureReplayPreviewModalProps {
  namespaceId: string;
  signatureHash: string;
  onClose: () => void;
  onJobStarted: (jobId: string) => void;
}

const SCOPE_OPTIONS: { value: SignatureReplayScope; label: string }[] = [
  { value: 'all', label: 'All messages' },
  { value: 'dateRange', label: 'Messages within a date range' },
  { value: 'unresolved', label: 'Only unresolved messages' },
  { value: 'failedReplay', label: 'Only failed replay attempts' },
];

/**
 * Dry-run + confirmation for "replay every message in this failure signature" — same
 * preview-then-confirm convention as BulkOperationPreviewModal, scoped to a signature's current
 * member messages instead of a namespace-wide filter.
 */
export function SignatureReplayPreviewModal({
  namespaceId,
  signatureHash,
  onClose,
  onJobStarted,
}: SignatureReplayPreviewModalProps) {
  const [scope, setScope] = useState<SignatureReplayScope>('all');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');

  const preview = useSignatureReplayPreview();
  const startReplay = useStartSignatureReplay();

  const filter = { scope, from: from || undefined, to: to || undefined };
  const isDateRangeIncomplete = scope === 'dateRange' && !from && !to;

  useEffect(() => {
    if (isDateRangeIncomplete) return;
    preview.mutate({ namespaceId, signatureHash, filter });
    // Only re-run when the scope identity actually changes — mutate() is stable.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [namespaceId, signatureHash, scope, from, to, isDateRangeIncomplete]);

  const data = preview.data;
  const isBusy = startReplay.isPending;

  const handleConfirm = async () => {
    const job = await startReplay.mutateAsync({ namespaceId, signatureHash, filter });
    onJobStarted(job.id);
  };

  useEffect(() => {
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !isBusy) {
        onClose();
      }
    };
    window.addEventListener('keydown', handleEscape);
    return () => window.removeEventListener('keydown', handleEscape);
  }, [isBusy, onClose]);

  // This component is mounted only while the dialog is open, so the trap is always armed.
  const dialogRef = useFocusTrap<HTMLDivElement>(true);

  return createPortal(
    <div
      className="fixed inset-0 z-50 flex items-center justify-center"
      ref={dialogRef}
      role="dialog"
      aria-modal="true"
      aria-label="Replay signature preview"
    >
      <div
        className="absolute inset-0 bg-black/40"
        onClick={() => !isBusy && onClose()}
        aria-hidden="true"
      />
      <div className="relative bg-white rounded-2xl shadow-2xl w-full max-w-lg mx-4 p-6 max-h-[85vh] overflow-y-auto">
        <div className="flex items-center justify-between mb-4">
          <div className="flex items-center gap-2">
            <RefreshCw className="w-5 h-5 text-sky-600" />
            <h2 className="text-base font-semibold text-gray-900">Replay Signature</h2>
          </div>
          <button
            onClick={onClose}
            className="p-1 text-gray-400 hover:text-gray-600 rounded-lg"
            aria-label="Close preview"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        <div className="mb-4">
          <label className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2 block">Scope</label>
          <div className="space-y-1.5">
            {SCOPE_OPTIONS.map((option) => (
              <label
                key={option.value}
                className="flex items-center gap-2 text-sm text-gray-700 cursor-pointer"
              >
                <input
                  type="radio"
                  name="signature-replay-scope"
                  value={option.value}
                  checked={scope === option.value}
                  onChange={() => setScope(option.value)}
                  className="text-sky-600 focus:ring-sky-500"
                />
                {option.label}
              </label>
            ))}
          </div>
          {scope === 'dateRange' && (
            <div className="flex items-center gap-2 mt-2">
              <input
                type="date"
                value={from}
                onChange={(e) => setFrom(e.target.value)}
                className="flex-1 px-2 py-1.5 text-sm border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-sky-500"
                aria-label="From date"
              />
              <span className="text-gray-400 text-sm">to</span>
              <input
                type="date"
                value={to}
                onChange={(e) => setTo(e.target.value)}
                className="flex-1 px-2 py-1.5 text-sm border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-sky-500"
                aria-label="To date"
              />
            </div>
          )}
        </div>

        {isDateRangeIncomplete ? (
          <div className="py-10 text-center text-sm text-gray-500">Pick at least one date to preview.</div>
        ) : preview.isPending ? (
          <div className="py-10 text-center text-sm text-gray-500">Matching messages…</div>
        ) : preview.isError ? (
          <div className="py-10 text-center text-sm text-red-600">
            Failed to preview this replay. Close and try again.
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
            disabled={isBusy}
            className="px-4 py-2 text-sm font-medium text-gray-600 hover:text-gray-800 hover:bg-gray-50 rounded-lg transition-colors focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-1"
          >
            Cancel
          </button>
          <button
            onClick={handleConfirm}
            disabled={!data?.canExecute || isBusy}
            className="flex items-center gap-1.5 px-4 py-2 text-sm font-medium rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed bg-sky-600 hover:bg-sky-700 text-white"
          >
            <RefreshCw className="w-3.5 h-3.5" />
            {isBusy ? 'Starting…' : `Replay ${data?.totalMatched ?? 0} message${data?.totalMatched === 1 ? '' : 's'}`}
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
