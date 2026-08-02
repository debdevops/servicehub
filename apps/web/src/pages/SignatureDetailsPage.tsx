import { useState } from 'react';
import { useParams, useSearchParams, Link } from 'react-router-dom';
import { ArrowLeft } from 'lucide-react';
import {
  useDlqSignatureDetail,
  useSignatureTimeline,
  useResolveSignature,
  useReopenSignature,
  useSuppressSignature,
  useArchiveSignature,
} from '@servicehub/ui-shared/hooks/useDlqSignatures';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { ProviderBadge } from '@servicehub/ui-shared/lib/providerStyles';
import {
  StatusBadge,
  TrendBadge,
  FailureInvestigationPanel,
  SignatureLifecycleActions,
  SignatureTimelinePanel,
  DlqTimelineDrawer,
} from '@/components/dlq';

function formatDate(ts: string): string {
  return new Date(ts).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

export function SignatureDetailsPage() {
  const { signatureHash } = useParams<{ signatureHash: string }>();
  const [searchParams] = useSearchParams();
  const namespaceId = searchParams.get('namespace') || undefined;
  const { isDemoMode } = useDemoContext();

  const { data: namespaces } = useNamespaces();
  const namespace = namespaces?.find(ns => ns.id === namespaceId);

  const { data: detail, isLoading: detailLoading } = useDlqSignatureDetail(namespaceId, signatureHash);
  const { data: timeline, isLoading: timelineLoading } = useSignatureTimeline(namespaceId, signatureHash);

  const [selectedMessageId, setSelectedMessageId] = useState<number | null>(null);

  const resolve = useResolveSignature();
  const reopen = useReopenSignature();
  const suppress = useSuppressSignature();
  const archive = useArchiveSignature();
  const anyPending = resolve.isPending || reopen.isPending || suppress.isPending || archive.isPending;

  const mutate = (mutation: typeof resolve) => {
    if (!namespaceId || !signatureHash) return;
    mutation.mutate({ namespaceId, signatureHash });
  };

  if (isDemoMode) {
    return (
      <div className="p-6 max-w-3xl mx-auto">
        <div className="bg-gray-50 border border-gray-200 rounded-xl p-6 text-center text-sm text-gray-600">
          Signature details require a live connection — try a real namespace.
        </div>
      </div>
    );
  }

  if (!namespaceId || !signatureHash) {
    return (
      <div className="p-6 max-w-3xl mx-auto">
        <div className="bg-gray-50 border border-gray-200 rounded-xl p-6 text-center text-sm text-gray-600">
          Missing namespace or signature reference.
        </div>
      </div>
    );
  }

  return (
    <div className="p-6 max-w-3xl mx-auto">
      <Link to={`/signatures?namespace=${namespaceId}`} className="inline-flex items-center gap-1.5 text-sm text-primary-600 hover:text-primary-700 mb-4">
        <ArrowLeft className="w-4 h-4" />
        Back to signatures
      </Link>

      {detailLoading || !detail ? (
        <div className="text-sm text-gray-500">Loading signature…</div>
      ) : (
        <>
          <div className="bg-white border border-gray-200 rounded-xl p-5 mb-4">
            <div className="flex items-start justify-between gap-3 flex-wrap mb-3">
              <div>
                <h1 className="text-lg font-semibold text-gray-900">
                  {detail.dominantDeadletterReason} · {detail.dominantEntity}
                </h1>
                <p className="text-xs text-gray-400 font-mono mt-0.5 break-all">Fingerprint: {detail.signatureHash}</p>
              </div>
              <div className="flex items-center gap-2 flex-wrap">
                <StatusBadge status={detail.status} size="md" />
                <TrendBadge trend={detail.trend} size="md" />
                {namespace?.cloudProvider && <ProviderBadge provider={namespace.cloudProvider} />}
              </div>
            </div>

            <p className="text-sm text-gray-700 mb-4">{detail.explanation}</p>

            <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 text-sm mb-4">
              <div>
                <span className="text-gray-500 block text-xs">Namespace</span>
                <span className="font-medium text-gray-900">{namespace?.displayName || namespace?.name || '—'}</span>
              </div>
              <div>
                <span className="text-gray-500 block text-xs">First Seen</span>
                <span className="font-medium text-gray-900">{formatDate(detail.firstSeenAt)}</span>
              </div>
              <div>
                <span className="text-gray-500 block text-xs">Last Seen</span>
                <span className="font-medium text-gray-900">{formatDate(detail.windowEnd)}</span>
              </div>
              <div>
                <span className="text-gray-500 block text-xs">Occurrence Count</span>
                <span className="font-medium text-gray-900">{detail.occurrenceCount}</span>
              </div>
              <div>
                <span className="text-gray-500 block text-xs">Confidence</span>
                <span className="font-medium text-gray-900">{detail.confidence}</span>
              </div>
              {!detail.isCurrentlyClustered && (
                <div>
                  <span className="text-gray-500 block text-xs">Currently Clustered</span>
                  <span className="font-medium text-gray-900">No — historical record</span>
                </div>
              )}
            </div>

            <SignatureLifecycleActions
              status={detail.status}
              onResolve={() => mutate(resolve)}
              onReopen={() => mutate(reopen)}
              onSuppress={() => mutate(suppress)}
              onArchive={() => mutate(archive)}
              pending={anyPending}
            />
          </div>

          {/* Knowledge */}
          <div className="mb-4">
            <h2 className="text-sm font-semibold text-gray-700 uppercase tracking-wider mb-2">Knowledge</h2>
            <FailureInvestigationPanel cluster={detail} />
          </div>

          {/* Timeline */}
          <div className="bg-white border border-gray-200 rounded-xl p-5 mb-4">
            <h2 className="text-sm font-semibold text-gray-700 uppercase tracking-wider mb-4">Timeline</h2>
            {timelineLoading ? (
              <div className="text-sm text-gray-500">Loading timeline…</div>
            ) : (
              <SignatureTimelinePanel events={timeline?.events ?? []} />
            )}
          </div>

          {/* Related Messages */}
          <div className="bg-white border border-gray-200 rounded-xl p-5">
            <h2 className="text-sm font-semibold text-gray-700 uppercase tracking-wider mb-3">
              Related Messages ({detail.relatedMessages.length})
            </h2>
            {detail.relatedMessages.length === 0 ? (
              <p className="text-sm text-gray-500">No related messages available.</p>
            ) : (
              <div className="divide-y divide-gray-100">
                {detail.relatedMessages.map(message => (
                  <button
                    key={message.id}
                    onClick={() => setSelectedMessageId(message.id)}
                    className="w-full text-left py-2.5 flex items-center justify-between gap-3 hover:bg-gray-50 rounded-lg px-2 -mx-2"
                  >
                    <div className="min-w-0">
                      <p className="text-sm font-medium text-gray-900 truncate">{message.messageId}</p>
                      <p className="text-xs text-gray-500 truncate">{message.entityName}</p>
                    </div>
                    <StatusBadge status={message.status} />
                  </button>
                ))}
              </div>
            )}
          </div>

          <DlqTimelineDrawer messageId={selectedMessageId} onClose={() => setSelectedMessageId(null)} />
        </>
      )}
    </div>
  );
}

export default SignatureDetailsPage;
