import { NavLink, useSearchParams } from 'react-router-dom';
import {
  Home,
  Layers,
  LayoutDashboard,
  AlertCircle,
  Clock,
  Database,
  BarChart3,
  Zap,
  Activity,
  HelpCircle,
  Shield,
  ShieldCheck,
  Cloud,
  Route,
  Pin,
  AlertTriangle,
  Radio,
  CheckCircle2,
  Gauge,
  Sparkles,
  ClipboardList,
  Users,
  GraduationCap,
} from 'lucide-react';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useNamespaceStats } from '@servicehub/ui-shared/hooks/useQueues';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { getMockStats } from '@servicehub/ui-shared/lib/demo/mockProviders';
import { ResizablePanel } from './ResizablePanel';

/**
 * Quick Access — shortcuts to the most-used pages, grouped by workflow stage.
 * Always the first panel: Overview → Browse across clouds → Diagnose & automate →
 * Advanced ServiceHub → Platform → Learn ServiceHub → Support. Collapsible, draggable, and
 * independently resizable.
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
        {/* ── Overview ── */}
        <div className="pt-1 pb-0.5 px-1 text-[10px] font-semibold text-gray-500 uppercase tracking-wider">Overview</div>
        <NavLink
          to={`${navPrefix}/home`}
          className={({ isActive }) =>
            `w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all border shadow-sm ${
              isActive
                ? 'bg-primary-50 text-primary-700 border-primary-300 font-medium'
                : 'bg-white hover:bg-primary-50 text-gray-700 hover:text-primary-700 border-gray-200 hover:border-primary-300'
            }`
          }
        >
          <Home className="w-4 h-4 text-primary-500" />
          <span className="flex-1 text-left">Home</span>
        </NavLink>
        <NavLink
          to={`${navPrefix}/dashboard`}
          className={({ isActive }) =>
            `w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all border shadow-sm ${
              isActive
                ? 'bg-indigo-50 text-indigo-700 border-indigo-300 font-medium'
                : 'bg-white hover:bg-indigo-50 text-gray-700 hover:text-indigo-700 border-gray-200 hover:border-indigo-300'
            }`
          }
        >
          <LayoutDashboard className="w-4 h-4 text-indigo-500" />
          <span className="flex-1 text-left">Namespace Overview</span>
          {totalDlqCount > 0 && (
            <span className="text-xs bg-red-100 text-red-700 px-1.5 py-0.5 rounded-full font-medium">
              {totalDlqCount}
            </span>
          )}
        </NavLink>
        <NavLink
          to={`${navPrefix}/incidents`}
          className={({ isActive }) =>
            `w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all border shadow-sm ${
              isActive
                ? 'bg-red-50 text-red-700 border-red-300 font-medium'
                : 'bg-white hover:bg-red-50 text-gray-700 hover:text-red-700 border-gray-200 hover:border-red-300'
            }`
          }
        >
          <AlertTriangle className="w-4 h-4 text-red-500" />
          <span className="flex-1 text-left">Incident Center</span>
          <span className="text-xs text-red-600 font-medium">Ops</span>
        </NavLink>
        <NavLink
          to={`${navPrefix}/fleet`}
          className={({ isActive }) =>
            `w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all border shadow-sm ${
              isActive
                ? 'bg-indigo-50 text-indigo-700 border-indigo-300 font-medium'
                : 'bg-white hover:bg-indigo-50 text-gray-700 hover:text-indigo-700 border-gray-200 hover:border-indigo-300'
            }`
          }
        >
          <Layers className="w-4 h-4 text-indigo-500" />
          <span className="flex-1 text-left">Fleet Health</span>
          <span className="text-xs text-indigo-600 font-medium">All NS</span>
        </NavLink>

        {/* ── Browse across clouds ── */}
        <div className="pt-2 pb-0.5 px-1 text-[10px] font-semibold text-gray-500 uppercase tracking-wider">Browse across clouds</div>
        <NavLink
          to={`${navPrefix}/messages-overview?tab=active`}
          className={({ isActive }) => {
            const isExactMatch = isActive && new URLSearchParams(window.location.search).get('tab') === 'active';
            return `w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all border shadow-sm ${
              isExactMatch
                ? 'bg-sky-50 text-sky-700 border-sky-300 font-medium'
                : 'bg-white hover:bg-sky-50 text-gray-700 hover:text-sky-700 border-gray-200 hover:border-sky-300'
            }`;
          }}
        >
          <Database className="w-4 h-4 text-sky-500" />
          <span className="flex-1 text-left">Active Messages</span>
          <span className="text-xs text-sky-700 font-medium">{browseAllLabel}</span>
        </NavLink>
        <NavLink
          to={currentNamespace ? `${navPrefix}/live-tail?namespace=${currentNamespace.id}` : `${navPrefix}/live-tail`}
          className={({ isActive }) =>
            `w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all border shadow-sm ${
              isActive
                ? 'bg-emerald-50 text-emerald-700 border-emerald-300 font-medium'
                : 'bg-white hover:bg-emerald-50 text-gray-700 hover:text-emerald-700 border-gray-200 hover:border-emerald-300'
            }`
          }
        >
          <Radio className="w-4 h-4 text-emerald-500" />
          <span className="flex-1 text-left">Live Tail</span>
        </NavLink>
        <NavLink
          to={`${navPrefix}/messages-overview?tab=deadletter`}
          className={({ isActive }) => {
            const isExactMatch = isActive && new URLSearchParams(window.location.search).get('tab') === 'deadletter';
            return `w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all border shadow-sm ${
              isExactMatch
                ? 'bg-red-50 text-red-700 border-red-300 font-medium'
                : 'bg-white hover:bg-red-50 text-gray-700 hover:text-red-700 border-gray-200 hover:border-red-300'
            }`;
          }}
        >
          <AlertCircle className="w-4 h-4 text-red-500" />
          <span className="flex-1 text-left">Dead-Letter</span>
          <span className="text-xs text-red-600 font-medium">{browseAllLabel}</span>
        </NavLink>
        <NavLink
          to={`${navPrefix}/scheduled`}
          className={({ isActive }) =>
            `w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all border shadow-sm ${
              isActive
                ? 'bg-sky-50 text-sky-700 border-sky-300 font-medium'
                : 'bg-white hover:bg-sky-50 text-gray-700 hover:text-sky-700 border-gray-200 hover:border-sky-300'
            }`
          }
        >
          <Clock className="w-4 h-4 text-sky-500" />
          <span className="flex-1 text-left">Scheduled Messages</span>
        </NavLink>
        <NavLink
          to={`${navPrefix}/cloud-bridge`}
          className={({ isActive }) =>
            `w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all border shadow-sm ${
              isActive
                ? 'bg-blue-50 text-blue-700 border-blue-300'
                : 'bg-white hover:bg-blue-50 text-gray-700 hover:text-blue-700 border-gray-200 hover:border-blue-300'
            }`
          }
        >
          <Cloud className="w-4 h-4 text-blue-500" />
          <span className="flex-1 text-left">Cloud Bridge</span>
        </NavLink>

        {/* ── Diagnose & automate ── */}
        <div className="pt-2 pb-0.5 px-1 text-[10px] font-semibold text-gray-500 uppercase tracking-wider">Diagnose &amp; automate</div>
        <NavLink
          to={currentNamespace ? `${navPrefix}/dlq-history?namespace=${currentNamespace.id}` : `${navPrefix}/dlq-history`}
          className={({ isActive }) =>
            `w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all border shadow-sm ${
              isActive
                ? 'bg-purple-50 text-purple-700 border-purple-300 font-medium'
                : 'bg-white hover:bg-purple-50 text-gray-700 hover:text-purple-700 border-gray-200 hover:border-purple-300'
            }`
          }
        >
          <BarChart3 className="w-4 h-4 text-purple-500" />
          <span className="flex-1 text-left">DLQ Intelligence</span>
          <span className="text-xs text-purple-600 font-medium">History</span>
        </NavLink>
        <NavLink
          to={`${navPrefix}/rules`}
          className={({ isActive }) =>
            `w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all border shadow-sm ${
              isActive
                ? 'bg-amber-50 text-amber-700 border-amber-300 font-medium'
                : 'bg-white hover:bg-amber-50 text-gray-700 hover:text-amber-700 border-gray-200 hover:border-amber-300'
            }`
          }
        >
          <Zap className="w-4 h-4 text-amber-500" />
          <span className="flex-1 text-left">Auto-Replay Rules</span>
        </NavLink>
        <NavLink
          to={`${navPrefix}/approval-queue`}
          className={({ isActive }) =>
            `w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all border shadow-sm ${
              isActive
                ? 'bg-amber-50 text-amber-700 border-amber-300 font-medium'
                : 'bg-white hover:bg-amber-50 text-gray-700 hover:text-amber-700 border-gray-200 hover:border-amber-300'
            }`
          }
        >
          <CheckCircle2 className="w-4 h-4 text-amber-500" />
          <span className="flex-1 text-left">Approval Queue</span>
        </NavLink>
        <NavLink
          to={`${navPrefix}/insights`}
          className={({ isActive }) =>
            `w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all border shadow-sm ${
              isActive
                ? 'bg-blue-50 text-blue-700 border-blue-300 font-medium'
                : 'bg-white hover:bg-blue-50 text-gray-700 hover:text-blue-700 border-gray-200 hover:border-blue-300'
            }`
          }
        >
          <Sparkles className="w-4 h-4 text-blue-500" />
          <span className="flex-1 text-left">Proactive Insights</span>
        </NavLink>
        <NavLink
          to={`${navPrefix}/cross-cloud-trace`}
          title={isMultiCloud ? undefined : 'Needs at least two connected providers to trace a cross-cloud hop'}
          className={({ isActive }) =>
            `w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all border shadow-sm ${
              isActive
                ? 'bg-violet-50 text-violet-700 border-violet-300 font-medium'
                : 'bg-white hover:bg-violet-50 text-gray-700 hover:text-violet-700 border-gray-200 hover:border-violet-300'
            }`
          }
        >
          <Route className="w-4 h-4 text-violet-500" />
          <span className="flex-1 text-left">Multi-Cloud Trace</span>
        </NavLink>

        {/* ── Advanced ServiceHub ── */}
        <div className="pt-2 pb-0.5 px-1 text-[10px] font-semibold text-gray-500 uppercase tracking-wider">Advanced ServiceHub</div>
        <NavLink
          to={`${navPrefix}/autonomy`}
          className={({ isActive }) =>
            `w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all border shadow-sm ${
              isActive
                ? 'bg-blue-50 text-blue-700 border-blue-300 font-medium'
                : 'bg-white hover:bg-blue-50 text-gray-700 hover:text-blue-700 border-gray-200 hover:border-blue-300'
            }`
          }
        >
          <Gauge className="w-4 h-4 text-blue-500" />
          <span className="flex-1 text-left">Autonomy</span>
        </NavLink>
        <NavLink
          to={`${navPrefix}/recovery`}
          className={({ isActive }) =>
            `w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all border shadow-sm ${
              isActive
                ? 'bg-teal-50 text-teal-700 border-teal-300 font-medium'
                : 'bg-white hover:bg-teal-50 text-gray-700 hover:text-teal-700 border-gray-200 hover:border-teal-300'
            }`
          }
        >
          <ShieldCheck className="w-4 h-4 text-teal-500" />
          <span className="flex-1 text-left">Recovery Evidence</span>
        </NavLink>
        <NavLink
          to={`${navPrefix}/playbook`}
          className={({ isActive }) =>
            `w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all border shadow-sm ${
              isActive
                ? 'bg-indigo-50 text-indigo-700 border-indigo-300 font-medium'
                : 'bg-white hover:bg-indigo-50 text-gray-700 hover:text-indigo-700 border-gray-200 hover:border-indigo-300'
            }`
          }
        >
          <ClipboardList className="w-4 h-4 text-indigo-500" />
          <span className="flex-1 text-left">Playbook Ledger</span>
        </NavLink>
        <NavLink
          to={`${navPrefix}/governance`}
          className={({ isActive }) =>
            `w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all border shadow-sm ${
              isActive
                ? 'bg-red-50 text-red-700 border-red-300 font-medium'
                : 'bg-white hover:bg-red-50 text-gray-700 hover:text-red-700 border-gray-200 hover:border-red-300'
            }`
          }
        >
          <Users className="w-4 h-4 text-red-500" />
          <span className="flex-1 text-left">Governance</span>
        </NavLink>

        {/* ── Platform ── */}
        <div className="pt-2 pb-0.5 px-1 text-[10px] font-semibold text-gray-500 uppercase tracking-wider">Platform</div>
        <NavLink
          to={`${navPrefix}/health`}
          className={({ isActive }) =>
            `w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all border shadow-sm ${
              isActive
                ? 'bg-emerald-50 text-emerald-700 border-emerald-300 font-medium'
                : 'bg-white hover:bg-emerald-50 text-gray-700 hover:text-emerald-700 border-gray-200 hover:border-emerald-300'
            }`
          }
        >
          <Activity className="w-4 h-4 text-emerald-500" />
          <span className="flex-1 text-left">System Health</span>
          <span className="text-xs text-emerald-600 font-medium">Status</span>
        </NavLink>
        <NavLink
          to={`${navPrefix}/audit`}
          className={({ isActive }) =>
            `w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all border shadow-sm ${
              isActive
                ? 'bg-primary-50 text-primary-700 border-primary-300 font-medium'
                : 'bg-white hover:bg-primary-50 text-gray-700 hover:text-primary-700 border-gray-200 hover:border-primary-300'
            }`
          }
        >
          <Shield className="w-4 h-4 text-primary-500" />
          <span className="flex-1 text-left">Audit Trail</span>
          <span className="text-xs text-primary-600 font-medium">Logs</span>
        </NavLink>
        <NavLink
          to={`${navPrefix}/security`}
          className={({ isActive }) =>
            `w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all border shadow-sm ${
              isActive
                ? 'bg-green-50 text-green-700 border-green-300'
                : 'bg-white hover:bg-green-50 text-gray-700 hover:text-green-700 border-gray-200 hover:border-green-300'
            }`
          }
        >
          <Shield className="w-4 h-4 text-green-500" />
          <span className="flex-1 text-left">Security &amp; Privacy</span>
        </NavLink>

        {/* ── Learn ServiceHub ── */}
        <div className="pt-2 pb-0.5 px-1 text-[10px] font-semibold text-gray-500 uppercase tracking-wider">Learn ServiceHub</div>
        <NavLink
          to={`${navPrefix}/advanced-servicehub`}
          className={({ isActive }) =>
            `w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all border shadow-sm ${
              isActive
                ? 'bg-indigo-50 text-indigo-700 border-indigo-300 font-medium'
                : 'bg-white hover:bg-indigo-50 text-gray-700 hover:text-indigo-700 border-gray-200 hover:border-indigo-300'
            }`
          }
        >
          <GraduationCap className="w-4 h-4 text-indigo-500" />
          <span className="flex-1 text-left">Advanced ServiceHub</span>
        </NavLink>

        {/* ── Support ── */}
        <div className="pt-2 pb-0.5 px-1 text-[10px] font-semibold text-gray-500 uppercase tracking-wider">Support</div>
        <NavLink
          to={`${navPrefix}/help`}
          className={({ isActive }) =>
            `w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all border shadow-sm ${
              isActive
                ? 'bg-primary-50 text-primary-700 border-primary-300 font-medium'
                : 'bg-white hover:bg-primary-50 text-gray-700 hover:text-primary-700 border-gray-200 hover:border-primary-300'
            }`
          }
        >
          <HelpCircle className="w-4 h-4 text-primary-500" />
          <span className="flex-1 text-left">Help &amp; Guide</span>
          <span className="text-xs text-primary-600 font-medium">?</span>
        </NavLink>
      </nav>
    </ResizablePanel>
  );
}
