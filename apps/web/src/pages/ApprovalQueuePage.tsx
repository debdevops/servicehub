import { useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { CheckCircle2, AlertCircle, ArrowLeft, RefreshCw, Info, Inbox, ShieldAlert } from 'lucide-react';
import { useApprovalQueue, useSignatureTrustEvidenceBatch } from '@servicehub/ui-shared/hooks/useRecoveryLedger';
import { useReplayMessage } from '@servicehub/ui-shared/hooks/useMessages';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { ProviderBadge } from '@servicehub/ui-shared/lib/providerStyles';
import { describeApprovalQueueReason, describeApprovalQueueReasonWithEvidence } from '@servicehub/ui-shared/lib/api/recovery';
import { EnvironmentBadge } from '@/components/EnvironmentBadge';
import type { CloudProviderType } from '@servicehub/ui-shared/lib/api/types';
import type { ApprovalQueueEntry } from '@servicehub/ui-shared/lib/api/recovery';
import toast from 'react-hot-toast';

const KNOWN_PROVIDERS: readonly CloudProviderType[] = ['azure', 'aws', 'gcp'];

type ApprovalOutcome = 'accepted' | 'failed';

// Approvals are dispatched with a small concurrency cap rather than all at once — the queue can
// legitimately hold dozens of entries, and firing that many replay calls simultaneously would
// just move the bottleneck to the provider connection pool.
const APPROVE_CONCURRENCY = 3;

function CloudBadge({ provider }: { provider: string | null }) {
  if (!provider) return null;
  const normalized = provider.toLowerCase() as CloudProviderType;
  if (!KNOWN_PROVIDERS.includes(normalized)) return null;
  return <ProviderBadge provider={normalized} />;
}

async function runWithConcurrency<T>(
  items: T[],
  limit: number,
  worker: (item: T) => Promise<void>,
): Promise<{ succeeded: T[]; failed: T[] }> {
  const succeeded: T[] = [];
  const failed: T[] = [];
  let cursor = 0;

  async function next(): Promise<void> {
    while (cursor < items.length) {
      const item = items[cursor];
      cursor += 1;
      try {
        await worker(item);
        succeeded.push(item);
      } catch {
        failed.push(item);
      }
    }
  }

  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, next));
  return { succeeded, failed };
}

/**
 * `/approval-queue` — the Approval Queue (roadmap §11 item 1): auto-replay rule matches the
 * Eligibility Gate escalated for manual review. "Approve" is nothing but a call to the existing,
 * already-gated `POST /api/v1/messages/replay` endpoint per selected entry — this page adds no
 * new execution path, only a view over entries that already require exactly that human action.
 *
 * Roadmap W2.5 ("recovery proposal, then verification"): "Approve Selected" no longer replays on
 * the first click. It opens a proposal — scope, sample, the gate's reason evidence-enriched into
 * plain language, and the stop condition — and only the second, explicit confirmation executes.
 * After execution, entries do not just vanish behind a toast: they move into a visible "Just
 * approved" list naming the real, honest state (accepted for replay / failed) and pointing at the
 * Recovery Ledger, where the actual Recovered/Returned/Unverified verification will appear once
 * the observation window closes — never claimed here, since it hasn't happened yet.
 */
