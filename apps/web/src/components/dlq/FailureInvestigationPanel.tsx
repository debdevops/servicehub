import { useState } from 'react';
import {
  ChevronDown,
  ChevronUp,
  AlertCircle,
  CheckCircle,
  AlertTriangle,
  Link as LinkIcon,
  Clock,
  User,
  FileText,
  Tag,
  Pencil,
  History,
  CalendarClock,
} from 'lucide-react';
import type { DlqClusterSignature, FailureKnowledge } from '@servicehub/ui-shared/lib/api/dlqSignatures';
import { useMarkForReview } from '@servicehub/ui-shared/hooks/useDlqSignatures';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { KnowledgeEditDialog } from './KnowledgeEditDialog';
import { KnowledgeHistoryPanel } from './KnowledgeHistoryPanel';

interface FailureInvestigationPanelProps {
  cluster: DlqClusterSignature;
  namespaceId: string;
}

const REPLAY_GUIDANCE_COLORS: Record<string, { bg: string; text: string; icon: React.ReactNode }> = {
  Safe: {
    bg: 'bg-green-50',
    text: 'text-green-700',
    icon: <CheckCircle className="w-4 h-4" />,
  },
  Unsafe: {
    bg: 'bg-red-50',
    text: 'text-red-700',
    icon: <AlertCircle className="w-4 h-4" />,
  },
  Investigate: {
    bg: 'bg-amber-50',
    text: 'text-amber-700',
    icon: <AlertTriangle className="w-4 h-4" />,
  },
};

function ReviewStatusBadge({ knowledge }: { knowledge: FailureKnowledge }) {
  if (!knowledge.reviewDueAt) return null;

  if (knowledge.isReviewOverdue) {
    return (
      <span className="inline-flex items-center gap-1 px-2 py-0.5 bg-red-100 text-red-700 text-xs font-semibold rounded-full">
        <CalendarClock className="w-3 h-3" />
        Review overdue
      </span>
    );
  }

  return (
    <span className="inline-flex items-center gap-1 px-2 py-0.5 bg-gray-100 text-gray-600 text-xs font-semibold rounded-full">
      <CalendarClock className="w-3 h-3" />
      Review due {new Date(knowledge.reviewDueAt).toLocaleDateString()}
    </span>
  );
}

function MarkForReviewControl({ namespaceId, signatureHash }: { namespaceId: string; signatureHash: string }) {
  const { isDemoMode } = useDemoContext();
  const [showPicker, setShowPicker] = useState(false);
  const [date, setDate] = useState('');
  const markForReview = useMarkForReview();

  if (isDemoMode) return null;

  if (!showPicker) {
    return (
      <button
        onClick={() => setShowPicker(true)}
        className="flex items-center gap-1 text-xs text-gray-500 hover:text-gray-700 font-medium"
      >
        <CalendarClock className="w-3.5 h-3.5" />
        Mark for review
      </button>
    );
  }

  return (
    <div className="flex items-center gap-2">
      <input
        type="date"
        value={date}
        onChange={(e) => setDate(e.target.value)}
        className="px-2 py-1 border border-gray-300 rounded-lg text-xs"
      />
      <button
        onClick={() => {
          if (!date) return;
          markForReview.mutate(
            { namespaceId, signatureHash, reviewDueAt: new Date(date).toISOString() },
            { onSuccess: () => setShowPicker(false) },
          );
        }}
        disabled={!date || markForReview.isPending}
        className="px-2 py-1 text-xs font-medium text-white bg-primary-500 hover:bg-primary-600 disabled:opacity-50 rounded-lg"
      >
        Set
      </button>
      <button
        onClick={() => setShowPicker(false)}
        className="px-2 py-1 text-xs text-gray-500 hover:text-gray-700"
      >
        Cancel
      </button>
    </div>
  );
}

