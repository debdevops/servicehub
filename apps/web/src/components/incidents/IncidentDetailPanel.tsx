import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  X,
  Copy,
  Check,
  Lightbulb,
  Eye,
  BookOpen,
  History,
  MoreVertical,
  Zap,
  ExternalLink,
  MessageSquare,
} from 'lucide-react';
import { useDlqSignatureDetail, useRootCauseMatches } from '@servicehub/ui-shared/hooks/useDlqSignatures';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { formatRelativeTime } from '@servicehub/ui-shared/lib/utils';
import { ProviderBadge } from '@servicehub/ui-shared/lib/providerStyles';
import type { Namespace } from '@servicehub/ui-shared/lib/api/types';
import type { IncidentListItem } from '@servicehub/ui-shared/hooks/useIncidentsList';
import { EnvironmentBadge } from '@/components/EnvironmentBadge';
import { StatusBadge, CategoryBadge, RootCauseExplorerPanel } from '@/components/dlq';

const SEVERITY_STYLES: Record<string, { bg: string; text: string; dot: string }> = {
  Critical: { bg: 'bg-red-50', text: 'text-red-700', dot: 'bg-red-500' },
  High: { bg: 'bg-orange-50', text: 'text-orange-700', dot: 'bg-orange-500' },
  Medium: { bg: 'bg-amber-50', text: 'text-amber-700', dot: 'bg-amber-500' },
  Low: { bg: 'bg-blue-50', text: 'text-blue-700', dot: 'bg-blue-500' },
};

export function SeverityBadge({ severity }: { severity: string }) {
  const style = SEVERITY_STYLES[severity] ?? SEVERITY_STYLES.Low;
  return (
    <span className={`inline-flex items-center gap-1.5 px-2 py-1 rounded-full text-xs font-semibold ${style.bg} ${style.text}`}>
      <span className={`w-1.5 h-1.5 rounded-full ${style.dot}`} />
      {severity}
    </span>
  );
}

type PanelTab = 'summary' | 'messages' | 'related';

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString(undefined, { month: 'short', day: 'numeric', year: 'numeric', hour: '2-digit', minute: '2-digit' });
}

/**
 * Incident Center's quick-preview side panel (roadmap: Incident Center redesign, callout 8/9) —
 * a focused view of the currently-selected row for quick investigation, distinct from the full
 * Incident Workspace page. Every action here links out to the page that already owns it
 * (Investigate → Incident Workspace, Add Knowledge → Signature Details, Replay → Incident
 * Workspace's recovery tab, Create Auto-Replay Rule → Rules) rather than duplicating their
 * controls, preserving every action the pre-redesign page exposed.
 */
