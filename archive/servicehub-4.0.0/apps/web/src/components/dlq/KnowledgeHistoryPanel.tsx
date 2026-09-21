import { History } from 'lucide-react';
import { useKnowledgeHistory } from '@servicehub/ui-shared/hooks/useDlqSignatures';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';

interface KnowledgeHistoryPanelProps {
  namespaceId: string;
  signatureHash: string;
}

function formatTimestamp(ts: string): string {
  return new Date(ts).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

/** Prior versions of a failure signature's operational knowledge, most recent first. */
export function KnowledgeHistoryPanel({ namespaceId, signatureHash }: KnowledgeHistoryPanelProps) {
  const { isDemoMode } = useDemoContext();
  const { data: history, isLoading } = useKnowledgeHistory(namespaceId, signatureHash);

  if (isDemoMode) {
    return <p className="text-sm text-gray-500">Version history isn&apos;t available in Demo Mode.</p>;
  }

  if (isLoading) {
    return <p className="text-sm text-gray-500">Loading history…</p>;
  }

  if (!history || history.length === 0) {
    return <p className="text-sm text-gray-500">No prior versions — this is the first recorded version.</p>;
  }

  return (
    <div className="space-y-3">
      {history.map((entry) => (
        <div key={entry.knowledgeVersion} className="border border-gray-200 rounded-lg p-3 bg-gray-50">
          <div className="flex items-center justify-between gap-2 mb-1.5">
            <span className="inline-flex items-center gap-1 text-xs font-semibold text-gray-600">
              <History className="w-3.5 h-3.5" />
              Version {entry.knowledgeVersion}
            </span>
            <span className="text-xs text-gray-400" title={formatTimestamp(entry.updatedAt)}>
              {formatTimestamp(entry.updatedAt)}
            </span>
          </div>
          <p className="text-xs text-gray-500 mb-2">
            Changed by: <span className="text-gray-700">{entry.updatedBy || 'Not provided'}</span>
          </p>
          {entry.rootCause && (
            <div className="mb-1.5">
              <span className="text-xs font-semibold text-gray-600 uppercase">Root Cause</span>
              <p className="text-sm text-gray-800">{entry.rootCause}</p>
            </div>
          )}
          {entry.resolutionNotes && (
            <div>
              <span className="text-xs font-semibold text-gray-600 uppercase">Resolution Notes</span>
              <p className="text-sm text-gray-800">{entry.resolutionNotes}</p>
            </div>
          )}
        </div>
      ))}
    </div>
  );
}
