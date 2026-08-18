import { ArrowLeft, ArrowRight } from 'lucide-react';
import { useLocation, useSearchParams } from 'react-router-dom';
import { useQuickAccessHistory } from '@/hooks/useQuickAccessHistory';

const WORKSPACE_BASE_PATHS = new Set(['messages-overview', 'messages', 'live-tail', 'scheduled']);

function getWorkspaceLabel(pathname: string, searchParams: URLSearchParams): string | null {
  // Strip an optional /demo/{provider} prefix, same convention as QuickAccessPanel's navPrefix.
  const segments = pathname.split('/').filter(Boolean);
  const basePath = segments[0] === 'demo' ? segments[2] : segments[0];

  if (!basePath || !WORKSPACE_BASE_PATHS.has(basePath)) return null;

  switch (basePath) {
    case 'messages-overview':
      return searchParams.get('tab') === 'deadletter' ? 'Dead-Letter' : 'Active Messages';
    case 'messages':
      return searchParams.get('queueType') === 'deadletter' ? 'Dead-Letter' : 'Active Messages';
    case 'live-tail':
      return 'Live Tail';
    case 'scheduled':
      return 'Scheduled Messages';
    default:
      return null;
  }
}

/**
 * Browser-like Back/Forward for the Quick Access workspace area (Active
 * Messages, Live Tail, Dead-Letter, Scheduled Messages). Renders nothing
 * outside those routes.
 */
export function QuickAccessToolbar() {
  const location = useLocation();
  const [searchParams] = useSearchParams();
  const { canGoBack, canGoForward, goBack, goForward } = useQuickAccessHistory();

  const label = getWorkspaceLabel(location.pathname, searchParams);
  if (!label) return null;

  return (
    <div className="flex items-center gap-1 px-4 py-2 border-b border-gray-200 bg-white text-sm shrink-0">
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