export function IncidentDetailPanel({
  item,
  namespaces,
  onClose,
}: {
  item: IncidentListItem;
  namespaces?: Namespace[];
  onClose?: () => void;
}) {
  const navigate = useNavigate();
  const { isDemoMode, cloudProvider } = useDemoContext();
  const navPrefix = isDemoMode && cloudProvider ? `/demo/${cloudProvider}` : '';
  const [tab, setTab] = useState<PanelTab>('summary');
  const [menuOpen, setMenuOpen] = useState(false);
  const [copied, setCopied] = useState(false);

  const { data: detail, isLoading: detailLoading } = useDlqSignatureDetail(item.namespaceId, item.signatureHash);
  const { data: rootCause } = useRootCauseMatches(item.namespaceId, item.signatureHash);
  const relatedCount = rootCause?.matches.length ?? 0;
  // The signature's total occurrence count (always exact) — not relatedMessages.length, which
  // can legitimately be 0 or a capped sample even when the signature has many occurrences.
  const messageCount = item.messageCount;
  const trackedMessageCount = detail?.relatedMessages.length ?? 0;

  const topErrorText = detail?.relatedMessages.find((m) => m.deadLetterErrorDescription)?.deadLetterErrorDescription;

  const goInvestigate = () => navigate(`${navPrefix}/incidents/${item.signatureHash}?namespace=${item.namespaceId}`);
  const goKnowledge = () => navigate(`${navPrefix}/signatures/${item.signatureHash}?namespace=${item.namespaceId}`);
  const goReplay = () => navigate(`${navPrefix}/incidents/${item.signatureHash}?namespace=${item.namespaceId}&tab=recovery`);
  const goCreateRule = () => navigate(`${navPrefix}/rules`);

  const copyErrorText = async () => {
    if (!topErrorText) return;
    try {
      await navigator.clipboard.writeText(topErrorText);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch {
      // Clipboard access denied — nothing actionable to recover here.
    }
  };

  return (
    <div className="w-full lg:w-[420px] shrink-0 bg-white border border-gray-200 rounded-xl shadow-sm flex flex-col max-h-[calc(100vh-220px)]">
      {/* Header */}
      <div className="p-4 border-b border-gray-100">
        <div className="flex items-start justify-between gap-2 mb-2">
          <div className="flex items-center gap-2 flex-wrap">
            {item.cloudProvider && <ProviderBadge provider={item.cloudProvider} />}
            <SeverityBadge severity={item.severity} />
            <StatusBadge status={item.status} />
          </div>
          {onClose && (
            <button
              onClick={onClose}
              aria-label="Close incident details"
              className="text-gray-400 hover:text-gray-600 shrink-0"
            >
              <X className="w-4 h-4" />
            </button>
          )}
        </div>
        <h2 className="text-sm font-semibold text-gray-900 leading-snug">{item.displayName}</h2>
        <p className="text-xs text-gray-400 font-mono mt-0.5 break-all">ID: {item.signatureHash}</p>
        <div className="flex flex-wrap gap-x-4 gap-y-0.5 text-xs text-gray-500 mt-2">
          <span>{messageCount} messages affected</span>
          <span>First seen: {formatDate(item.firstSeenAt)}</span>
          <span>Last seen: {formatRelativeTime(new Date(item.lastSeenAt))}</span>
        </div>
      </div>

      {/* Tabs */}
      <div className="flex gap-1 border-b border-gray-100 px-2">
        {([
          ['summary', 'Summary'],
          ['messages', `Messages (${trackedMessageCount})`],
          ['related', `Related (${relatedCount})`],
        ] as const).map(([id, label]) => (
          <button
            key={id}
            onClick={() => setTab(id)}
            className={`px-3 py-2 text-xs font-medium border-b-2 whitespace-nowrap ${
              tab === id ? 'border-primary-600 text-primary-700' : 'border-transparent text-gray-500 hover:text-gray-700'
            }`}
          >
            {label}
          </button>
        ))}
      </div>

      {/* Tab content */}
      <div className="flex-1 overflow-y-auto p-4 space-y-4">
        {tab === 'summary' && (
          <>
            <div>
              <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-1">Description</h3>
              {detailLoading ? (
                <p className="text-sm text-gray-400">Loading…</p>
              ) : (
                <p className="text-sm text-gray-700">{detail?.explanation || 'No description available for this signature yet.'}</p>
              )}
            </div>

            {topErrorText && (
              <div>
                <div className="flex items-center justify-between mb-1">
                  <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wide">Top Error Text</h3>
                  <button onClick={copyErrorText} className="text-gray-400 hover:text-gray-600" aria-label="Copy error text">
                    {copied ? <Check className="w-3.5 h-3.5 text-green-600" /> : <Copy className="w-3.5 h-3.5" />}
                  </button>
                </div>
                <p className="text-sm text-gray-700 bg-gray-50 border border-gray-200 rounded-lg p-2.5 font-mono text-xs break-words">
                  "{topErrorText}"
                </p>
              </div>
            )}

            <div>
              <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-1.5">Affected Entities</h3>
              <div className="flex items-center gap-2 text-sm text-gray-700 bg-gray-50 border border-gray-200 rounded-lg p-2.5">
                <MessageSquare className="w-4 h-4 text-gray-400 shrink-0" />
                <span className="font-medium truncate">{detail?.dominantEntity ?? item.namespaceName ?? '—'}</span>
                <span className="text-gray-300">·</span>
                <span className="text-gray-500 truncate">{item.namespaceName ?? '—'}</span>
                <EnvironmentBadge env={item.environment} />
              </div>
            </div>

            {item.recommendedNextAction && (
              <div>
                <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-1.5">Recommended Action</h3>
                <div className="flex items-start gap-2 text-sm text-amber-800 bg-amber-50 border border-amber-200 rounded-lg p-2.5">
                  <Lightbulb className="w-4 h-4 shrink-0 mt-0.5" />
                  <span>{item.recommendedNextAction}</span>
                </div>
              </div>
            )}

            <div>
              <h3 className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-1.5">Category</h3>
              <CategoryBadge category={item.category} />
            </div>
          </>
        )}

        {tab === 'messages' && (
          <div className="space-y-2">
            {detailLoading ? (
              <p className="text-sm text-gray-400">Loading messages…</p>
            ) : !detail || detail.relatedMessages.length === 0 ? (
              <p className="text-sm text-gray-500">No related messages on file for this signature.</p>
            ) : (
              detail.relatedMessages.map((m) => (
                <div key={m.id} className="border border-gray-200 rounded-lg p-2.5 text-xs">
                  <div className="flex items-center justify-between gap-2 mb-1">
                    <span className="font-mono text-gray-500 truncate">{m.messageId}</span>
                    <span className="text-gray-400 shrink-0">{formatRelativeTime(new Date(m.detectedAtUtc))}</span>
                  </div>
                  <p className="text-gray-700 truncate">{m.deadLetterErrorDescription || m.deadLetterReason || 'No error detail recorded.'}</p>
                </div>
              ))
            )}
          </div>
        )}

        {tab === 'related' && (
          <RootCauseExplorerPanel
            namespaceId={item.namespaceId}
            signatureHash={item.signatureHash}
            dominantDeadletterReason={item.category}
            namespaces={namespaces}
          />
        )}
      </div>

      {/* Actions */}
      <div className="p-3 border-t border-gray-100 space-y-2">
        <div className="flex items-center gap-2">
          <button
            onClick={goInvestigate}
            aria-label="Investigate (detail panel)"
            className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 text-xs font-medium rounded-md bg-primary-600 text-white hover:bg-primary-700 transition-colors"
          >
            <Eye className="w-3.5 h-3.5" />
            Investigate
          </button>
          <button
            onClick={goKnowledge}
            aria-label={`${item.hasKnowledge ? 'Update' : 'Add'} knowledge (detail panel)`}
            className="inline-flex items-center gap-1.5 px-3 py-2 text-xs font-medium rounded-md bg-gray-100 text-gray-700 hover:bg-gray-200 transition-colors whitespace-nowrap"
          >
            <BookOpen className="w-3.5 h-3.5" />
            {item.hasKnowledge ? 'Knowledge' : 'Add Knowledge'}
          </button>
          <button
            onClick={goReplay}
            aria-label="Replay (detail panel)"
            className="inline-flex items-center gap-1.5 px-3 py-2 text-xs font-medium rounded-md bg-amber-50 text-amber-700 hover:bg-amber-100 transition-colors"
          >
            <History className="w-3.5 h-3.5" />
            Replay
          </button>
          <div className="relative">
            <button
              onClick={() => setMenuOpen((v) => !v)}
              aria-label="More actions"
              className="p-2 rounded-md bg-gray-100 text-gray-500 hover:bg-gray-200 transition-colors"
            >
              <MoreVertical className="w-3.5 h-3.5" />
            </button>
            {menuOpen && (
              <>
                <div className="fixed inset-0 z-10" onClick={() => setMenuOpen(false)} />
                <div className="absolute right-0 bottom-full mb-1 w-48 bg-white border border-gray-200 rounded-lg shadow-lg z-20 py-1">
                  <button
                    onClick={() => {
                      setMenuOpen(false);
                      goKnowledge();
                    }}
                    className="w-full text-left px-3 py-1.5 text-xs text-gray-700 hover:bg-gray-50 flex items-center gap-2"
                  >
                    <ExternalLink className="w-3.5 h-3.5" />
                    Open full signature investigation
                  </button>
                </div>
              </>
            )}
          </div>
        </div>
        <button
          onClick={goCreateRule}
          className="w-full inline-flex items-center justify-center gap-1.5 px-3 py-2 text-xs font-semibold rounded-md bg-violet-600 text-white hover:bg-violet-700 transition-colors"
        >
          <Zap className="w-3.5 h-3.5" />
          Create Auto-Replay Rule
        </button>
      </div>
    </div>
  );
}
