import { Link, useSearchParams } from 'react-router-dom';
import { User, Cloud, HelpCircle, Search, Bell } from 'lucide-react';
import { useQueries } from '@tanstack/react-query';
import { useNamespaces } from '@/hooks/useNamespaces';
import { apiClient } from '@/lib/api/client';
import { getProviderStyle } from '@/lib/providerStyles';
import { setThemeProvider } from '@/lib/providerTheme';
import { ProviderIcon } from '@/components/ProviderIcon';

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
  const statsResults = useQueries({
    queries: namespaceIds.map(id => ({
      queryKey: ['namespace-stats', id] as const,
      queryFn: async () => {
        const response = await apiClient.get<{ totalDlq: number }>(`/namespaces/${id}/stats`, {
          _silent: true,
        } as Record<string, unknown>);
        return response.data;
      },
      enabled: !!id,
      staleTime: 30_000,
      refetchInterval: 60_000,
      refetchIntervalInBackground: false,
    })),
  });
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
            v3.3.0
          </span>
        </Link>

        {/* Connected namespaces — quick-switch chips */}
        {namespaces && namespaces.length > 0 && (
          <div className="hidden lg:flex items-center gap-2 flex-1 min-w-0">
            <span className="text-[10px] font-bold text-white/60 uppercase tracking-wider shrink-0">
              Current Connections
            </span>
            <div className="flex items-center gap-1.5 overflow-x-auto min-w-0">
              {namespaces.map((ns) => {
                const style = getProviderStyle(ns.cloudProvider);
                return (
                  <button
                    key={ns.id}
                    onClick={() => setThemeProvider(ns.cloudProvider ?? 'azure')}
                    className={`flex items-center gap-1.5 bg-white/95 hover:bg-white text-gray-800 pl-1 pr-2.5 py-1 rounded-full text-xs font-medium shrink-0 transition-colors shadow-sm border-l-[3px] ${style.accentBorder}`}
                    title={`${ns.displayName || ns.name} — ${style.label}`}
                  >
                    <ProviderIcon provider={ns.cloudProvider} className="w-4 h-4 shrink-0" />
                    <span className="truncate max-w-[110px]">{ns.displayName || ns.name}</span>
                    <span className={`w-1.5 h-1.5 rounded-full shrink-0 ${ns.isActive ? 'bg-green-500' : 'bg-gray-300'}`} />
                  </button>
                );
              })}
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
            <kbd className="text-[10px] font-mono bg-gray-100 text-gray-500 px-1 rounded">⌘K</kbd>
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
              <span className="absolute -top-0.5 -right-0.5 min-w-[16px] h-4 px-1 flex items-center justify-center bg-red-500 text-white text-[10px] font-bold rounded-full border-2 border-primary-500">
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

      {/* Connection status row — visible when a namespace is selected via URL, or on small screens */}
      {isConnected && (
        <div className="px-4 pb-2 flex items-center gap-2 text-sm" data-tour="header-connection">
          <div className="flex items-center gap-2 bg-white/10 px-3 py-1.5 rounded-full">
            <div className="w-2 h-2 bg-green-400 rounded-full animate-pulse" aria-hidden="true" />
            <ProviderIcon provider={currentNamespace.cloudProvider} className="w-4 h-4 shrink-0" />
            <span className="font-medium">{currentNamespace.displayName || currentNamespace.name}</span>
            <span className={`px-1.5 py-0.5 text-[10px] font-bold rounded uppercase leading-none ${
              currentNamespace.environment === 'prod' ? 'bg-red-500 text-white' :
              currentNamespace.environment === 'uat' ? 'bg-amber-400 text-amber-900' :
              'bg-green-400 text-green-900'
            }`}>
              {(currentNamespace.environment ?? 'dev').toUpperCase()}
            </span>
          </div>
        </div>
      )}
    </header>
  );
}
