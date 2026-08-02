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
} from 'lucide-react';
import type { DlqClusterSignature, FailureKnowledge } from '@servicehub/ui-shared/lib/api/dlqSignatures';

interface FailureInvestigationPanelProps {
  cluster: DlqClusterSignature;
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

function KnowledgeSection({
  knowledge,
  isNew,
  firstSeenAt,
  windowEnd,
  occurrenceCount,
}: {
  knowledge: FailureKnowledge;
  isNew: boolean;
  firstSeenAt: string;
  windowEnd: string;
  occurrenceCount: number;
}) {
  const [expanded, setExpanded] = useState(false);

  if (!knowledge && !isNew) {
    return (
      <div className="bg-gray-50 border border-gray-200 rounded-lg p-4 text-center">
        <p className="text-sm text-gray-600">
          No operational knowledge has been recorded for this failure yet.
        </p>
      </div>
    );
  }

  const replayGuidance = knowledge?.replayGuidance || 'Investigate';
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
              {!isNew && knowledge && (
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
              {knowledge?.knowledgeVersion && (
                <div>
                  <span className="text-gray-500 block">Knowledge v{knowledge.knowledgeVersion}</span>
                  {knowledge.lastUpdatedAt && (
                    <span className="font-semibold text-gray-900 text-xs">{formatRelative(knowledge.lastUpdatedAt)}</span>
                  )}
                </div>
              )}
            </div>
          </div>

          <button
            onClick={() => setExpanded(!expanded)}
            className="p-1 hover:bg-gray-100 rounded transition-colors mt-1 flex-shrink-0"
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

      {/* Expandable content */}
      {expanded && knowledge && (
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
        </div>
      )}
    </div>
  );
}

export function FailureInvestigationPanel({ cluster }: FailureInvestigationPanelProps) {
  if (!cluster.knowledge && cluster.isNew) {
    return null;
  }

  return (
    <KnowledgeSection
      knowledge={cluster.knowledge!}
      isNew={cluster.isNew}
      firstSeenAt={cluster.firstSeenAt}
      windowEnd={cluster.windowEnd}
      occurrenceCount={cluster.occurrenceCount}
    />
  );
}
