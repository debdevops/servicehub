import { AlertCircle, MapPin } from 'lucide-react';
import type { Namespace } from '@servicehub/ui-shared/lib/api/types';
import { useRootCauseMatches } from '@servicehub/ui-shared/hooks/useDlqSignatures';
import { ProviderBadge } from '@servicehub/ui-shared/lib/providerStyles';
import { StatusBadge } from './StatusBadge';

interface RootCauseExplorerPanelProps {
  namespaceId: string;
  signatureHash: string;
  dominantDeadletterReason: string;
  namespaces?: Namespace[];
}

function formatDate(ts: string): string {
  return new Date(ts).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
}

/**
 * Root Cause Explorer (Epic 3) — answers "has this happened before anywhere in my fleet?" by
 * exact signature-hash equality only (no scoring, no fuzzy matching): every match below shares
 * this signature's dominant deadletter reason and top terms by construction, since the hash is
 * derived from those alone. Namespace names are resolved from the already-fetched namespace
 * list; a match in a namespace the caller can't currently list (e.g. a scoped API key) falls
 * back to a short identifier rather than being hidden.
 */
export function RootCauseExplorerPanel({
  namespaceId,
  signatureHash,
  dominantDeadletterReason,
  namespaces,
}: RootCauseExplorerPanelProps) {
  const { data, isLoading } = useRootCauseMatches(namespaceId, signatureHash);
  const matches = data?.matches ?? [];

  return (
    <div className="bg-white border border-gray-200 rounded-xl p-5">
      <h2 className="text-sm font-semibold text-gray-700 uppercase tracking-wider mb-3">Root Cause Explorer</h2>

      {isLoading ? (
        <p className="text-sm text-gray-500">Searching the fleet…</p>
      ) : matches.length === 0 ? (
        <p className="text-sm text-gray-500">Not seen in any other namespace in your fleet.</p>
      ) : (
        <>
          <p className="text-sm text-gray-600 bg-gray-50 border border-gray-200 rounded-lg p-3 mb-3">
            Every match below shares this signature's fingerprint exactly — the same dominant reason (
            <span className="font-medium">{dominantDeadletterReason}</span>) and distinguishing terms — since the
            fingerprint is derived from those alone, not a similarity score.
          </p>
          <p className="text-sm text-gray-700 mb-3">
            Seen <span className="font-semibold">{data!.totalOccurrencesAcrossFleet}</span> time(s) across{' '}
            {matches.length + 1} namespace(s) in your fleet.
          </p>
          <div className="divide-y divide-gray-100">
            {matches.map((match) => {
              const ns = namespaces?.find((n) => n.id === match.namespaceId);
              const label = ns?.displayName || ns?.name || `Namespace ${match.namespaceId.slice(0, 8)}`;

              return (
                <div key={match.namespaceId} className="py-3 first:pt-0 last:pb-0">
                  <div className="flex items-center justify-between gap-2 flex-wrap mb-1">
                    <div className="flex items-center gap-2 min-w-0">
                      <MapPin className="w-3.5 h-3.5 text-gray-400 shrink-0" />
                      <span className="text-sm font-medium text-gray-900 truncate">{label}</span>
                      {ns?.cloudProvider && <ProviderBadge provider={ns.cloudProvider} />}
                    </div>
                    <StatusBadge status={match.lifecycleStatus} />
                  </div>
                  <p className="text-xs text-gray-500 mb-1.5">
                    {match.occurrenceCount} occurrence{match.occurrenceCount === 1 ? '' : 's'} · first seen{' '}
                    {formatDate(match.firstSeenAt)} · last seen {formatDate(match.lastSeenAt)}
                  </p>
                  {match.knowledge?.rootCause ? (
                    <div className="text-sm bg-sky-50 border border-sky-100 rounded-lg p-2.5">
                      <p className="text-gray-900 mb-1">{match.knowledge.rootCause}</p>
                      {match.knowledge.resolutionNotes && (
                        <p className="text-gray-600 text-xs">Resolved: {match.knowledge.resolutionNotes}</p>
                      )}
                      {match.knowledge.owner && (
                        <p className="text-gray-500 text-xs mt-1">Owner: {match.knowledge.owner}</p>
                      )}
                    </div>
                  ) : (
                    <p className="text-xs text-gray-400 flex items-center gap-1">
                      <AlertCircle className="w-3 h-3" />
                      No root cause recorded there yet.
                    </p>
                  )}
                </div>
              );
            })}
          </div>
        </>
      )}
    </div>
  );
}
