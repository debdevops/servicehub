import { useState } from 'react';
import { useParams, useSearchParams, Link } from 'react-router-dom';
import { ArrowLeft, RefreshCw, Lightbulb } from 'lucide-react';
import {
  useDlqSignatureDetail,
  useSignatureTimeline,
  useResolveSignature,
  useReopenSignature,
  useSuppressSignature,
  useArchiveSignature,
} from '@servicehub/ui-shared/hooks/useDlqSignatures';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { ProviderBadge } from '@servicehub/ui-shared/lib/providerStyles';
import {
  StatusBadge,
  TrendBadge,
  FailureInvestigationPanel,
  SignatureLifecycleActions,
  SignatureTimelinePanel,
  DlqTimelineDrawer,
  SignatureReplayPreviewModal,
  SignatureReplayProgressPanel,
  ReplaySafetyPanel,
  RootCauseExplorerPanel,
  RecentChangesPanel,
  CrossCloudTraceLink,
  getTrendRecommendation,
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

  const { data: namespaces } = useNamespaces();
  const namespace = namespaces?.find(ns => ns.id === namespaceId);

  const { data: detail, isLoading: detailLoading } = useDlqSignatureDetail(namespaceId, signatureHash);
  const { data: timeline, isLoading: timelineLoading } = useSignatureTimeline(namespaceId, signatureHash);

  const [selectedMessageId, setSelectedMessageId] = useState<number | null>(null);
  const [showReplayPreview, setShowReplayPreview] = useState(false);
  const [replayJobId, setReplayJobId] = useState<string | null>(null);

  const resolve = useResolveSignature();
  const reopen = useReopenSignature();
  const suppress = useSuppressSignature();
  const archive = useArchiveSignature();
  const anyPending = resolve.isPending || reopen.isPending || suppress.isPending || archive.isPending;

  const mutate = (mutation: typeof resolve) => {
    if (!namespaceId || !signatureHash) return;
    mutation.mutate({ namespaceId, signatureHash });
  };

  if (!namespaceId || !signatureHash) {
    return (
      <div className="p-6 max-w-3xl mx-auto">
        <div className="bg-gray-50 border border-gray-200 rounded-xl p-6 text-center text-sm text-gray-600">
          Missing namespace or signature reference.
        </div>
      </div>
    );
  }

  const correlationId = detail?.relatedMessages.find(m => m.correlationId)?.correlationId ?? null;
  const trendRecommendation = detail ? getTrendRecommendation(detail.trend) : null;

  return (
    <div className="p-6 max-w-5xl mx-auto">
      <Link to={`/signatures?namespace=${namespaceId}`} className="inline-flex items-center gap-1.5 text-sm text-primary-600 hover:text-primary-700 mb-4">
        <ArrowLeft className="w-4 h-4" />
        Back to signatures
      </Link>

      {detailLoading || !detail ? (
        <div className="text-sm text-gray-500">Loading signature…</div>
      ) : (
        <>
          {/* Header / Identity */}
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

            {trendRecommendation && (
              <div className="flex items-center gap-2 text-sm text-sky-700 bg-sky-50 border border-sky-100 rounded-lg px-3 py-2">
                <Lightbulb className="w-4 h-4 shrink-0" />
                {trendRecommendation}
              </div>
            )}
          </div>

          {/* Actions bar */}
          <div className="bg-white border border-gray-200 rounded-xl p-4 mb-4 flex items-center justify-between gap-2 flex-wrap">
            <SignatureLifecycleActions
              status={detail.status}
              onResolve={() => mutate(resolve)}
              onReopen={() => mutate(reopen)}
              onSuppress={() => mutate(suppress)}
              onArchive={() => mutate(archive)}
              pending={anyPending}
            />
            <div className="flex items-center gap-2 flex-wrap">
              <CrossCloudTraceLink correlationId={correlationId} />
              <button
                onClick={() => setShowReplayPreview(true)}
                disabled={!detail.isCurrentlyClustered || detail.relatedMessages.length === 0}
                title={
                  !detail.isCurrentlyClustered || detail.relatedMessages.length === 0
                    ? 'No currently resolvable messages for this signature'
                    : undefined
                }
                className="flex items-center gap-1.5 px-2.5 py-1 text-xs font-medium rounded-md bg-sky-50 text-sky-700 border border-sky-200 hover:bg-sky-100 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                <RefreshCw className="w-3.5 h-3.5" />
                Replay Signature
              </button>
            </div>
          </div>

          {/* Root Cause & Knowledge · Replay Safety & History */}
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-4 mb-4 items-start">
            <div>
              <h2 className="text-sm font-semibold text-gray-700 uppercase tracking-wider mb-2">Root Cause &amp; Knowledge</h2>
              <FailureInvestigationPanel cluster={detail} namespaceId={namespaceId} />
            </div>
            <ReplaySafetyPanel
              namespaceId={namespaceId}
              signatureHash={signatureHash}
              cloudProvider={namespace?.cloudProvider}
              lastSeenAt={detail.windowEnd}
              onStartReplay={() => setShowReplayPreview(true)}
            />
          </div>

          {/* Root Cause Explorer */}
          <div className="mb-4">
            <RootCauseExplorerPanel
              namespaceId={namespaceId}
              signatureHash={signatureHash}
              dominantDeadletterReason={detail.dominantDeadletterReason}
              namespaces={namespaces}
            />
          </div>

          {/* Recent Changes Before Failure */}
          <div className="mb-4">
            <RecentChangesPanel
              namespaceId={namespaceId}
              signatureHash={signatureHash}
              firstSeenAt={detail.firstSeenAt}
            />
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

          {showReplayPreview && (
            <SignatureReplayPreviewModal
              namespaceId={namespaceId}
              signatureHash={signatureHash}
              onClose={() => setShowReplayPreview(false)}
              onJobStarted={(jobId) => {
                setShowReplayPreview(false);
                setReplayJobId(jobId);
              }}
            />
          )}

          {replayJobId && (
            <SignatureReplayProgressPanel
              jobId={replayJobId}
              namespaceId={namespaceId}
              signatureHash={signatureHash}
              onDismiss={() => setReplayJobId(null)}
            />
          )}
        </>
      )}
    </div>
  );
}

export default SignatureDetailsPage;
