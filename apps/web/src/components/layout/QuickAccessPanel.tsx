import { NavLink, useNavigate } from 'react-router-dom';
import {
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
  Cloud,
  Route,
  Pin,
} from 'lucide-react';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useNamespaceStats } from '@servicehub/ui-shared/hooks/useQueues';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { getMockStats } from '@servicehub/ui-shared/lib/demo/mockProviders';
import { ResizablePanel } from './ResizablePanel';

/**
 * Quick Access — shortcuts to the most-used pages, grouped by workflow stage.
 * Always the first panel: Overview → Browse across clouds → Diagnose & automate →
 * Platform → Support. Collapsible, draggable, and independently resizable.
 */
export function QuickAccessPanel() {
  const navigate = useNavigate();
  const { data: namespaces } = useNamespaces();
  const { isDemoMode, cloudProvider } = useDemoContext();

  const activeNamespace = namespaces?.find((ns) => ns.isActive);
  const demoStats = isDemoMode && cloudProvider ? getMockStats(cloudProvider) : null;

  const allNamespaceIds = isDemoMode ? [] : (namespaces?.map((ns) => ns.id) ?? []);
  const allStatsResults = useNamespaceStats(allNamespaceIds);
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
        <div className="pt-1 pb-0.5 px-1 text-[10px] font-semibold text-gray-400 uppercase tracking-wider">Overview</div>
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
        <div className="pt-2 pb-0.5 px-1 text-[10px] font-semibold text-gray-400 uppercase tracking-wider">Browse across clouds</div>
        <button
          onClick={() => navigate(`${navPrefix}/messages-overview?tab=active`)}
          className="w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all bg-white hover:bg-sky-50 text-gray-700 hover:text-sky-700 border border-gray-200 hover:border-sky-300 shadow-sm"
        >
          <Database className="w-4 h-4 text-sky-500" />
          <span className="flex-1 text-left">Active Messages</span>
          <span className="text-xs text-sky-600 font-medium">All Clouds</span>
        </button>
        <button
          onClick={() => navigate(`${navPrefix}/messages-overview?tab=deadletter`)}
          className="w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all bg-white hover:bg-red-50 text-gray-700 hover:text-red-700 border border-gray-200 hover:border-red-300 shadow-sm"
        >
          <AlertCircle className="w-4 h-4 text-red-500" />
          <span className="flex-1 text-left">Dead-Letter</span>
          <span className="text-xs text-red-600 font-medium">All Clouds</span>
        </button>
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
        <div className="pt-2 pb-0.5 px-1 text-[10px] font-semibold text-gray-400 uppercase tracking-wider">Diagnose &amp; automate</div>
        <NavLink
          to={activeNamespace ? `${navPrefix}/dlq-history?namespace=${activeNamespace.id}` : `${navPrefix}/dlq-history`}
          className="w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all bg-white hover:bg-purple-50 text-gray-700 hover:text-purple-700 border border-gray-200 hover:border-purple-300 shadow-sm"
        >
          <BarChart3 className="w-4 h-4 text-purple-500" />
          <span className="flex-1 text-left">DLQ Intelligence</span>
          <span className="text-xs text-purple-600 font-medium">History</span>
        </NavLink>
        <NavLink
          to={`${navPrefix}/rules`}
          className="w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all bg-white hover:bg-amber-50 text-gray-700 hover:text-amber-700 border border-gray-200 hover:border-amber-300 shadow-sm"
        >
          <Zap className="w-4 h-4 text-amber-500" />
          <span className="flex-1 text-left">Auto-Replay Rules</span>
        </NavLink>
        <NavLink
          to={`${navPrefix}/cross-cloud-trace`}
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

        {/* ── Platform ── */}
        <div className="pt-2 pb-0.5 px-1 text-[10px] font-semibold text-gray-400 uppercase tracking-wider">Platform</div>
        <NavLink
          to={`${navPrefix}/health`}
          className="w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all bg-white hover:bg-emerald-50 text-gray-700 hover:text-emerald-700 border border-gray-200 hover:border-emerald-300 shadow-sm"
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
                ? 'bg-violet-50 text-violet-700 border-violet-300 font-medium'
                : 'bg-white hover:bg-violet-50 text-gray-700 hover:text-violet-700 border-gray-200 hover:border-violet-300'
            }`
          }
        >
          <Shield className="w-4 h-4 text-violet-500" />
          <span className="flex-1 text-left">Audit Trail</span>
          <span className="text-xs text-violet-600 font-medium">Logs</span>
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

        {/* ── Support ── */}
        <div className="pt-2 pb-0.5 px-1 text-[10px] font-semibold text-gray-400 uppercase tracking-wider">Support</div>
        <NavLink
          to={`${navPrefix}/help`}
          className="w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm transition-all bg-white hover:bg-primary-50 text-gray-700 hover:text-primary-700 border border-gray-200 hover:border-primary-300 shadow-sm"
        >
          <HelpCircle className="w-4 h-4 text-primary-500" />
          <span className="flex-1 text-left">Help &amp; Guide</span>
          <span className="text-xs text-primary-600 font-medium">?</span>
        </NavLink>
      </nav>
    </ResizablePanel>
  );
}