export default function ApprovalQueuePage() {
  const { isDemoMode } = useDemoContext();
  const [searchParams] = useSearchParams();
  const namespaceId = searchParams.get('namespace') || undefined;

  const { data: entries, isLoading, isError, refetch, isFetching } = useApprovalQueue(namespaceId);
  const replayMutation = useReplayMessage();

  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [reviewing, setReviewing] = useState(false);
  const [approving, setApproving] = useState(false);
  const [justApproved, setJustApproved] = useState<{ entry: ApprovalQueueEntry; outcome: ApprovalOutcome }[]>([]);

  const list = useMemo(() => entries ?? [], [entries]);
  const proposalTargets = useMemo(() => list.filter(e => selected.has(e.entryId)), [list, selected]);
  const evidenceByHash = useSignatureTrustEvidenceBatch(
    proposalTargets.map(e => e.signatureHash).filter((h): h is string => !!h),
  );

  function toggle(id: string) {
    setSelected(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function toggleAll() {
    setSelected(prev => (prev.size === list.length ? new Set() : new Set(list.map(e => e.entryId))));
  }

  // One plain-language sentence per distinct reason present in this proposal, evidence-enriched
  // where possible — duplicated reasons across rows collapse to a single bullet rather than
  // repeating the same sentence once per message.
  const proposalReasons = useMemo(() => {
    const seen = new Set<string>();
    const reasons: string[] = [];
    for (const entry of proposalTargets) {
      const evidence = entry.signatureHash ? evidenceByHash.get(entry.signatureHash) : undefined;
      const text = describeApprovalQueueReasonWithEvidence(entry.reasonCode, evidence);
      if (!seen.has(text)) {
        seen.add(text);
        reasons.push(text);
      }
    }
    return reasons;
  }, [proposalTargets, evidenceByHash]);

  async function confirmApprove() {
    const targets = proposalTargets;
    if (targets.length === 0) return;

    setApproving(true);
    try {
      const { succeeded, failed } = await runWithConcurrency(targets, APPROVE_CONCURRENCY, async entry => {
        await replayMutation.mutateAsync({
          namespaceId: entry.namespaceId,
          sequenceNumber: entry.sequenceNumber,
          entityName: entry.entityName,
          subscriptionName: entry.subscriptionName ?? undefined,
        });
      });

      setJustApproved([
        ...succeeded.map(entry => ({ entry, outcome: 'accepted' as const })),
        ...failed.map(entry => ({ entry, outcome: 'failed' as const })),
      ]);

      if (succeeded.length > 0) {
        toast.success(`${succeeded.length} of ${targets.length} message${targets.length === 1 ? '' : 's'} accepted for replay`);
      }
      if (failed.length > 0) {
        toast.error(`${failed.length} replay${failed.length === 1 ? '' : 's'} failed — see below for which`);
      }

      setSelected(new Set());
      setReviewing(false);
      await refetch();
    } finally {
      setApproving(false);
    }
  }

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      <div className="bg-white border-b border-gray-200 px-6 py-4 shrink-0">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-xl font-bold text-gray-900 flex items-center gap-2">
              <CheckCircle2 className="w-5 h-5 text-amber-600" />
              Approval Queue
            </h1>
            <p className="text-sm text-gray-500 mt-0.5">
              Auto-replay rule matches the Eligibility Gate escalated for manual review. Approving
              an entry replays it exactly as if you had replayed it by hand.
            </p>
          </div>
          <div className="flex items-center gap-2">
            <button
              onClick={() => { setJustApproved([]); setReviewing(true); }}
              disabled={selected.size === 0 || reviewing || isDemoMode}
              className="flex items-center gap-1.5 px-3 py-2 text-sm font-medium text-white bg-amber-600 rounded-lg hover:bg-amber-700 shadow-sm transition-all disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <CheckCircle2 className="w-4 h-4" />
              Review &amp; Approve ({selected.size})
            </button>
            <button
              onClick={() => refetch()}
              disabled={isFetching || isDemoMode}
              className="flex items-center gap-1.5 px-3 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-200 rounded-lg hover:bg-gray-50 shadow-sm transition-all disabled:opacity-50"
            >
              <RefreshCw className={`w-4 h-4 ${isFetching ? 'animate-spin' : ''}`} />
              Refresh
            </button>
          </div>
        </div>

        {isDemoMode && (
          <div className="mt-3 flex items-center gap-2 px-3 py-2 rounded-lg bg-amber-50 border border-amber-200 text-xs text-amber-800">
            <Info className="w-4 h-4 shrink-0" />
            Demo Mode — approving requires a live connection, so this queue is empty here.
          </div>
        )}

        {reviewing && (
          <div className="mt-3 border border-amber-300 rounded-lg bg-amber-50/60 p-4" role="region" aria-label="Approval proposal">
            <div className="flex items-center justify-between gap-3 mb-3">
              <h2 className="text-sm font-semibold text-gray-900">
                Proposal — replay {proposalTargets.length} message{proposalTargets.length === 1 ? '' : 's'}
              </h2>
              <button
                onClick={() => setReviewing(false)}
                disabled={approving}
                className="flex items-center gap-1 text-xs font-medium text-gray-500 hover:text-gray-700"
              >
                <ArrowLeft className="w-3.5 h-3.5" /> Back
              </button>
            </div>

            <div className="grid gap-3 text-xs text-gray-700 sm:grid-cols-2">
              <div>
                <p className="font-semibold text-gray-500 uppercase tracking-wide mb-1">Scope &amp; sample</p>
                <p>{proposalTargets.length} message{proposalTargets.length === 1 ? '' : 's'} across{' '}
                  {new Set(proposalTargets.map(e => e.ruleId)).size} rule(s), each replayed exactly as if
                  replayed by hand.</p>
              </div>
              <div>
                <p className="font-semibold text-gray-500 uppercase tracking-wide mb-1">Stop condition</p>
                <p>Approving does not grant future unattended trust for these signatures — the next match
                  will escalate for review again the same way. Anything left un-approved simply stays
                  Declined; nothing retries automatically.</p>
              </div>
            </div>

            {proposalReasons.length > 0 && (
              <div className="mt-3">
                <p className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-1 flex items-center gap-1">
                  <ShieldAlert className="w-3.5 h-3.5" /> Why the gate escalated this
                </p>
                <ul className="space-y-1">
                  {proposalReasons.map((reason, i) => (
                    <li key={i} className="text-xs text-gray-700 bg-white border border-gray-200 rounded-lg px-2.5 py-1.5">
                      {reason}
                    </li>
                  ))}
                </ul>
              </div>
            )}

            <div className="mt-4 flex justify-end gap-2">
              <button
                onClick={() => setReviewing(false)}
                disabled={approving}
                className="px-3 py-1.5 text-xs font-medium text-gray-600 hover:text-gray-800 rounded-lg"
              >
                Cancel
              </button>
              <button
                onClick={confirmApprove}
                disabled={approving}
                className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-white bg-amber-600 rounded-lg hover:bg-amber-700 disabled:opacity-50"
              >
                <CheckCircle2 className="w-3.5 h-3.5" />
                {approving ? 'Replaying…' : `Confirm & Replay ${proposalTargets.length}`}
              </button>
            </div>
          </div>
        )}

        {justApproved.length > 0 && (
          <div className="mt-3 border border-gray-200 rounded-lg bg-white p-4">
            <div className="flex items-center justify-between gap-3 mb-2">
              <h2 className="text-sm font-semibold text-gray-900">Just approved</h2>
              <Link to={`/recovery${namespaceId ? `?namespace=${namespaceId}` : ''}`} className="text-xs font-medium text-amber-700 hover:underline">
                View in Recovery Ledger
              </Link>
            </div>
            <p className="text-xs text-gray-500 mb-2">
              This is what actually happened, not confirmation that a message was sent — accepted
              means the provider took the replay, not that it stayed off the dead-letter queue. That
              Recovered / Returned / Unverified verification appears in the Recovery Ledger once the
              observation window closes.
            </p>
            <ul className="space-y-1">
              {justApproved.map(({ entry, outcome }) => (
                <li key={entry.entryId} className="flex items-center justify-between text-xs px-2.5 py-1.5 rounded-lg bg-gray-50">
                  <span className="font-mono text-gray-600 truncate">
                    {entry.subscriptionName ? `${entry.entityName}/${entry.subscriptionName}` : entry.entityName} · seq #{entry.sequenceNumber}
                  </span>
                  <span className={outcome === 'accepted' ? 'text-green-700 font-medium' : 'text-red-600 font-medium'}>
                    {outcome === 'accepted' ? 'Accepted for replay' : 'Failed'}
                  </span>
                </li>
              ))}
            </ul>
          </div>
        )}
      </div>

      <div className="flex-1 overflow-auto">
        {isLoading ? (
          <div className="flex items-center justify-center h-64">
            <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-amber-600" />
          </div>
        ) : isError ? (
          <div className="flex items-center justify-center h-64">
            <div className="text-center">
              <AlertCircle className="w-10 h-10 text-red-400 mx-auto mb-3" />
              <p className="text-gray-600 font-medium">Failed to load the approval queue</p>
              <button onClick={() => refetch()} className="mt-3 px-4 py-2 text-sm text-amber-600 hover:text-amber-700 border border-amber-300 rounded-lg hover:bg-amber-50">
                Try Again
              </button>
            </div>
          </div>
        ) : list.length === 0 ? (
          <div className="flex items-center justify-center h-64">
            <div className="text-center">
              <Inbox className="w-10 h-10 text-gray-300 mx-auto mb-3" />
              <p className="text-gray-500 font-medium">Nothing waiting on approval</p>
              <p className="text-sm text-gray-500 mt-1">
                Rule matches the Eligibility Gate escalates for manual review will appear here.
              </p>
            </div>
          </div>
        ) : (
          <table className="w-full text-sm" aria-label="Approval queue">
            <thead className="bg-gray-50 border-b border-gray-200 sticky top-0 z-10">
              <tr>
                <th scope="col" className="px-4 py-3 w-10">
                  <input
                    type="checkbox"
                    aria-label="Select all"
                    checked={list.length > 0 && selected.size === list.length}
                    onChange={toggleAll}
                    className="rounded border-gray-300 text-amber-600 focus:ring-amber-500"
                  />
                </th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500 w-40">Escalated</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500">Rule</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500">Entity</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500">Cloud / Env</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500">Reason</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {list.map((entry: ApprovalQueueEntry) => (
                <tr key={entry.entryId} className="hover:bg-amber-50/40 transition-colors">
                  <td className="px-4 py-3">
                    <input
                      type="checkbox"
                      aria-label={`Select entry for ${entry.entityName}`}
                      checked={selected.has(entry.entryId)}
                      onChange={() => toggle(entry.entryId)}
                      className="rounded border-gray-300 text-amber-600 focus:ring-amber-500"
                    />
                  </td>
                  <td className="px-4 py-3 text-gray-500 whitespace-nowrap font-mono text-xs">
                    {new Date(entry.declinedAt).toLocaleString(undefined, {
                      month: 'short', day: '2-digit', hour: '2-digit', minute: '2-digit',
                    })}
                  </td>
                  <td className="px-4 py-3 text-gray-800 font-medium text-xs">{entry.ruleName}</td>
                  <td className="px-4 py-3 text-gray-600 text-xs font-mono truncate max-w-[240px]">
                    {entry.subscriptionName ? `${entry.entityName}/${entry.subscriptionName}` : entry.entityName}
                    <div className="text-gray-400">seq #{entry.sequenceNumber}</div>
                  </td>
                  <td className="px-4 py-3">
                    <div className="flex items-center gap-1 flex-wrap">
                      <CloudBadge provider={entry.provider} />
                      <EnvironmentBadge env={entry.environment} />
                      {entry.namespaceName && (
                        <span className="text-xs text-gray-500 bg-gray-100 px-1.5 py-0.5 rounded">{entry.namespaceName}</span>
                      )}
                    </div>
                  </td>
                  <td className="px-4 py-3 text-gray-600 text-xs max-w-[320px]">
                    {describeApprovalQueueReason(entry.reasonCode)}
                    {entry.matchedCount != null && (
                      <span className="text-gray-400"> ({entry.matchedCount} prior matches)</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
