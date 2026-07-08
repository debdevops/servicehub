/**
 * DemoModeBanner — Persistent visual indicator when in demo mode.
 *
 * Renders a colored banner directly below the Header inside MainLayout.
 * Clearly communicates:
 *   - Which cloud provider is being demonstrated
 *   - The scenario name
 *   - A CTA to connect a real namespace
 *   - That all destructive operations are disabled
 */

import { Link } from 'react-router-dom';
import { FlaskConical, ArrowRight } from 'lucide-react';
import { useDemoContext } from '@/lib/demo/DemoContext';

const ACCENT_CLASSES = {
  blue: {
    banner: 'bg-blue-600 border-blue-700',
    badge: 'bg-blue-700 text-blue-100 border-blue-500',
    cta: 'bg-white text-blue-700 hover:bg-blue-50',
    icon: 'text-blue-200',
  },
  orange: {
    banner: 'bg-orange-500 border-orange-600',
    badge: 'bg-orange-600 text-orange-100 border-orange-400',
    cta: 'bg-white text-orange-700 hover:bg-orange-50',
    icon: 'text-orange-200',
  },
  green: {
    banner: 'bg-green-600 border-green-700',
    badge: 'bg-green-700 text-green-100 border-green-500',
    cta: 'bg-white text-green-700 hover:bg-green-50',
    icon: 'text-green-200',
  },
} as const;

type AccentColor = keyof typeof ACCENT_CLASSES;

export function DemoModeBanner() {
  const { isDemoMode, cloudProviderName, scenarioName, accentColor } = useDemoContext();

  if (!isDemoMode) return null;

  const accent = (ACCENT_CLASSES[accentColor as AccentColor] ?? ACCENT_CLASSES.blue);

  return (
    <div
      className={`flex items-center justify-between px-4 py-1.5 text-white border-b text-sm shrink-0 ${accent.banner}`}
      role="banner"
      aria-label="Demo mode indicator"
      data-testid="demo-mode-banner"
    >
      {/* Left: Provider + scenario */}
      <div className="flex items-center gap-3 min-w-0">
        <div className="flex items-center gap-1.5">
          <FlaskConical className={`w-4 h-4 shrink-0 ${accent.icon}`} />
          <span className="font-bold text-white">Demo Mode</span>
        </div>
        <span className="text-white/60 hidden sm:block">·</span>
        <span className="text-white/90 font-medium hidden sm:block truncate">
          {cloudProviderName}
        </span>
        <span
          className={`hidden md:inline-flex items-center px-2 py-0.5 rounded text-xs font-medium border ${accent.badge}`}
        >
          {scenarioName}
        </span>
        <span className="text-white/70 text-xs hidden lg:block">
          — All cloud operations are read-only. Mock data only.
        </span>
      </div>

      {/* Right: CTA */}
      <Link
        to="/connect"
        className={`flex items-center gap-1.5 ml-4 px-3 py-1 rounded-lg text-xs font-bold shrink-0 transition-colors ${accent.cta}`}
      >
        Connect Real {cloudProviderName.split(' ')[0]}
        <ArrowRight className="w-3.5 h-3.5" />
      </Link>
    </div>
  );
}
