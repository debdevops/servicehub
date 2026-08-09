import { Link } from 'react-router-dom';
import { Lightbulb } from 'lucide-react';
import { useAuditLogs } from '@servicehub/ui-shared/hooks/useAudit';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { getMockRecentChanges } from '@servicehub/ui-shared/lib/demo/mockProviders';
import { computeRecentChangesVerdict } from './recentChangesVerdict';

interface RecentChangesPanelProps {
  namespaceId: string;
  signatureHash: string;
  firstSeenAt: string;
}

const WINDOW_MS = 24 * 60 * 60 * 1000;

function formatWindowBoundary(ts: string): string {
  const formatted = new Date(ts).toLocaleString('en-US', {
    timeZone: 'UTC',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  });
  return `${formatted} UTC`;
}

function formatTimestamp(ts: string): string {
  return new Date(ts).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

/**
 * Recent Changes Before Failure (Epic 4) — namespace-wide audit entries in a fixed 24h window
 * immediately before this signature's firstSeenAt. A plain time filter over already-durable
 * audit data, not a correlation engine: the window renders as literal text so it never reads as
 * an exhaustive or intelligently-scoped list, and the recommendation below only ever states the
 * count of recorded changes — it never infers root cause or ranks likelihood.
 */
export function RecentChangesPanel({ namespaceId, signatureHash, firstSeenAt }: RecentChangesPanelProps) {
  const { isDemoMode } = useDemoContext();
  const to = firstSeenAt;
  const from = new Date(new Date(firstSeenAt).getTime() - WINDOW_MS).toISOString();

  const { data, isLoading, error } = useAuditLogs({ namespaceId, from, to });
  const demoChanges = isDemoMode ? getMockRecentChanges(signatureHash) : undefined;

  const changes = isDemoMode ? (demoChanges?.items ?? []) : (data?.items ?? []);
  const err = error as { response?: { status?: number } } | null;
  const is403 = !isDemoMode && err?.response?.status === 403;

  return (
    <div className="bg-white border border-gray-200 rounded-xl p-5">
      <h2 className="text-sm font-semibold text-gray-700 uppercase tracking-wider mb-1">Recent Changes Before Failure</h2>
      <p className="text-xs text-gray-500 mb-3">
        Showing changes in the 24h before this failure started ({formatWindowBoundary(from)} – {formatWindowBoundary(to)})
      </p>

      {is403 ? (
        <p className="text-sm text-gray-500">Audit access required to view recent changes.</p>
      ) : !isDemoMode && isLoading ? (
        <p className="text-sm text-gray-500">Loading recent changes…</p>
      ) : (
        <>
          {changes.length > 0 && (
            <div className="divide-y divide-gray-100 mb-3">
              {changes.map((change) => (
                <div key={change.id} className="py-2 flex items-center justify-between gap-3 flex-wrap">
                  <div className="min-w-0">
                    <p className="text-sm text-gray-900">
                      <span className="font-medium">{change.action}</span>
                      {change.resourceName && <> · {change.resourceName}</>}
                    </p>
                    <p className="text-xs text-gray-500">{change.userIdentity}</p>
                  </div>
                  <span className="text-xs text-gray-400 shrink-0">{formatTimestamp(change.timestamp)}</span>
                </div>
              ))}
            </div>
          )}

          <div className="flex items-center gap-2 text-sm text-sky-700 bg-sky-50 border border-sky-100 rounded-lg px-3 py-2 flex-wrap">
            <Lightbulb className="w-4 h-4 shrink-0" />
            <span className="flex-1">{computeRecentChangesVerdict(changes)}</span>
            <Link
              to={`/audit?namespace=${namespaceId}`}
              className="text-xs font-medium text-sky-700 hover:text-sky-800 underline shrink-0"
            >
              View full audit trail →
            </Link>
          </div>
        </>
      )}
    </div>
  );
}
