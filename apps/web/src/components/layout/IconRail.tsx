import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { Link, useLocation, useSearchParams } from 'react-router-dom';
import { Settings, MoreHorizontal } from 'lucide-react';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { NAV_ENTRIES, isNavEntryActive, type NavGroup } from '@/nav/navigation';

// Same section order Quick Access groups under (see navigation.ts) — the operator's actual loop:
// Overview shows what needs attention, Observe explains it, Recover acts on it, Autonomous
// ServiceHub answers what ServiceHub may do on its own, Platform is ServiceHub's own health,
// Support is help.
const GROUP_ORDER: NavGroup[] = ['Overview', 'Observe', 'Recover', 'Autonomous ServiceHub', 'Platform', 'Support'];

/** Every Quick Access destination, in that same order — the rail no longer hand-picks a fixed
 * five; it shows as many as fit (see below) and only folds the tail behind "More". */
const RAIL_ITEMS = NAV_ENTRIES
  .filter((entry) => entry.quickAccess)
  .sort((a, b) => GROUP_ORDER.indexOf(a.quickAccess!.group) - GROUP_ORDER.indexOf(b.quickAccess!.group));

const CONNECT_ENTRY = NAV_ENTRIES.find((entry) => entry.id === 'connect')!;

// 40px button + 4px gap (gap-1).
const ITEM_HEIGHT = 44;

/**
 * Icon-only navigation rail. Renders every Quick Access destination as its own icon and only
 * folds the overflow behind a "More" button once they genuinely no longer fit the viewport's
 * height (measured live via ResizeObserver on the icon list itself) — a tall window shows every
 * icon with no "..." at all; a short one shows as many as it can and folds the rest into a
 * popover, never a silently-clipped scroll area.
 */
export function IconRail() {
  const { isDemoMode, cloudProvider } = useDemoContext();
  const navPrefix = isDemoMode && cloudProvider ? `/demo/${cloudProvider}` : '';
  const location = useLocation();
  const [searchParams] = useSearchParams();
  const listRef = useRef<HTMLDivElement>(null);
  const moreRef = useRef<HTMLDivElement>(null);
  const [visibleCount, setVisibleCount] = useState(RAIL_ITEMS.length);
  const [moreOpen, setMoreOpen] = useState(false);

  useLayoutEffect(() => {
    const el = listRef.current;
    if (!el) return;

    const compute = () => {
      const available = el.clientHeight;
      // A height of 0 means "not yet laid out" (or a test environment that never lays out at
      // all, like jsdom) rather than "no room" — collapsing to one icon in that case would be
      // worse than just showing everything until a real measurement comes in.
      if (available <= 0) return;
      const maxFit = Math.max(1, Math.floor(available / ITEM_HEIGHT));
      setVisibleCount(maxFit >= RAIL_ITEMS.length ? RAIL_ITEMS.length : Math.max(1, maxFit - 1));
    };

    compute();
    const observer = new ResizeObserver(compute);
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    if (!moreOpen) return;
    const handleClick = (e: MouseEvent) => {
      if (moreRef.current && !moreRef.current.contains(e.target as Node)) setMoreOpen(false);
    };
    const handleEscape = (e: KeyboardEvent) => { if (e.key === 'Escape') setMoreOpen(false); };
    document.addEventListener('mousedown', handleClick);
    document.addEventListener('keydown', handleEscape);
    return () => {
      document.removeEventListener('mousedown', handleClick);
      document.removeEventListener('keydown', handleEscape);
    };
  }, [moreOpen]);

  const visibleItems = RAIL_ITEMS.slice(0, visibleCount);
  const overflowItems = RAIL_ITEMS.slice(visibleCount);

  return (
    <aside className="w-14 shrink-0 bg-white border-r border-gray-200 flex flex-col items-center py-3 gap-1">
      <div ref={listRef} className="flex-1 min-h-0 w-full flex flex-col items-center gap-1 overflow-hidden">
        {visibleItems.map((entry) => {
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
      </div>

      {overflowItems.length > 0 && (
        <div className="relative shrink-0" ref={moreRef}>
          <button
            type="button"
            onClick={() => setMoreOpen((v) => !v)}
            title="More destinations"
            aria-label="More destinations"
            aria-haspopup="menu"
            aria-expanded={moreOpen}
            className={`w-10 h-10 shrink-0 flex items-center justify-center rounded-lg transition-colors ${
              moreOpen ? 'bg-gray-100 text-primary-600' : 'text-gray-400 hover:bg-gray-100 hover:text-primary-600'
            }`}
          >
            <MoreHorizontal className="w-5 h-5" />
          </button>
          {moreOpen && (
            <div
              role="menu"
              className="absolute left-full bottom-0 ml-1 z-20 w-56 bg-white border border-gray-200 rounded-lg shadow-lg py-1 max-h-[70vh] overflow-y-auto"
            >
              {overflowItems.map((entry) => {
                const Icon = entry.icon;
                const active = isNavEntryActive(entry, location.pathname, searchParams);
                return (
                  <Link
                    key={entry.id}
                    to={entry.to({ navPrefix })}
                    role="menuitem"
                    onClick={() => setMoreOpen(false)}
                    className={`flex items-center gap-2.5 px-3 py-2 text-sm transition-colors ${
                      active ? 'bg-primary-50 text-primary-700 font-medium' : 'text-gray-700 hover:bg-gray-50'
                    }`}
                  >
                    <Icon className="w-4 h-4 shrink-0 text-gray-400" />
                    <span className="truncate">{entry.label}</span>
                  </Link>
                );
              })}
            </div>
          )}
        </div>
      )}

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
