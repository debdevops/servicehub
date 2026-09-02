import { Link, useLocation, useSearchParams } from 'react-router-dom';
import { Settings } from 'lucide-react';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { NAV_ENTRIES, isNavEntryActive } from '@/nav/navigation';

const RAIL_ITEMS = NAV_ENTRIES.filter((entry) => entry.quickAccess);
const CONNECT_ENTRY = NAV_ENTRIES.find((entry) => entry.id === 'connect')!;

/**
 * Slim icon-only navigation rail — a compact, always-visible shortcut strip that mirrors
 * Quick Access's routes so the busiest destinations stay one click away even when the
 * Quick Access panel is collapsed. Renders the same shared nav definition
 * (`@/nav/navigation`) Quick Access, the command palette, and the workspace toolbar all read
 * from — no independently-maintained item list here.
 */
export function IconRail() {
  const { isDemoMode, cloudProvider } = useDemoContext();
  const navPrefix = isDemoMode && cloudProvider ? `/demo/${cloudProvider}` : '';
  const location = useLocation();
  const [searchParams] = useSearchParams();

  return (
    <aside className="w-14 shrink-0 bg-white border-r border-gray-200 flex flex-col items-center py-3 gap-1 overflow-y-auto">
      {RAIL_ITEMS.map((entry) => {
        const Icon = entry.icon;
        const active = isNavEntryActive(entry, location.pathname, searchParams);
        return (
          <Link
            key={entry.id}
            to={entry.to({ navPrefix })}
            title={entry.label}
            aria-label={entry.label}
            className={`w-10 h-10 shrink-0 flex items-center justify-center rounded-lg transition-colors ${
              active
                ? 'bg-primary-100 text-primary-600'
                : 'text-gray-400 hover:bg-gray-100 hover:text-primary-600'
            }`}
          >
            <Icon className="w-5 h-5" />
          </Link>
        );
      })}
      <div className="flex-1" />
      <Link
        to="/connect"
        title="Connect"
        aria-label="Connect"
        className={`w-10 h-10 shrink-0 flex items-center justify-center rounded-lg transition-colors ${
          isNavEntryActive(CONNECT_ENTRY, location.pathname, searchParams)
            ? 'bg-primary-100 text-primary-600'
            : 'text-gray-400 hover:bg-gray-100 hover:text-primary-600'
        }`}
      >
        <Settings className="w-5 h-5" />
      </Link>
    </aside>
  );
}
