import { NavLink } from 'react-router-dom';
import {
  LayoutDashboard,
  Layers,
  Database,
  AlertCircle,
  Clock,
  Cloud,
  BarChart3,
  Zap,
  Route,
  Activity,
  Shield,
  HelpCircle,
  Settings,
} from 'lucide-react';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';

interface RailItem {
  to: string;
  label: string;
  icon: React.ComponentType<{ className?: string }>;
}

/**
 * Slim icon-only navigation rail — a compact, always-visible shortcut strip that mirrors
 * Quick Access's routes so the busiest destinations stay one click away even when the
 * Quick Access panel is collapsed. Purely a compact view over the same routes; no
 * navigation logic lives here beyond NavLink itself.
 */
export function IconRail() {
  const { isDemoMode, cloudProvider } = useDemoContext();
  const navPrefix = isDemoMode && cloudProvider ? `/demo/${cloudProvider}` : '';

  const items: RailItem[] = [
    { to: `${navPrefix}/dashboard`, label: 'Namespace Overview', icon: LayoutDashboard },
    { to: `${navPrefix}/fleet`, label: 'Fleet Health', icon: Layers },
    { to: `${navPrefix}/messages-overview?tab=active`, label: 'Active Messages', icon: Database },
    { to: `${navPrefix}/messages-overview?tab=deadletter`, label: 'Dead-Letter', icon: AlertCircle },
    { to: `${navPrefix}/scheduled`, label: 'Scheduled Messages', icon: Clock },
    { to: `${navPrefix}/cloud-bridge`, label: 'Cloud Bridge', icon: Cloud },
    { to: `${navPrefix}/dlq-history`, label: 'DLQ Intelligence', icon: BarChart3 },
    { to: `${navPrefix}/rules`, label: 'Auto-Replay Rules', icon: Zap },
    { to: `${navPrefix}/cross-cloud-trace`, label: 'Multi-Cloud Trace', icon: Route },
    { to: `${navPrefix}/health`, label: 'System Health', icon: Activity },
    { to: `${navPrefix}/security`, label: 'Security & Privacy', icon: Shield },
    { to: `${navPrefix}/help`, label: 'Help & Guide', icon: HelpCircle },
  ];

  return (
    <aside className="w-14 shrink-0 bg-white border-r border-gray-200 flex flex-col items-center py-3 gap-1 overflow-y-auto">
      {items.map(({ to, label, icon: Icon }) => (
        <NavLink
          key={label}
          to={to}
          title={label}
          aria-label={label}
          className={({ isActive }) =>
            `w-10 h-10 shrink-0 flex items-center justify-center rounded-lg transition-colors ${
              isActive
                ? 'bg-primary-100 text-primary-600'
                : 'text-gray-400 hover:bg-gray-100 hover:text-primary-600'
            }`
          }
        >
          <Icon className="w-5 h-5" />
        </NavLink>
      ))}
      <div className="flex-1" />
      <NavLink
        to="/connect"
        title="Connect"
        aria-label="Connect"
        className={({ isActive }) =>
          `w-10 h-10 shrink-0 flex items-center justify-center rounded-lg transition-colors ${
            isActive ? 'bg-primary-100 text-primary-600' : 'text-gray-400 hover:bg-gray-100 hover:text-primary-600'
          }`
        }
      >
        <Settings className="w-5 h-5" />
      </NavLink>
    </aside>
  );
}
