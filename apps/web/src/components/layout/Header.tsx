import { Link } from 'react-router-dom';
import { Settings, User, Search, Cloud } from 'lucide-react';

export function Header() {
  return (
    <header
      className="h-14 bg-primary-500 text-white flex items-center justify-between px-4 shadow-sm"
    >
      {/* Logo & Brand */}
      <div className="flex items-center gap-3">
        <Link to="/" className="flex items-center gap-2 font-semibold text-lg">
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
      <div className="flex items-center gap-2 text-sm">
        <div className="flex items-center gap-2 bg-white/10 px-3 py-1.5 rounded-full">
          <div className="w-2 h-2 bg-green-400 rounded-full animate-pulse" />
          <span className="text-white/90">Connected:</span>
          <span className="font-medium">Prod-SB-01</span>
        </div>
      </div>

      {/* Actions */}
      <div className="flex items-center gap-2">
        {/* Global Search */}
        <button
          className="flex items-center gap-2 bg-white/10 hover:bg-white/20 px-3 py-1.5 rounded-lg text-sm transition-colors"
          title="Search (⌘K)"
        >
          <Search className="w-4 h-4" />
          <span className="text-white/70">Search...</span>
          <kbd className="text-xs bg-white/10 px-1.5 py-0.5 rounded">⌘K</kbd>
        </button>

        {/* Settings */}
        <button
          className="p-2 hover:bg-white/10 rounded-lg transition-colors"
          title="Settings"
        >
          <Settings className="w-5 h-5" />
        </button>

        {/* User Menu */}
        <button className="w-8 h-8 bg-white/20 rounded-full flex items-center justify-center hover:bg-white/30 transition-colors">
          <User className="w-4 h-4" />
        </button>
      </div>
    </header>
  );
}
