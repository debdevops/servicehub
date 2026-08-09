import { Sparkles } from 'lucide-react';
import { Link } from 'react-router-dom';
import { useDlqSignatures } from '@servicehub/ui-shared/hooks/useDlqSignatures';
import type { DlqClusterSignature } from '@servicehub/ui-shared/lib/api/dlqSignatures';
import { FailureInvestigationPanel } from './FailureInvestigationPanel';

interface DlqSignaturesPanelProps {
  namespaceId?: string;
  onFilterEntity?: (entityName: string) => void;
}

function ClusterCard({
  cluster,
  namespaceId,
  onFilterEntity,
}: {
  cluster: DlqClusterSignature;
  namespaceId?: string;
  onFilterEntity?: (entityName: string) => void;
}) {
  return (
    <div className="space-y-3">
      <div className="bg-white border border-primary-200 rounded-xl p-4">
        <div className="flex items-start justify-between gap-2 mb-2">
          <div className="flex items-center gap-2 flex-wrap">
            <span
              className={`text-xs px-2 py-0.5 rounded-full font-semibold ${
                cluster.isNew ? 'bg-amber-100 text-amber-700' : 'bg-primary-100 text-primary-700'
              }`}
            >
              {cluster.isNew ? '🆕 New' : `🔁 Recurring ×${cluster.occurrenceCount}`}
            </span>
            <span className="text-xs text-gray-500">
              {cluster.size} message{cluster.size === 1 ? '' : 's'} · {cluster.dominantEntity}
            </span>
          </div>
          <span className="text-xs px-2 py-0.5 bg-gray-100 text-gray-600 rounded-full font-medium shrink-0">
            {cluster.dominantDeadletterReason}
          </span>
        </div>

        <p className="text-sm text-gray-700 mb-2">{cluster.explanation}</p>

        {cluster.topTerms.length > 0 && (
          <div className="flex items-center gap-1.5 flex-wrap mb-2">
            {cluster.topTerms.map((term) => (
              <span key={term} className="text-xs px-2 py-0.5 bg-gray-50 border border-gray-200 rounded text-gray-600">
                {term}
              </span>
            ))}
          </div>
        )}

        <div className="flex items-center gap-3">
          {onFilterEntity && (
            <button
              onClick={() => onFilterEntity(cluster.dominantEntity)}
              className="text-xs text-primary-600 hover:text-primary-700 font-medium"
            >
              Filter table to {cluster.dominantEntity} →
            </button>
          )}
          {namespaceId && (
            <Link
              to={`/signatures/${cluster.signatureHash}?namespace=${namespaceId}`}
              className="text-xs text-primary-600 hover:text-primary-700 font-medium"
            >
              View details →
            </Link>
          )}
        </div>
      </div>

      {namespaceId && <FailureInvestigationPanel cluster={cluster} namespaceId={namespaceId} />}
    </div>
  );
}

export function DlqSignaturesPanel({ namespaceId, onFilterEntity }: DlqSignaturesPanelProps) {
  const { data, loading, available } = useDlqSignatures(namespaceId);

  if (loading || !available || !data || data.clusters.length === 0) {
    return null;
  }

  return (
    <div className="bg-primary-50 border border-primary-200 rounded-xl p-4 mb-3">
      <div className="flex items-center gap-2 mb-3">
        <Sparkles className="w-4 h-4 text-primary-500" />
        <h3 className="text-sm font-semibold text-primary-900">Recurring Failure Signatures</h3>
        <span className="text-xs text-primary-600">
          ServiceHub Interpretation — {data.clusters.length} cluster{data.clusters.length === 1 ? '' : 's'} detected
        </span>
      </div>

      <div className="space-y-3">
        {data.clusters.map((cluster) => (
          <ClusterCard
            key={`${cluster.dominantEntity}-${cluster.windowStart}`}
            cluster={cluster}
            namespaceId={namespaceId}
            onFilterEntity={onFilterEntity}
          />
        ))}
      </div>

      {data.singletons.length > 0 && (
        <p className="text-xs text-primary-600 mt-3">
          + {data.singletons.length} additional one-off failure{data.singletons.length === 1 ? '' : 's'} not part of a recurring pattern.
        </p>
      )}
    </div>
  );
}