function KnowledgeSection({
  knowledge,
  isNew,
  firstSeenAt,
  windowEnd,
  occurrenceCount,
  namespaceId,
  signatureHash,
}: {
  knowledge: FailureKnowledge | null;
  isNew: boolean;
  firstSeenAt: string;
  windowEnd: string;
  occurrenceCount: number;
  namespaceId: string;
  signatureHash: string;
}) {
  const [expanded, setExpanded] = useState(false);
  const [showEditDialog, setShowEditDialog] = useState(false);
  const [showHistory, setShowHistory] = useState(false);

  if (!knowledge) {
    return (
      <>
        <div className="bg-gray-50 border border-gray-200 rounded-lg p-4 text-center">
          <p className="text-sm text-gray-600 mb-2">
            No operational knowledge has been recorded for this failure yet.
          </p>
          <button
            onClick={() => setShowEditDialog(true)}
            className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-white bg-primary-500 hover:bg-primary-600 rounded-lg"
          >
            <Pencil className="w-3.5 h-3.5" />
            Add Knowledge
          </button>
        </div>
        {showEditDialog && (
          <KnowledgeEditDialog
            namespaceId={namespaceId}
            signatureHash={signatureHash}
            knowledge={null}
            onClose={() => setShowEditDialog(false)}
          />
        )}
      </>
    );
  }

  const replayGuidance = knowledge.replayGuidance || 'Investigate';
  const colors = REPLAY_GUIDANCE_COLORS[replayGuidance] || REPLAY_GUIDANCE_COLORS.Investigate;

  const formatDate = (dateStr: string) => {
    const date = new Date(dateStr);
    return date.toLocaleString(undefined, {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  };

  const formatRelative = (dateStr: string) => {
    const diff = Date.now() - new Date(dateStr).getTime();
    const mins = Math.floor(diff / 60000);
    if (mins < 1) return 'just now';
    if (mins < 60) return `${mins}m ago`;
    const hrs = Math.floor(mins / 60);
    if (hrs < 24) return `${hrs}h ago`;
    const days = Math.floor(hrs / 24);
    return `${days}d ago`;
  };

  return (
    <div className="border border-gray-200 rounded-lg overflow-hidden">
      {/* Header with quick info */}
      <div className="bg-white px-4 py-3">
        <div className="flex items-start justify-between gap-3">
          <div className="flex-1">
            <div className="flex items-center gap-2 flex-wrap mb-2">
              {!isNew && (
                <span className="inline-flex items-center gap-1 px-2 py-0.5 bg-blue-100 text-blue-700 text-xs font-semibold rounded-full">
                  ✓ Known Failure
                </span>
              )}
              {!isNew && (
                <span className="inline-flex items-center gap-1 px-2 py-0.5 bg-purple-100 text-purple-700 text-xs font-semibold rounded-full">
                  🔁 Recurring
                </span>
              )}
              <span className={`inline-flex items-center gap-1 px-2 py-0.5 text-xs font-semibold rounded-full ${colors.bg} ${colors.text}`}>
                {colors.icon}
                {replayGuidance}
              </span>
              <ReviewStatusBadge knowledge={knowledge} />
            </div>

            {/* Key metrics */}
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-2 text-xs">
              <div>
                <span className="text-gray-500 block">Occurrences</span>
                <span className="font-semibold text-gray-900">{occurrenceCount}</span>
              </div>
              <div>
                <span className="text-gray-500 block">First Seen</span>
                <span className="font-semibold text-gray-900">{formatRelative(firstSeenAt)}</span>
              </div>
              <div>
                <span className="text-gray-500 block">Last Seen</span>
                <span className="font-semibold text-gray-900">{formatRelative(windowEnd)}</span>
              </div>
              {knowledge.knowledgeVersion && (
                <div>
                  <span className="text-gray-500 block">Knowledge v{knowledge.knowledgeVersion}</span>
                  {knowledge.lastUpdatedAt && (
                    <span className="font-semibold text-gray-900 text-xs">{formatRelative(knowledge.lastUpdatedAt)}</span>
                  )}
                </div>
              )}
            </div>
          </div>

          <div className="flex items-center gap-1 shrink-0">
            <button
              onClick={() => setShowEditDialog(true)}
              className="p-1 hover:bg-gray-100 rounded transition-colors mt-1"
              aria-label="Edit knowledge"
              title="Edit knowledge"
            >
              <Pencil className="w-4 h-4 text-gray-600" />
            </button>
            <button
              onClick={() => setExpanded(!expanded)}
              className="p-1 hover:bg-gray-100 rounded transition-colors mt-1"
              aria-label={expanded ? 'Collapse details' : 'Expand details'}
            >
              {expanded ? (
                <ChevronUp className="w-4 h-4 text-gray-600" />
              ) : (
                <ChevronDown className="w-4 h-4 text-gray-600" />
              )}
            </button>
          </div>
        </div>
      </div>

      {/* Expandable content */}
      {expanded && (
        <div className="border-t border-gray-200 bg-gray-50 px-4 py-3 space-y-4">
          {/* Root Cause */}
          {knowledge.rootCause && (
            <div>
              <h4 className="text-xs font-semibold text-gray-600 uppercase mb-1">Root Cause</h4>
              <p className="text-sm text-gray-900 leading-relaxed">{knowledge.rootCause}</p>
            </div>
          )}

          {/* Resolution Notes */}
          {knowledge.resolutionNotes && (
            <div>
              <h4 className="text-xs font-semibold text-gray-600 uppercase mb-1">How We Fixed It</h4>
              <p className="text-sm text-gray-900 leading-relaxed">{knowledge.resolutionNotes}</p>
            </div>
          )}

          {/* Replay Guidance Details */}
          {knowledge.replayGuidance && (
            <div>
              <h4 className="text-xs font-semibold text-gray-600 uppercase mb-1">Replay Guidance</h4>
              <div className={`text-sm p-2 rounded ${colors.bg} ${colors.text} font-medium`}>
                {knowledge.replayGuidance}
              </div>
            </div>
          )}

          {/* Owner */}
          {knowledge.owner && (
            <div>
              <div className="flex items-center gap-2 text-xs font-semibold text-gray-600 uppercase mb-1">
                <User className="w-3 h-3" />
                Owner
              </div>
              <p className="text-sm text-gray-900">{knowledge.owner}</p>
            </div>
          )}

          {/* Runbook Link */}
          {knowledge.runbookLink && (
            <div>
              <div className="flex items-center gap-2 text-xs font-semibold text-gray-600 uppercase mb-1">
                <LinkIcon className="w-3 h-3" />
                Runbook
              </div>
              <a
                href={knowledge.runbookLink}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex items-center gap-1 text-sm text-primary-600 hover:text-primary-700 hover:underline"
              >
                {knowledge.runbookLink}
                <LinkIcon className="w-3 h-3" />
              </a>
            </div>
          )}

          {/* Operational Notes */}
          {knowledge.operationalNotes && (
            <div>
              <div className="flex items-center gap-2 text-xs font-semibold text-gray-600 uppercase mb-1">
                <FileText className="w-3 h-3" />
                Operational Notes
              </div>
              <p className="text-sm text-gray-900 leading-relaxed">{knowledge.operationalNotes}</p>
            </div>
          )}

          {/* Tags */}
          {knowledge.tags && (
            <div>
              <div className="flex items-center gap-2 text-xs font-semibold text-gray-600 uppercase mb-1">
                <Tag className="w-3 h-3" />
                Tags
              </div>
              <div className="flex flex-wrap gap-1">
                {knowledge.tags.split(',').map((tag) => (
                  <span
                    key={tag.trim()}
                    className="inline-flex items-center px-2 py-0.5 bg-gray-200 text-gray-700 text-xs rounded-full font-medium"
                  >
                    {tag.trim()}
                  </span>
                ))}
              </div>
            </div>
          )}

          {/* Review Due */}
          {knowledge.reviewDueAt && (
            <div>
              <div className="flex items-center gap-2 text-xs font-semibold text-gray-600 uppercase mb-1">
                <Clock className="w-3 h-3" />
                Review Due
              </div>
              <p className="text-sm text-gray-900">{formatDate(knowledge.reviewDueAt)}</p>
            </div>
          )}

          {/* Owner / updated-by */}
          {knowledge.updatedBy && (
            <p className="text-xs text-gray-400">Last updated by {knowledge.updatedBy}</p>
          )}

          {/* Actions */}
          <div className="flex items-center justify-between gap-2 pt-2 border-t border-gray-200">
            <MarkForReviewControl namespaceId={namespaceId} signatureHash={signatureHash} />
            <button
              onClick={() => setShowHistory((v) => !v)}
              className="flex items-center gap-1 text-xs text-gray-500 hover:text-gray-700 font-medium"
            >
              <History className="w-3.5 h-3.5" />
              {showHistory ? 'Hide history' : 'View history'}
            </button>
          </div>

          {showHistory && (
            <div className="pt-2">
              <KnowledgeHistoryPanel namespaceId={namespaceId} signatureHash={signatureHash} />
            </div>
          )}
        </div>
      )}

      {showEditDialog && (
        <KnowledgeEditDialog
          namespaceId={namespaceId}
          signatureHash={signatureHash}
          knowledge={knowledge}
          onClose={() => setShowEditDialog(false)}
        />
      )}
    </div>
  );
}

export function FailureInvestigationPanel({ cluster, namespaceId }: FailureInvestigationPanelProps) {
  return (
    <KnowledgeSection
      knowledge={cluster.knowledge ?? null}
      isNew={cluster.isNew}
      firstSeenAt={cluster.firstSeenAt}
      windowEnd={cluster.windowEnd}
      occurrenceCount={cluster.occurrenceCount}
      namespaceId={namespaceId}
      signatureHash={cluster.signatureHash}
    />
  );
}
