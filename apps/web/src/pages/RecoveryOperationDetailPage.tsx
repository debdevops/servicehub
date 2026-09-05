import { useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import { ArrowLeft, ShieldCheck, Download, ShieldQuestion, AlertCircle, Info, X, Hash, FlaskConical } from 'lucide-react';
import { useRecoveryOperation } from '@servicehub/ui-shared/hooks/useRecoveryOperation';
import { useDownloadRecoveryExport, useWriteOffRecoveryEntry, useRehearseRecoveryEntry } from '@servicehub/ui-shared/hooks/useRecoveryOperation';
import { useVerifyChain } from '@servicehub/ui-shared/hooks/useChainVerification';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import {
  describeRecoveryDetailReason,
  describeApprovalQueueReason,
  type RecoveryLedgerEntry,
  type RecoveryRehearsal,
} from '@servicehub/ui-shared/lib/api/recovery';
import { VerificationResultNote } from '@/components/recovery/VerificationResultNote';
import { useFocusTrap } from '@servicehub/ui-shared/hooks/useFocusTrap';

const NON_TERMINAL: RecoveryLedgerEntry['state'][] = ['Executing', 'Observing', 'ExecutionUnknown'];

function WriteOffModal({ entry, onClose }: { entry: RecoveryLedgerEntry; onClose: () => void }) {
  const [reason, setReason] = useState('');
  const writeOff = useWriteOffRecoveryEntry();
  const dialogRef = useFocusTrap<HTMLDivElement>(true);

  return (
    <>
      <div className="fixed inset-0 bg-black/30 z-40" onClick={onClose} aria-hidden="true" />
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="write-off-title"
        className="fixed inset-0 z-50 flex items-center justify-center p-4"
      >
        <div className="bg-white rounded-xl shadow-2xl w-full max-w-md p-5">
          <div className="flex items-center justify-between mb-3">
            <h2 id="write-off-title" className="font-semibold text-gray-900">Write off entry</h2>
            <button onClick={onClose} aria-label="Close" className="p-1 rounded hover:bg-gray-100">
              <X className="w-4 h-4 text-gray-500" />
            </button>
          </div>
          <p className="text-sm text-gray-600 mb-3">
            Declares this entry unrecoverable without ServiceHub having observed an outcome. A
            reason is required and becomes part of the permanent evidence record.
          </p>
          <textarea
            value={reason}
            onChange={e => setReason(e.target.value)}
            rows={3}
            placeholder="Why is this entry being written off?"
            className="w-full px-3 py-2 text-sm rounded-lg border border-gray-200 focus:outline-none focus:ring-2 focus:ring-teal-400 resize-none"
          />
          <div className="flex justify-end gap-2 mt-4">
            <button onClick={onClose} className="px-3 py-1.5 text-sm text-gray-600 hover:bg-gray-100 rounded-lg">Cancel</button>
            <button
              onClick={() => writeOff.mutate({ entryId: entry.id, reason }, { onSuccess: onClose })}
              disabled={!reason.trim() || writeOff.isPending}
              className="px-3 py-1.5 text-sm font-medium text-white bg-teal-600 hover:bg-teal-700 disabled:bg-gray-300 rounded-lg"
            >
              {writeOff.isPending ? 'Writing off…' : 'Write off'}
            </button>
          </div>
        </div>
      </div>
    </>
  );
}

const VERDICT_STYLES: Record<RecoveryRehearsal['verdict'], { label: string; className: string }> = {
  Allow: { label: 'Would allow', className: 'bg-green-50 text-green-800 border-green-200' },
  Escalate: { label: 'Would escalate to a human', className: 'bg-amber-50 text-amber-800 border-amber-200' },
  Deny: { label: 'Would deny', className: 'bg-red-50 text-red-800 border-red-200' },
};

/**
 * The result of one rehearsal (roadmap W1.2), rendered inline under the entry it was run against.
 * States plainly that nothing happened — a rehearsal that looked like an execution receipt would
 * be worse than no rehearsal at all.
 */
function RehearsalResult({ rehearsal }: { rehearsal: RecoveryRehearsal }) {
  const style = VERDICT_STYLES[rehearsal.verdict];
  const reason = describeApprovalQueueReason(rehearsal.reasonCode);

  return (
    <div className={`mt-2 rounded-lg border px-3 py-2 text-xs ${style.className}`}>
      <div className="font-semibold">
        {style.label} — evaluated as {rehearsal.actorKindEvaluated}
      </div>
      {reason && <p className="mt-1">{reason}</p>}
      {rehearsal.matchedCount > 0 && (
        <p className="mt-1">
          {rehearsal.matchedCount} prior attempt{rehearsal.matchedCount === 1 ? '' : 's'} on this
          message's lineage were matched by the recurrence cap.
        </p>
      )}
      <p className="mt-1.5 opacity-80">
        Nothing was executed and nothing was written to either ledger — rehearsal runs the gate
        against this entry's recorded identity and reports the verdict.{' '}
        <span className="whitespace-nowrap">Evaluated {new Date(rehearsal.evaluatedAt).toLocaleString()}.</span>
      </p>
    </div>
  );
}

/**
 * `/recovery/:operationId` — one operation's full evidence: header, per-entry table with
 * verification results, the complete hash-chained event log, chain verification, evidence export,
 * and per-entry write-off.
 */
export default function RecoveryOperationDetailPage() {
  const { operationId } = useParams<{ operationId: string }>();
  const { isDemoMode, cloudProvider } = useDemoContext();
  const navPrefix = isDemoMode && cloudProvider ? `/demo/${cloudProvider}` : '';
  const { data, isLoading, isError } = useRecoveryOperation(operationId ?? null);
  const verifyChain = useVerifyChain();
  const downloadExport = useDownloadRecoveryExport();
  const [writeOffEntry, setWriteOffEntry] = useState<RecoveryLedgerEntry | null>(null);
  const [showEvents, setShowEvents] = useState(false);
  const rehearse = useRehearseRecoveryEntry();
  const [rehearsals, setRehearsals] = useState<Record<string, RecoveryRehearsal>>({});

  if (isLoading) {
    return (
      <div className="flex-1 flex items-center justify-center">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-teal-600" />
      </div>
    );
  }

  if (isError || !data) {
    return (
      <div className="flex-1 flex items-center justify-center">
        <div className="text-center">
          <AlertCircle className="w-10 h-10 text-red-400 mx-auto mb-3" />
          <p className="text-gray-600 font-medium">Recovery operation not found</p>
          <Link to={`${navPrefix}/recovery`} className="mt-3 inline-block text-sm text-teal-600 hover:underline">Back to ledger</Link>
        </div>
      </div>
    );
  }

  const { operation, entries, events } = data;
  const counts = entries.reduce<Record<string, number>>((acc, e) => {
    acc[e.state] = (acc[e.state] ?? 0) + 1;
    return acc;
  }, {});

  // The reason an entry closed as Unverified (etc.) is recorded on the event that closed it, not
  // on the entry itself — find it once per entry so it renders immediately, not only inside the
  // event-chain table below (which is collapsed by default).
  const reasonByEntryId = new Map<string, string>();
  for (const evt of events) {
    if (!evt.entryId) continue;
    const reason = describeRecoveryDetailReason(evt.detailJson);
    if (reason) reasonByEntryId.set(evt.entryId, reason);
  }

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      <div className="bg-white border-b border-gray-200 px-6 py-4 shrink-0">
        <Link to={`${navPrefix}/recovery`} className="inline-flex items-center gap-1 text-xs text-gray-500 hover:text-teal-700 mb-2">
          <ArrowLeft className="w-3.5 h-3.5" /> Back to ledger
        </Link>
        <div className="flex items-start justify-between flex-wrap gap-3">
          <div>
            <h1 className="text-xl font-bold text-gray-900 flex items-center gap-2">
              <ShieldCheck className="w-5 h-5 text-teal-600" />
              {operation.kind} — {operation.scopeDescription}
            </h1>
            <p className="text-sm text-gray-500 mt-0.5">
              Opened {new Date(operation.openedAt).toLocaleString()} by <span className="font-medium">{operation.actorIdentity}</span> ({operation.trigger})
            </p>
            {operation.reason && <p className="text-sm text-gray-600 mt-1 italic">"{operation.reason}"</p>}
          </div>
          <div className="flex items-center gap-2">
            <button
              onClick={() => operationId && verifyChain.mutate(operationId)}
              disabled={verifyChain.isPending}
              className="flex items-center gap-1.5 px-3 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-200 rounded-lg hover:bg-gray-50 shadow-sm disabled:opacity-50"
            >
              <ShieldQuestion className="w-4 h-4" />
              Verify chain
            </button>
            <div className="relative group">
              <button
                onClick={() => operationId && downloadExport.mutate({ operationId, format: 'json' })}
                disabled={downloadExport.isPending}
                className="flex items-center gap-1.5 px-3 py-2 text-sm font-medium text-white bg-teal-600 hover:bg-teal-700 rounded-lg shadow-sm disabled:opacity-50"
              >
                <Download className="w-4 h-4" />
                Export evidence
              </button>
            </div>
          </div>
        </div>

        {isDemoMode && (
          <div className="mt-3 flex items-center gap-2 px-3 py-2 rounded-lg bg-amber-50 border border-amber-200 text-xs text-amber-800">
            <Info className="w-4 h-4 shrink-0" />
            Demo Mode — this operation and every exported artefact from it is fixture data, watermarked <code>demoMode: true</code>.
          </div>
        )}

        <div className="mt-3 flex items-center gap-2 flex-wrap text-xs">
          {Object.entries(counts).map(([state, count]) => (
            <span key={state} className="px-2 py-1 rounded-lg bg-gray-100 text-gray-700 font-medium">
              {count} {state}
            </span>
          ))}
          <span className="px-2 py-1 rounded-lg bg-gray-50 text-gray-400">
            v{operation.serviceVersion} · {operation.namespaceNameSnapshot ?? 'cross-namespace'}
          </span>
        </div>
      </div>

      <div className="flex-1 overflow-auto p-6 space-y-6">
        {/* Entries table */}
        <section>
          <h2 className="text-sm font-semibold text-gray-700 uppercase tracking-wide mb-2">Entries ({entries.length})</h2>
          <div className="border border-gray-200 rounded-xl overflow-hidden">
            <table className="w-full text-sm">
              <thead className="bg-gray-50 border-b border-gray-200">
                <tr>
                  <th scope="col" className="px-3 py-2 text-left text-xs font-semibold text-gray-500">Target</th>
                  <th scope="col" className="px-3 py-2 text-left text-xs font-semibold text-gray-500">Body Hash</th>
                  <th scope="col" className="px-3 py-2 text-left text-xs font-semibold text-gray-500">Result</th>
                  <th scope="col" className="px-3 py-2 text-left text-xs font-semibold text-gray-500 w-28">Action</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {entries.map(entry => (
                  <tr key={entry.id}>
                    <td className="px-3 py-2 text-gray-700 font-mono text-xs">{entry.targetEntity}</td>
                    <td className="px-3 py-2 text-gray-400 font-mono text-xs truncate max-w-[160px]">{entry.bodyHash}</td>
                    <td className="px-3 py-2">
                      <VerificationResultNote entry={entry} reasonText={reasonByEntryId.get(entry.id) ?? null} />
                      {rehearsals[entry.id] && <RehearsalResult rehearsal={rehearsals[entry.id]} />}
                    </td>
                    <td className="px-3 py-2 align-top">
                      <div className="flex flex-col items-start gap-1">
                        <button
                          onClick={() => rehearse.mutate(
                            { entryId: entry.id },
                            { onSuccess: r => setRehearsals(prev => ({ ...prev, [entry.id]: r })) },
                          )}
                          disabled={rehearse.isPending}
                          className="text-xs text-gray-500 hover:text-teal-700 underline inline-flex items-center gap-1 disabled:opacity-50"
                          title="Ask the Eligibility Gate what it would decide about this entry right now. Reads only — it cannot reach a broker."
                        >
                          <FlaskConical className="w-3 h-3" />
                          Rehearse
                        </button>
                        {NON_TERMINAL.includes(entry.state) && (
                          <button
                            onClick={() => setWriteOffEntry(entry)}
                            className="text-xs text-gray-500 hover:text-red-600 underline"
                          >
                            Write off
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>

        {/* Event chain */}
        <section>
          <button
            onClick={() => setShowEvents(v => !v)}
            className="flex items-center gap-1.5 text-sm font-semibold text-gray-700 uppercase tracking-wide mb-2"
          >
            <Hash className="w-4 h-4 text-teal-600" />
            Event chain ({events.length}) {showEvents ? '▾' : '▸'}
          </button>
          {showEvents && (
            <div className="border border-gray-200 rounded-xl overflow-hidden overflow-x-auto">
              <table className="w-full text-xs">
                <thead className="bg-gray-50 border-b border-gray-200">
                  <tr>
                    <th scope="col" className="px-3 py-2 text-left font-semibold text-gray-500">Seq</th>
                    <th scope="col" className="px-3 py-2 text-left font-semibold text-gray-500">Event</th>
                    <th scope="col" className="px-3 py-2 text-left font-semibold text-gray-500">Occurred</th>
                    <th scope="col" className="px-3 py-2 text-left font-semibold text-gray-500">Actor</th>
                    <th scope="col" className="px-3 py-2 text-left font-semibold text-gray-500">Detail</th>
                    <th scope="col" className="px-3 py-2 text-left font-semibold text-gray-500">Hash</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {events.map(evt => (
                    <tr key={evt.id}>
                      <td className="px-3 py-2 font-mono text-gray-500">{evt.seq}</td>
                      <td className="px-3 py-2 font-medium text-gray-800">{evt.eventType}</td>
                      <td className="px-3 py-2 text-gray-500 whitespace-nowrap">{new Date(evt.occurredAt).toLocaleString()}</td>
                      <td className="px-3 py-2 text-gray-600">{evt.actorIdentity}</td>
                      <td className="px-3 py-2 text-gray-600 max-w-[220px]">{describeRecoveryDetailReason(evt.detailJson) ?? '—'}</td>
                      <td className="px-3 py-2 font-mono text-gray-400 truncate max-w-[140px]" title={evt.entryHash}>{evt.entryHash}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      </div>

      {writeOffEntry && <WriteOffModal entry={writeOffEntry} onClose={() => setWriteOffEntry(null)} />}
    </div>
  );
}
