import { Link, useSearchParams } from 'react-router-dom';
import { User, Cloud, HelpCircle, Search } from 'lucide-react';
import { Link as RouterLink } from 'react-router-dom';
import { useNamespaces } from '@/hooks/useNamespaces';
import { getStoredUser } from '@/components/WelcomeDialog';

export function Header() {
  const [searchParams] = useSearchParams();
  const namespaceId = searchParams.get('namespace');
  const { data: namespaces } = useNamespaces();
  
  // Find the current namespace from URL params
  const currentNamespace = namespaces?.find(ns => ns.id === namespaceId);
  const isConnected = !!currentNamespace;

  // Read user identity collected by WelcomeDialog
  const user = getStoredUser();

  return (
    <>
    <header
      className="h-14 bg-primary-500 text-white flex items-center justify-between px-4 shadow-sm"
    >
      {/* Logo & Brand */}
      <div className="flex items-center gap-3">
        <Link to="/" className="flex items-center gap-2 font-semibold text-lg" aria-label="ServiceHub Home">
          <div className="w-8 h-8 bg-white/20 rounded-xl flex items-center justify-center border border-white/25">
            <Cloud className="w-4 h-4" />
          </div>
          <span className="tracking-tight">
            <span className="text-white/95">Service</span>
            <span className="text-white font-bold">Hub</span>
          </span>
        </Link>
      </div>

      {/* Connection Status */}
      <div className="flex items-center gap-2 text-sm" data-tour="header-connection">
        {isConnected ? (
          <div className="flex items-center gap-2 bg-white/10 px-3 py-1.5 rounded-full">
            <div className="w-2 h-2 bg-green-400 rounded-full animate-pulse" aria-hidden="true" />
            <span className="text-white/90">Connected:</span>
            <span className="font-medium">{currentNamespace.displayName || currentNamespace.name}</span>
            {/* Environment badge */}
            <span className={`px-1.5 py-0.5 text-[10px] font-bold rounded uppercase leading-none ${
              currentNamespace.environment === 'Prod' ? 'bg-red-500 text-white' :
              currentNamespace.environment === 'Uat' ? 'bg-amber-400 text-amber-900' :
              'bg-green-400 text-green-900'
            }`}>
              {currentNamespace.environment || 'DEV'}
            </span>
          </div>
        ) : (
          <div className="flex items-center gap-2 bg-white/10 px-3 py-1.5 rounded-full">
            <div className="w-2 h-2 bg-gray-400 rounded-full" aria-hidden="true" />
            <span className="text-white/70">No namespace selected</span>
          </div>
        )}
      </div>

      {/* Actions */}
      <div className="flex items-center gap-2">
        {/* Command Palette trigger */}
        <button
          onClick={() => window.dispatchEvent(new Event('servicehub:open-palette'))}
          className="hidden sm:flex items-center gap-1.5 px-2.5 py-1.5 bg-white/10 hover:bg-white/20 border border-white/20 rounded-lg text-white/80 text-xs transition-colors"
          title="Open command palette (⌘K)"
          aria-label="Open command palette"
        >
          <Search className="w-3.5 h-3.5" />
          <span>Search…</span>
          <kbd className="ml-1 text-[10px] font-mono bg-white/10 px-1 rounded">⌘K</kbd>
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
        <RouterLink
          to="/help"
          className="p-2 hover:bg-white/10 rounded-lg transition-colors"
          title="Help & Quick Reference"
          aria-label="Help"
          data-tour="header-help"
        >
          <HelpCircle className="w-5 h-5" />
        </RouterLink>

        {/* User Menu */}
        <button 
          className="w-8 h-8 bg-white/20 rounded-full flex items-center justify-center hover:bg-white/30 transition-colors"
          aria-label="User menu"
          title={user ? `${user.fullName} (${user.email})` : 'ServiceHub User'}
        >
          <User className="w-4 h-4" />
        </button>
      </div>
    </header>
  </>
  );
}
