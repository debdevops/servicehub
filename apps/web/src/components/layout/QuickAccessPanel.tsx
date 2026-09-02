import type { ReactNode } from 'react';
import { Link, useLocation, useSearchParams } from 'react-router-dom';
import { Pin } from 'lucide-react';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useNamespaceStats } from '@servicehub/ui-shared/hooks/useQueues';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { getMockStats } from '@servicehub/ui-shared/lib/demo/mockProviders';
import { NAV_ENTRIES, isNavEntryActive, type NavColor, type NavEntry, type NavGroup } from '@/nav/navigation';
import { ResizablePanel } from './ResizablePanel';

// Section order — every entry's `quickAccess.group` must be one of these, in this order.
const GROUP_ORDER: NavGroup[] = [
  'Overview',
  'Browse across clouds',
  'Diagnose & automate',
  'Advanced ServiceHub',
  'Platform',
  'Learn ServiceHub',
  'Support',
];

// Literal, fully-spelled Tailwind classes — Tailwind's JIT scanner only picks up class names
// that appear verbatim in source, so these cannot be built by string interpolation.
const COLOR_STYLES: Record<NavColor, { icon: string; active: string; inactive: string }> = {
  primary: {
    icon: 'text-primary-500',
    active: 'bg-primary-50 text-primary-700 border-primary-300 font-medium',
    inactive: 'bg-white hover:bg-primary-50 text-gray-700 hover:text-primary-700 border-gray-200 hover:border-primary-300',
  },
  indigo: {
    icon: 'text-indigo-500',
    active: 'bg-indigo-50 text-indigo-700 border-indigo-300 font-medium',
    inactive: 'bg-white hover:bg-indigo-50 text-gray-700 hover:text-indigo-700 border-gray-200 hover:border-indigo-300',
  },
  red: {
    icon: 'text-red-500',
    active: 'bg-red-50 text-red-700 border-red-300 font-medium',
    inactive: 'bg-white hover:bg-red-50 text-gray-700 hover:text-red-700 border-gray-200 hover:border-red-300',
  },
  sky: {
    icon: 'text-sky-500',
    active: 'bg-sky-50 text-sky-700 border-sky-300 font-medium',
    inactive: 'bg-white hover:bg-sky-50 text-gray-700 hover:text-sky-700 border-gray-200 hover:border-sky-300',
  },
  emerald: {
    icon: 'text-emerald-500',
    active: 'bg-emerald-50 text-emerald-700 border-emerald-300 font-medium',
    inactive: 'bg-white hover:bg-emerald-50 text-gray-700 hover:text-emerald-700 border-gray-200 hover:border-emerald-300',
  },
  blue: {
    icon: 'text-blue-500',
    active: 'bg-blue-50 text-blue-700 border-blue-300 font-medium',
    inactive: 'bg-white hover:bg-blue-50 text-gray-700 hover:text-blue-700 border-gray-200 hover:border-blue-300',
  },
  purple: {
    icon: 'text-purple-500',
    active: 'bg-purple-50 text-purple-700 border-purple-300 font-medium',
    inactive: 'bg-white hover:bg-purple-50 text-gray-700 hover:text-purple-700 border-gray-200 hover:border-purple-300',
  },
  amber: {
    icon: 'text-amber-500',
    active: 'bg-amber-50 text-amber-700 border-amber-300 font-medium',
    inactive: 'bg-white hover:bg-amber-50 text-gray-700 hover:text-amber-700 border-gray-200 hover:border-amber-300',
  },
  violet: {
    icon: 'text-violet-500',
    active: 'bg-violet-50 text-violet-700 border-violet-300 font-medium',
    inactive: 'bg-white hover:bg-violet-50 text-gray-700 hover:text-violet-700 border-gray-200 hover:border-violet-300',
  },
  teal: {
    icon: 'text-teal-500',
    active: 'bg-teal-50 text-teal-700 border-teal-300 font-medium',
    inactive: 'bg-white hover:bg-teal-50 text-gray-700 hover:text-teal-700 border-gray-200 hover:border-teal-300',
  },
  green: {
    icon: 'text-green-500',
    active: 'bg-green-50 text-green-700 border-green-300 font-medium',
    inactive: 'bg-white hover:bg-green-50 text-gray-700 hover:text-green-700 border-gray-200 hover:border-green-300',
  },
};

