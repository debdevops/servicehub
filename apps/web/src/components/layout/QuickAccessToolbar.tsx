import { ArrowLeft, ArrowRight } from 'lucide-react';
import { useLocation, useSearchParams } from 'react-router-dom';
import { useQuickAccessHistory } from '@/hooks/useQuickAccessHistory';
import { resolveWorkspaceLabel } from '@/nav/navigation';

/**
 * Browser-like Back/Forward for every Quick Access destination (Namespace
 * Overview, Incident Center, Fleet Health, Active Messages, Live Tail,
 * Dead-Letter, Scheduled Messages, Cloud Bridge, DLQ Intelligence, Failure
 * Signatures, Auto-Replay Rules, Approval Queue, Autonomy, Proactive
 * Insights, Multi-Cloud Trace, System Health, Audit Trail, Recovery
 * Evidence, Playbook Ledger, Governance, Security & Privacy, Help & Guide,
 * Advanced ServiceHub). Renders nothing outside those routes (e.g.
 * /connect, which sits outside the Quick Access menu). Label lookup comes
 * from the shared nav definition (`@/nav/navigation`), not a
 * hand-maintained copy — roadmap W2.4.
 */
export function QuickAccessToolbar() {
  const location = useLocation();
  const [searchParams] = useSearchParams();
  const { canGoBack, canGoForward, goBack, goForward } = useQuickAccessHistory();

  const label = resolveWorkspaceLabel(location.pathname, searchParams);
  if (!label) return null;

  return (
    <div className="flex items-center gap-1 px-4 py-1.5 border-b border-gray-200 bg-white text-sm shrink-0">
      <button
        type="button"
        onClick={goBack}
        disabled={!canGoBack}
        aria-label="Go back"
        title="Go back"
        className="inline-flex items-center gap-1 px-2 py-1 rounded-md text-gray-600 hover:bg-gray-100 hover:text-gray-900 disabled:opacity-40 disabled:hover:bg-transparent disabled:cursor-not-allowed transition-colors"
      >
        <ArrowLeft className="w-3.5 h-3.5" />
        <span>Back</span>
      </button>
      <button
        type="button"
        onClick={goForward}
        disabled={!canGoForward}
        aria-label="Go forward"
        title="Go forward"
        className="inline-flex items-center gap-1 px-2 py-1 rounded-md text-gray-600 hover:bg-gray-100 hover:text-gray-900 disabled:opacity-40 disabled:hover:bg-transparent disabled:cursor-not-allowed transition-colors"
      >
        <ArrowRight className="w-3.5 h-3.5" />
        <span>Forward</span>
      </button>
      <span className="ml-2 font-medium text-gray-700 truncate">{label}</span>
    </div>
  );
}
