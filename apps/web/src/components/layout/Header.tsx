import { Link, useSearchParams } from 'react-router-dom';
import { User, Cloud, HelpCircle, Search, Bell } from 'lucide-react';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useNamespaceStats } from '@servicehub/ui-shared/hooks/useQueues';
import { getProviderStyle } from '@servicehub/ui-shared/lib/providerStyles';
import { ProviderIcon } from '@servicehub/ui-shared/components/ProviderIcon';
import { EnvironmentBadge } from '@/components/EnvironmentBadge';

export function Header() {
  const [searchParams] = useSearchParams();
  const namespaceId = searchParams.get('namespace');
  const { data: namespaces } = useNamespaces();

  // Find the current namespace from URL params
  const currentNamespace = namespaces?.find(ns => ns.id === namespaceId);
  const isConnected = !!currentNamespace;

  // Total active dead-letters across every connection — a genuinely useful notification
  // signal rather than a decorative badge.
  const namespaceIds = namespaces?.map(ns => ns.id) ?? [];
  const statsResults = useNamespaceStats(namespaceIds);
  const totalDlqCount = statsResults.reduce((total, result) => total + (result.data?.totalDlq ?? 0), 0);

  return (
    <header className="bg-gradient-to-r from-primary-600 via-primary-500 to-primary-400 text-white shadow-sm shrink-0">
      {/* Top row — brand, search, notifications, user */}
      <div className="h-14 flex items-center justify-between px-4 gap-4">
        <Link to="/" className="flex items-center gap-2 font-semibold text-lg shrink-0" aria-label="ServiceHub Home">
          <div className="w-8 h-8 bg-white/20 rounded-xl flex items-center justify-center border border-white/25">
            <Cloud className="w-4 h-4" />
          </div>
          <span className="tracking-tight">
            <span className="text-white/95">Service</span>
            <span className="text-white font-bold">Hub</span>
          </span>
          <span className="hidden sm:inline text-[10px] font-semibold bg-white/15 border border-white/20 rounded-full px-2 py-0.5 ml-1">
            v{import.meta.env.VITE_APP_VERSION}
          </span>
        </Link>

        {/* Current connection — provider + namespace + environment. Never hidden: this is the
            operator's only always-visible statement of what a destructive action would affect,
            so it degrades (truncates) rather than disappearing on a narrow viewport. */}
        {isConnected && (
          <div className="flex items-center gap-2 flex-1 min-w-0" data-tour="header-connection">
            <div className="flex items-center gap-2 bg-white/10 px-3 py-1.5 rounded-full min-w-0">
              <div className="w-2 h-2 bg-green-400 rounded-full animate-pulse shrink-0" aria-hidden="true" />
              <ProviderIcon provider={currentNamespace.cloudProvider} className="w-4 h-4 shrink-0" />
              <span className="hidden sm:inline text-xs font-bold uppercase tracking-wide shrink-0">
                {getProviderStyle(currentNamespace.cloudProvider).label}
              </span>
              <span
                className="hidden md:inline text-xs font-medium text-white/90 truncate min-w-0"
                title={currentNamespace.displayName || currentNamespace.name}
              >
                {currentNamespace.displayName || currentNamespace.name}
              </span>
              <span className="shrink-0">
                <EnvironmentBadge env={currentNamespace.environment} />
              </span>
            </div>
          </div>
        )}

        {!namespaces?.length && (
          <div className="flex-1 flex items-center justify-center">
            {isConnected ? null : (
              <div className="flex items-center gap-2 bg-white/10 px-3 py-1.5 rounded-full text-sm">
                <div className="w-2 h-2 bg-gray-300 rounded-full" aria-hidden="true" />
                <span className="text-white/70">No namespace selected</span>
                <Link to="/connect" className="text-[11px] font-semibold text-white underline underline-offset-2">
                  Connect a cloud
                </Link>
              </div>
            )}
          </div>
        )}

        {/* Actions */}
        <div className="flex items-center gap-2 shrink-0">
          {/* Search / Command Palette trigger */}
          <button
            onClick={() => window.dispatchEvent(new Event('servicehub:open-palette'))}
            className="hidden sm:flex items-center gap-1.5 px-3 py-1.5 bg-white/95 hover:bg-white border border-white/20 rounded-lg text-gray-500 text-xs transition-colors w-44"
            title="Open command palette (⌘K)"
            aria-label="Open command palette"
          >
            <Search className="w-3.5 h-3.5" />
            <span className="flex-1 text-left">Search…</span>
            <kbd className="text-[10px] font-mono bg-gray-100 text-gray-600 px-1 rounded">⌘K</kbd>
          </button>
          <button
            onClick={() => window.dispatchEvent(new Event('servicehub:open-palette'))}
            className="sm:hidden p-2 hover:bg-white/10 rounded-lg transition-colors"
            title="Open command palette (⌘K)"
            aria-label="Open command palette"
          >
            <Search className="w-5 h-5" />
          </button>

          {/* Help */}
          <Link
            to="/help"
            className="p-2 hover:bg-white/10 rounded-lg transition-colors"
            title="Help & Quick Reference"
            aria-label="Help"
            data-tour="header-help"
          >
            <HelpCircle className="w-5 h-5" />
          </Link>

          {/* Notifications — total active dead-letters across every connection */}
          <Link
            to="/dlq-history"
            className="relative p-2 hover:bg-white/10 rounded-lg transition-colors"
            title={`${totalDlqCount} active dead-letter message(s) across all connections`}
            aria-label="Notifications"
          >
            <Bell className="w-5 h-5" />
            {totalDlqCount > 0 && (
              <span className="absolute -top-0.5 -right-0.5 min-w-[16px] h-4 px-1 flex items-center justify-center bg-red-600 text-white text-[10px] font-bold rounded-full border-2 border-primary-500">
                {totalDlqCount > 99 ? '99+' : totalDlqCount}
              </span>
            )}
          </Link>

          {/* User Menu */}
          <button
            className="w-8 h-8 bg-white/20 rounded-full flex items-center justify-center hover:bg-white/30 transition-colors"
            aria-label="User menu"
            title="ServiceHub User"
          >
            <User className="w-4 h-4" />
          </button>
        </div>
      </div>
    </header>
  );
}