// A handful of entries carry a small trailing badge that isn't part of the shared nav
// definition (it's Quick Access's own visual chrome, not a route/label/icon every surface
// needs). Static text lives here; the two that depend on live data (the DLQ count, and
// "All Namespaces" vs "All Clouds") are computed by the component below and passed in.
const STATIC_BADGES: Record<string, { text: string; className: string }> = {
  incidents: { text: 'Ops', className: 'text-xs text-red-600 font-medium' },
  fleet: { text: 'All NS', className: 'text-xs text-indigo-600 font-medium' },
  'dlq-history': { text: 'History', className: 'text-xs text-purple-600 font-medium' },
  health: { text: 'Status', className: 'text-xs text-emerald-600 font-medium' },
  audit: { text: 'Logs', className: 'text-xs text-primary-600 font-medium' },
  help: { text: '?', className: 'text-xs text-primary-600 font-medium' },
};

function renderBadge(entry: NavEntry, totalDlqCount: number, browseAllLabel: string): ReactNode {
  if (entry.id === 'dashboard') {
    return totalDlqCount > 0 ? (
      <span className="text-xs bg-red-100 text-red-700 px-1.5 py-0.5 rounded-full font-medium">{totalDlqCount}</span>
    ) : null;
  }
  if (entry.id === 'messages-active') {
    return <span className="text-xs text-sky-700 font-medium">{browseAllLabel}</span>;
  }
  if (entry.id === 'messages-deadletter') {
    return <span className="text-xs text-red-600 font-medium">{browseAllLabel}</span>;
  }
  const staticBadge = STATIC_BADGES[entry.id];
  return staticBadge ? <span className={staticBadge.className}>{staticBadge.text}</span> : null;
}

const QUICK_ACCESS_ENTRIES = NAV_ENTRIES.filter((entry) => entry.quickAccess);

/**
 * Quick Access — shortcuts to the most-used pages, grouped by workflow stage. Renders the same
 * shared nav definition (`@/nav/navigation`) Icon Rail, the command palette, and the workspace
 * toolbar all read from — no independently-maintained item list here (roadmap W2.4).
 *
 * Sections, in order: Overview → Browse across clouds → Diagnose & automate → Advanced
 * ServiceHub → Platform → Learn ServiceHub → Support. Collapsible, draggable, and independently
 * resizable.
 *
 * Advanced ServiceHub groups the pages that explain and govern ServiceHub's own autonomy —
 * Autonomy (the "how autonomous is this, and why" page), the Recovery and Playbook ledgers, and
 * Governance/RBAC — separate from the daily Diagnose & automate loop (DLQ Intelligence,
 * Auto-Replay Rules, Approval Queue, Proactive Insights, Multi-Cloud Trace), which stays put
 * since Approval Queue is a live, time-sensitive queue tied directly to Auto-Replay Rules, not a
 * governance surface.
 *
 * Learn ServiceHub is deliberately a separate, single-item section, not folded into Advanced
 * ServiceHub or Support: it's neither an operational feature (Advanced ServiceHub's four pages
 * all do something) nor a support resource (Help & Guide answers "how do I do X"). It's pure
 * architecture explanation — "what is Advanced ServiceHub and why does it exist" — so it gets its
 * own section, placed directly above Support since that's the other "understand the product"
 * destination.
 */
