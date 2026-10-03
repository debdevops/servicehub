import { useState, useEffect } from 'react';
import { useParams, useSearchParams, Link } from 'react-router-dom';
import { ArrowLeft, RefreshCw, Lightbulb, AlertCircle, ChevronLeft, ChevronRight } from 'lucide-react';
import {
  useDlqSignatureDetail,
  useSignatureTimeline,
  useResolveSignature,
  useReopenSignature,
  useSuppressSignature,
  useArchiveSignature,
} from '@servicehub/ui-shared/hooks/useDlqSignatures';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useDlqSummary } from '@servicehub/ui-shared/hooks/useDlqHistory';
import { useSignatureReplayJob } from '@servicehub/ui-shared/hooks/useSignatureReplay';
import { isTerminalBulkOperationStatus } from '@servicehub/ui-shared/lib/api/bulkOperations';
import { useActiveJobs } from '@servicehub/ui-shared/lib/activeJobs/ActiveJobsContext';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { extractApiError } from '@servicehub/ui-shared/lib/api/errors';
import type { ApiError } from '@servicehub/ui-shared/lib/api/types';
import { ProviderBadge } from '@servicehub/ui-shared/lib/providerStyles';
import { EnvironmentBadge } from '@/components/EnvironmentBadge';
import {
  StatusBadge,
  TrendBadge,
  AutonomyStatus,
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
import { tooltips } from '@servicehub/ui-shared/lib/helpContent';

function formatDate(ts: string): string {
  return new Date(ts).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

function formatRelative(ts: string): string {
  const minutes = Math.floor((Date.now() - new Date(ts).getTime()) / 60000);
  if (minutes < 1) return 'just now';
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}

const MESSAGES_PAGE_SIZE = 10;

// Every message in this list shares the signature's entity and (usually) its dead-letter
// reason — a signature is scoped to one entity/cause pair, so repeating either per row is pure
// noise (already shown once in the page header above). Correlation ID is the one field that
// actually varies message-to-message and is what an operator would search logs/traces for, so
// it leads; only fall back toward entity-level facts when a message has no correlation ID.
function messageIdentity(message: {
  correlationId: string | null;
  deadLetterReason: string | null;
  messageId: string;
}): { primary: string; badge: string | null; showMessageId: boolean } {
  if (message.correlationId) {
    return { primary: message.correlationId, badge: 'Correlation ID', showMessageId: true };
  }
  if (message.deadLetterReason) {
    return { primary: message.deadLetterReason, badge: null, showMessageId: true };
  }
  return { primary: message.messageId, badge: null, showMessageId: false };
}

export function SignatureDetailsPage() {
  const { signatureHash } = useParams<{ signatureHash: string }>();
  const [searchParams] = useSearchParams();
  const namespaceId = searchParams.get('namespace') || undefined;

  const { data: namespaces } = useNamespaces();
  const namespace = namespaces?.find(ns => ns.id === namespaceId);
  const { data: dlqSummary } = useDlqSummary(namespaceId);
  const { isDemoMode, cloudProvider } = useDemoContext();
  const basePath = isDemoMode && cloudProvider ? `/demo/${cloudProvider}` : '';
  const activeJobs = useActiveJobs();

  const {
    data: detail,
    isLoading: detailLoading,
    isError: detailFailed,
    error: detailError,
    refetch: refetchDetail,
  } = useDlqSignatureDetail(namespaceId, signatureHash);
  const { data: timeline, isLoading: timelineLoading } = useSignatureTimeline(namespaceId, signatureHash);

  const [selectedMessageId, setSelectedMessageId] = useState<number | null>(null);
  const [showReplayPreview, setShowReplayPreview] = useState(false);
  const [replayJobId, setReplayJobId] = useState<string | null>(null);
  const [messagePage, setMessagePage] = useState(1);

  // Reset pagination when navigating to a different signature — otherwise a page number left
  // over from a longer message list can land past the end of a shorter one.
  useEffect(() => {
    setMessagePage(1);
  }, [namespaceId, signatureHash]);

  // Same query SignatureReplayProgressPanel polls (React Query dedupes it) — used here only to
  // decide whether "Replay Signature" should be clickable. Until the first poll response lands,
  // treat a just-started job as active rather than momentarily re-enabling the button.
  const { data: activeReplayJob } = useSignatureReplayJob(replayJobId, namespaceId, signatureHash);
  const isReplayInFlight = !!replayJobId && (!activeReplayJob || !isTerminalBulkOperationStatus(activeReplayJob.status));

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

  // A live 404 is the terminal case: the signature is genuinely gone, so there is nothing to
  // retry. A settled query holding neither data nor an error means the same thing. Every other
  // failure — including Demo Mode's plain `Error('Signature not found')`, which carries no HTTP
  // status — falls through to the retryable branch, which surfaces its message verbatim.
  const isNotFound =
    (detailError as ApiError | undefined)?.response?.status === 404 || (!detailFailed && !detail);

  const correlationId = detail?.relatedMessages.find(m => m.correlationId)?.correlationId ?? null;
  const trendRecommendation = detail ? getTrendRecommendation(detail.trend) : null;

  return (
    <div className="flex-1 overflow-y-auto min-w-0">
    <div className="p-6 max-w-5xl mx-auto">
      <Link to={`${basePath}/signatures?namespace=${namespaceId}`} className="inline-flex items-center gap-1.5 text-sm text-primary-600 hover:text-primary-700 mb-4">
        <ArrowLeft className="w-4 h-4" />
        Back to signatures
      </Link>

      {detailLoading ? (
        <div className="text-sm text-gray-500">Loading signature…</div>
      ) : detailFailed || !detail ? (
        // Previously `detailLoading || !detail` drove the spinner, so a failed request — a 404
        // for a signature whose namespace was deleted, an auth rejection, an unreachable API —
        // left "Loading signature…" on screen forever with no way out. A 404 is terminal, so it
        // gets a plain not-found message; anything else is worth retrying.
        isNotFound ? (
          <div className="bg-gray-50 border border-gray-200 rounded-xl p-6 text-center">
            <p className="text-sm font-medium text-gray-700">Signature not found</p>
            <p className="text-sm text-gray-500 mt-1">
              This failure signature is no longer available. Its namespace may have been removed,
              or the signature may have aged out of the retained history.
            </p>
          </div>
        ) : (
          <div className="flex items-start gap-2 rounded-lg bg-red-50 border border-red-200 p-4 text-sm text-red-700">
            <AlertCircle className="w-4 h-4 mt-0.5 shrink-0" />
            <span className="flex-1">
              {extractApiError(detailError, 'Failed to load this failure signature.')}
            </span>
            <button
              className="text-xs font-medium underline shrink-0"
              onClick={() => refetchDetail()}
            >
              Try Again
            </button>
          </div>
        )
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
                {namespace?.environment && <EnvironmentBadge env={namespace.environment} />}
              </div>
            </div>

            <p className="text-sm text-gray-700 mb-1">
              {detail.knowledge?.rootCause ? (
                <>
                  <span className="font-medium">Likely cause (recorded by an operator):</span> {detail.knowledge.rootCause}
                </>
              ) : (
                detail.explanation
              )}
            </p>
            {detail.confidence === 'Medium' && (
              <p className="text-xs text-gray-500 mb-3">
                ServiceHub has medium confidence in this classification — review the technical evidence below before relying on it fully.
              </p>
            )}

            <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 text-sm mb-4">
              <div>
                <span className="text-gray-500 block text-xs">Namespace</span>
                <span className="font-medium text-gray-900">{namespace?.displayName || namespace?.name || '—'}</span>
              </div>
              <div>
                <span className="text-gray-500 block text-xs" title={tooltips.signatureDetails.dlqShare.detail}>% of this namespace's DLQ</span>
                <span className="font-medium text-gray-900">
                  {dlqSummary && dlqSummary.activeMessages > 0
                    ? `${Math.round((detail.size / dlqSummary.activeMessages) * 100)}%`
                    : '—'}
                </span>
              </div>
              <div>
                <span className="text-gray-500 block text-xs">First Seen</span>
                <span className="font-medium text-gray-900">{formatDate(detail.firstSeenAt)}</span>
              </div>
              <div>
                <span className="text-gray-500 block text-xs">Last Active</span>
                <span className="font-medium text-gray-900">{formatDate(detail.windowEnd)}</span>
              </div>
              <div>
                <span
                  className="text-gray-500 block text-xs"
                  title="How many times ServiceHub's analysis has re-detected this pattern since it was first seen — not a count of affected messages. See Related Messages below for that."
                >
                  Occurrence Count
                </span>
                <span className="font-medium text-gray-900">{detail.occurrenceCount}</span>
              </div>
              <div>
                <span className="text-gray-500 block text-xs" title={tooltips.signatureDetails.confidence.detail}>Confidence</span>
                <span className="font-medium text-gray-900">{detail.confidence}</span>
              </div>
              {!detail.isCurrentlyClustered && (
                <div>
                  <span className="text-gray-500 block text-xs" title={tooltips.signatureDetails.currentlyClustered.detail}>Currently Clustered</span>
                  <span className="font-medium text-gray-900">No — historical record</span>
                </div>
              )}
            </div>

            <div className="mb-3">
              <AutonomyStatus signatureHash={detail.signatureHash} />
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
                disabled={!detail.isCurrentlyClustered || detail.relatedMessages.length === 0 || isReplayInFlight}
                title={
                  isReplayInFlight
                    ? 'A replay for this signature is already queued or running'
                    : !detail.isCurrentlyClustered || detail.relatedMessages.length === 0
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
            <h2
              className="text-sm font-semibold text-gray-700 uppercase tracking-wider mb-3"
              title="Messages currently in the DLQ that match this pattern right now — distinct from Occurrence Count above."
            >
              Related Messages ({detail.relatedMessages.length})
            </h2>
            {detail.relatedMessages.length === 0 ? (
              <p className="text-sm text-gray-500">No related messages available.</p>
            ) : (
              (() => {
                const totalPages = Math.max(1, Math.ceil(detail.relatedMessages.length / MESSAGES_PAGE_SIZE));
                const currentPage = Math.min(messagePage, totalPages);
                const start = (currentPage - 1) * MESSAGES_PAGE_SIZE;
                const pageMessages = detail.relatedMessages.slice(start, start + MESSAGES_PAGE_SIZE);

                return (
                  <>
                    <div className="divide-y divide-gray-100">
                      {pageMessages.map(message => {
                        const identity = messageIdentity(message);
                        return (
                          <button
                            key={message.id}
                            onClick={() => setSelectedMessageId(message.id)}
                            className="w-full text-left py-2.5 flex items-center justify-between gap-3 hover:bg-gray-50 rounded-lg px-2 -mx-2"
                          >
                            <div className="min-w-0">
                              <div className="flex items-center gap-1.5 min-w-0">
                                <p className="text-sm font-medium text-gray-900 font-mono truncate">
                                  {identity.primary}
                                </p>
                                {identity.badge && (
                                  <span className="shrink-0 text-[10px] px-1.5 py-0.5 rounded bg-gray-100 text-gray-500 font-medium">
                                    {identity.badge}
                                  </span>
                                )}
                              </div>
                              <p className="text-xs text-gray-500 truncate mt-0.5">
                                {message.deliveryCount != null && (
                                  <>{message.deliveryCount} {message.deliveryCount === 1 ? 'delivery' : 'deliveries'}</>
                                )}
                                {message.deadLetterTimeUtc && <> · dead-lettered {formatRelative(message.deadLetterTimeUtc)}</>}
                              </p>
                              {identity.showMessageId && (
                                <p className="text-[11px] text-gray-400 font-mono truncate mt-0.5">{message.messageId}</p>
                              )}
                            </div>
                            <StatusBadge status={message.status} />
                          </button>
                        );
                      })}
                    </div>

                    {totalPages > 1 && (
                      <div className="flex items-center justify-between gap-3 mt-3 pt-3 border-t border-gray-100">
                        <p className="text-xs text-gray-500">
                          Showing {start + 1}–{Math.min(start + MESSAGES_PAGE_SIZE, detail.relatedMessages.length)} of{' '}
                          {detail.relatedMessages.length}
                        </p>
                        <div className="flex items-center gap-2">
                          <button
                            onClick={() => setMessagePage(p => Math.max(1, p - 1))}
                            disabled={currentPage === 1}
                            className="p-1 rounded-md border border-gray-200 text-gray-500 hover:bg-gray-50 disabled:opacity-40 disabled:cursor-not-allowed"
                            aria-label="Previous page"
                          >
                            <ChevronLeft className="w-4 h-4" />
                          </button>
                          <span className="text-xs text-gray-600">
                            Page {currentPage} of {totalPages}
                          </span>
                          <button
                            onClick={() => setMessagePage(p => Math.min(totalPages, p + 1))}
                            disabled={currentPage === totalPages}
                            className="p-1 rounded-md border border-gray-200 text-gray-500 hover:bg-gray-50 disabled:opacity-40 disabled:cursor-not-allowed"
                            aria-label="Next page"
                          >
                            <ChevronRight className="w-4 h-4" />
                          </button>
                        </div>
                      </div>
                    )}
                  </>
                );
              })()
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
                // Tracked independently of this page's lifetime — the toast still fires (and
                // caches still refresh) even if the user navigates away before it finishes.
                activeJobs.trackSignatureReplay(jobId, namespaceId, signatureHash);
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
    </div>
  );
}

export default SignatureDetailsPage;
