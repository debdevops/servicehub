import { useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { CheckCircle2, AlertCircle, RefreshCw, Info, Inbox } from 'lucide-react';
import { useApprovalQueue } from '@servicehub/ui-shared/hooks/useRecoveryLedger';
import { useReplayMessage } from '@servicehub/ui-shared/hooks/useMessages';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { ProviderBadge } from '@servicehub/ui-shared/lib/providerStyles';
import { describeApprovalQueueReason } from '@servicehub/ui-shared/lib/api/recovery';
import { EnvironmentBadge } from '@/components/EnvironmentBadge';
import type { CloudProviderType } from '@servicehub/ui-shared/lib/api/types';
import type { ApprovalQueueEntry } from '@servicehub/ui-shared/lib/api/recovery';
import toast from 'react-hot-toast';

const KNOWN_PROVIDERS: readonly CloudProviderType[] = ['azure', 'aws', 'gcp'];

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
 * Eligibility Gate escalated for manual review, surfaced as a one-click-bulk-approve panel.
 * "Approve" is nothing but a call to the existing, already-gated `POST /api/v1/messages/replay`
 * endpoint per selected entry — this page adds no new execution path, only a view over entries
 * that already require exactly that human action, and a convenient way to fire it in bulk.
 */
export default function ApprovalQueuePage() {
  const { isDemoMode } = useDemoContext();
  const [searchParams] = useSearchParams();
  const namespaceId = searchParams.get('namespace') || undefined;

  const { data: entries, isLoading, isError, refetch, isFetching } = useApprovalQueue(namespaceId);
  const replayMutation = useReplayMessage();

  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [approving, setApproving] = useState(false);

  const list = useMemo(() => entries ?? [], [entries]);

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

  async function approveSelected() {
    const targets = list.filter(e => selected.has(e.entryId));
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

      if (succeeded.length > 0) {
        toast.success(`Approved ${succeeded.length} of ${targets.length} message${targets.length === 1 ? '' : 's'}`);
      }
      if (failed.length > 0) {
        toast.error(`${failed.length} approval${failed.length === 1 ? '' : 's'} failed — see the toasts above for detail`);
      }

      setSelected(new Set());
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
              onClick={approveSelected}
              disabled={selected.size === 0 || approving || isDemoMode}
              className="flex items-center gap-1.5 px-3 py-2 text-sm font-medium text-white bg-amber-600 rounded-lg hover:bg-amber-700 shadow-sm transition-all disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <CheckCircle2 className="w-4 h-4" />
              {approving ? 'Approving…' : `Approve Selected (${selected.size})`}
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