export function QuickAccessPanel() {
  const location = useLocation();
  const [searchParams] = useSearchParams();
  const { data: namespaces } = useNamespaces();
  const { isDemoMode, cloudProvider } = useDemoContext();

  // The namespace the operator is currently viewing — read from the URL, the single
  // source of truth every other namespace-aware component (MainLayout, Header,
  // MessagesPage, LiveTailPage, ...) already uses. `isActive` on a namespace means
  // "this connection is enabled", not "currently selected" — every namespace has it
  // set, so using it here previously meant "whichever namespace is first in the list".
  const currentNamespaceId = searchParams.get('namespace');
  const currentNamespace = namespaces?.find((ns) => ns.id === currentNamespaceId);
  const demoStats = isDemoMode && cloudProvider ? getMockStats(cloudProvider) : null;

  // "All Clouds" is misleading on a single-provider installation — reads as though
  // Azure/AWS/GCP data the operator doesn't have is somehow included. Swap to
  // "All Namespaces" whenever fewer than 2 distinct providers are actually configured.
  const configuredProviderCount = new Set((namespaces ?? []).map((ns) => ns.cloudProvider).filter(Boolean)).size;
  const isMultiCloud = configuredProviderCount > 1;
  const browseAllLabel = isMultiCloud ? 'All Clouds' : 'All Namespaces';

  const allNamespaceIds = isDemoMode ? [] : (namespaces?.map((ns) => ns.id) ?? []);
  // autoRefresh: false — Header renders the same fleet-wide dead-letter total from the same
  // ['namespace-stats', id] cache entries and is mounted on every page alongside this panel.
  // Both registering a refetch interval made two always-mounted components each own a poll
  // cadence for one number; Header is the single owner and this panel reads what it warms.
  // Each entry is a live cloud-provider call, so the cadence is real API spend, not just a
  // local counter.
  const allStatsResults = useNamespaceStats(allNamespaceIds, false);
  const totalDlqCount = demoStats
    ? demoStats.totalDlq
    : allStatsResults.reduce((total, result) => {
        if (!result.data) return total;
        return total + result.data.totalDlq;
      }, 0);

  const navPrefix = isDemoMode && cloudProvider ? `/demo/${cloudProvider}` : '';
  const linkCtx = { navPrefix, currentNamespaceId: currentNamespace?.id };

  return (
    <ResizablePanel
      panelId="quick-access"
      title="Quick Access"
      icon={<Pin className="w-3.5 h-3.5 text-primary-500 shrink-0" />}
      defaultWidth={280}
      minWidth={220}
      maxWidth={420}
      narrowBreakpoint={1024}
      dataTour="quick-access"
    >
      <nav className="space-y-1 px-3 py-3">
        {GROUP_ORDER.map((group) => {
          const entries = QUICK_ACCESS_ENTRIES.filter((entry) => entry.quickAccess!.group === group);
          if (entries.length === 0) return null;
          return (
            <div key={group}>
              <div className="pt-1 pb-0.5 px-1 text-[10px] font-semibold text-gray-500 uppercase tracking-wider">{group}</div>
              {entries.map((entry) => {
                const Icon = entry.icon;
                const { icon, active, inactive } = COLOR_STYLES[entry.quickAccess!.color];
                const isActive = isNavEntryActive(entry, location.pathname, searchParams);
                const title =
                  entry.id === 'cross-cloud-trace' && !isMultiCloud
                    ? 'Needs at least two connected providers to trace a cross-cloud hop'
                    : undefined;
                return (
                  <Link
                    key={entry.id}
                    to={entry.to(linkCtx)}
                    title={title}
                    className={`w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all border shadow-sm ${
                      isActive ? active : inactive
                    }`}
                  >
                    <Icon className={`w-4 h-4 ${icon}`} />
                    <span className="flex-1 text-left">{entry.label}</span>
                    {renderBadge(entry, totalDlqCount, browseAllLabel)}
                  </Link>
                );
              })}
            </div>
          );
        })}
      </nav>
    </ResizablePanel>
  );
}
