import { useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import { Info } from 'lucide-react';
import { useUpsertKnowledge } from '@servicehub/ui-shared/hooks/useDlqSignatures';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import type { FailureKnowledge } from '@servicehub/ui-shared/lib/api/dlqSignatures';

interface KnowledgeEditDialogProps {
  namespaceId: string;
  signatureHash: string;
  knowledge: FailureKnowledge | null;
  onClose: () => void;
}

const REPLAY_GUIDANCE_OPTIONS = ['', 'Safe', 'Unsafe', 'Investigate'] as const;

const MAX_LONG_TEXT = 4096;
const MAX_TAGS = 2048;
const MAX_SHORT_TEXT = 256;

function isValidRunbookUrl(value: string): boolean {
  try {
    const url = new URL(value);
    return url.protocol === 'http:' || url.protocol === 'https:';
  } catch {
    return false;
  }
}

function toDateInputValue(iso: string | null | undefined): string {
  if (!iso) return '';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  return date.toISOString().slice(0, 10);
}

/** Creates or updates a failure signature's operational knowledge. Same modal/form
 * conventions as RuleBuilderDialog: controlled local state, Escape-to-close, disabled
 * Save while pending, client-side validation mirroring the backend's DataAnnotations. */
export function KnowledgeEditDialog({ namespaceId, signatureHash, knowledge, onClose }: KnowledgeEditDialogProps) {
  const { isDemoMode } = useDemoContext();
  const upsert = useUpsertKnowledge();

  const [rootCause, setRootCause] = useState('');
  const [resolutionNotes, setResolutionNotes] = useState('');
  const [operationalNotes, setOperationalNotes] = useState('');
  const [runbookLink, setRunbookLink] = useState('');
  const [owner, setOwner] = useState('');
  const [replayGuidance, setReplayGuidance] = useState<(typeof REPLAY_GUIDANCE_OPTIONS)[number]>('');
  const [tags, setTags] = useState('');
  const [reviewDueAt, setReviewDueAt] = useState('');
  const [changedBy, setChangedBy] = useState('');

  useEffect(() => {
    setRootCause(knowledge?.rootCause ?? '');
    setResolutionNotes(knowledge?.resolutionNotes ?? '');
    setOperationalNotes(knowledge?.operationalNotes ?? '');
    setRunbookLink(knowledge?.runbookLink ?? '');
    setOwner(knowledge?.owner ?? '');
    setReplayGuidance((knowledge?.replayGuidance as typeof replayGuidance) ?? '');
    setTags(knowledge?.tags ?? '');
    setReviewDueAt(toDateInputValue(knowledge?.reviewDueAt));
    setChangedBy('');
  }, [knowledge]);

  useEffect(() => {
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !upsert.isPending) onClose();
    };
    window.addEventListener('keydown', handleEscape);
    return () => window.removeEventListener('keydown', handleEscape);
  }, [upsert.isPending, onClose]);

  const rootCauseError =
    rootCause.trim().length === 0
      ? 'Root cause is required'
      : rootCause.length > MAX_LONG_TEXT
        ? `Must be ${MAX_LONG_TEXT} characters or fewer`
        : null;
  const runbookLinkError =
    runbookLink.trim().length > 0 && !isValidRunbookUrl(runbookLink.trim())
      ? 'Must be a valid http(s) URL'
      : null;
  const tagsError = tags.length > MAX_TAGS ? `Must be ${MAX_TAGS} characters or fewer` : null;
  const ownerError = owner.length > MAX_SHORT_TEXT ? `Must be ${MAX_SHORT_TEXT} characters or fewer` : null;
  const changedByError = changedBy.length > MAX_SHORT_TEXT ? `Must be ${MAX_SHORT_TEXT} characters or fewer` : null;
  const resolutionNotesError =
    resolutionNotes.length > MAX_LONG_TEXT ? `Must be ${MAX_LONG_TEXT} characters or fewer` : null;
  const operationalNotesError =
    operationalNotes.length > MAX_LONG_TEXT ? `Must be ${MAX_LONG_TEXT} characters or fewer` : null;

  const canSave =
    !rootCauseError &&
    !runbookLinkError &&
    !tagsError &&
    !ownerError &&
    !changedByError &&
    !resolutionNotesError &&
    !operationalNotesError &&
    !isDemoMode;

  const handleSave = () => {
    if (!canSave) return;

    upsert.mutate(
      {
        namespaceId,
        signatureHash,
        request: {
          rootCause: rootCause.trim(),
          resolutionNotes: resolutionNotes.trim() || undefined,
          operationalNotes: operationalNotes.trim() || undefined,
          runbookLink: runbookLink.trim() || undefined,
          owner: owner.trim() || undefined,
          replayGuidance: replayGuidance || undefined,
          tags: tags.trim() || undefined,
          reviewDueAt: reviewDueAt ? new Date(reviewDueAt).toISOString() : undefined,
          changedBy: changedBy.trim() || undefined,
        },
      },
      { onSuccess: onClose },
    );
  };

  return createPortal(
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div
        className="bg-white rounded-2xl shadow-2xl w-full max-w-lg max-h-[90vh] flex flex-col mx-4"
        role="dialog"
        aria-modal="true"
        aria-labelledby="knowledge-edit-dialog-title"
      >
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200">
          <h2 id="knowledge-edit-dialog-title" className="text-lg font-bold text-gray-900">
            {knowledge?.rootCause ? 'Edit Knowledge' : 'Add Knowledge'}
          </h2>
          <div className="flex items-center gap-2">
            <button
              onClick={onClose}
              className="px-3 py-1.5 text-sm text-gray-600 border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
            >
              Cancel
            </button>
            <button
              onClick={handleSave}
              disabled={!canSave || upsert.isPending}
              className="px-4 py-1.5 text-sm font-medium text-white rounded-lg bg-primary-500 hover:bg-primary-600 disabled:opacity-50 transition-colors"
            >
              {upsert.isPending ? 'Saving...' : 'Save'}
            </button>
          </div>
        </div>

        <div className="flex-1 overflow-y-auto px-6 py-5 space-y-4">
          {isDemoMode && (
            <div className="flex items-start gap-2 p-3 bg-amber-50 border border-amber-200 rounded-xl text-xs text-amber-800">
              <Info className="w-4 h-4 shrink-0 mt-0.5" />
              <span>Demo Mode — this is a read-only preview with pre-seeded data. Editing is disabled.</span>
            </div>
          )}

          <div>
            <label className="block text-xs font-semibold text-gray-600 uppercase mb-1">Root Cause *</label>
            <textarea
              value={rootCause}
              onChange={(e) => setRootCause(e.target.value)}
              placeholder="What causes this failure?"
              rows={3}
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-primary-500 focus:border-primary-500 resize-none"
            />
            {rootCauseError && <p className="text-xs text-red-600 mt-1">{rootCauseError}</p>}
          </div>

          <div>
            <label className="block text-xs font-semibold text-gray-600 uppercase mb-1">Resolution Notes</label>
            <textarea
              value={resolutionNotes}
              onChange={(e) => setResolutionNotes(e.target.value)}
              placeholder="How was it fixed?"
              rows={3}
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-primary-500 focus:border-primary-500 resize-none"
            />
            {resolutionNotesError && <p className="text-xs text-red-600 mt-1">{resolutionNotesError}</p>}
          </div>

          <div>
            <label className="block text-xs font-semibold text-gray-600 uppercase mb-1">Operational Notes</label>
            <textarea
              value={operationalNotes}
              onChange={(e) => setOperationalNotes(e.target.value)}
              placeholder="Operational context, when it tends to occur, etc."
              rows={2}
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-primary-500 focus:border-primary-500 resize-none"
            />
            {operationalNotesError && <p className="text-xs text-red-600 mt-1">{operationalNotesError}</p>}
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-semibold text-gray-600 uppercase mb-1">Owner</label>
              <input
                type="text"
                value={owner}
                onChange={(e) => setOwner(e.target.value)}
                placeholder="team@example.com"
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-primary-500 focus:border-primary-500"
              />
              {ownerError && <p className="text-xs text-red-600 mt-1">{ownerError}</p>}
            </div>
            <div>
              <label className="block text-xs font-semibold text-gray-600 uppercase mb-1">Replay Guidance</label>
              <select
                value={replayGuidance}
                onChange={(e) => setReplayGuidance(e.target.value as typeof replayGuidance)}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm bg-white focus:ring-2 focus:ring-primary-500 focus:border-primary-500"
              >
                {REPLAY_GUIDANCE_OPTIONS.map((option) => (
                  <option key={option} value={option}>
                    {option || 'Not set'}
                  </option>
                ))}
              </select>
            </div>
          </div>

          <div>
            <label className="block text-xs font-semibold text-gray-600 uppercase mb-1">Runbook Link</label>
            <input
              type="text"
              value={runbookLink}
              onChange={(e) => setRunbookLink(e.target.value)}
              placeholder="https://wiki.example.com/runbooks/..."
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-primary-500 focus:border-primary-500"
            />
            {runbookLinkError && <p className="text-xs text-red-600 mt-1">{runbookLinkError}</p>}
          </div>

          <div>
            <label className="block text-xs font-semibold text-gray-600 uppercase mb-1">Tags (comma-separated)</label>
            <input
              type="text"
              value={tags}
              onChange={(e) => setTags(e.target.value)}
              placeholder="database, timeout, critical"
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-primary-500 focus:border-primary-500"
            />
            {tagsError && <p className="text-xs text-red-600 mt-1">{tagsError}</p>}
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-semibold text-gray-600 uppercase mb-1">Review Due Date</label>
              <input
                type="date"
                value={reviewDueAt}
                onChange={(e) => setReviewDueAt(e.target.value)}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-primary-500 focus:border-primary-500"
              />
            </div>
            <div>
              <label className="block text-xs font-semibold text-gray-600 uppercase mb-1">Changed By (optional)</label>
              <input
                type="text"
                value={changedBy}
                onChange={(e) => setChangedBy(e.target.value)}
                placeholder="your name or email"
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-primary-500 focus:border-primary-500"
              />
              {changedByError && <p className="text-xs text-red-600 mt-1">{changedByError}</p>}
            </div>
          </div>
        </div>
      </div>
    </div>,
    document.body,
  );
}
