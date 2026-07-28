/**
 * SimulatorModeBanner — Persistent visual indicator when the connected backend
 * is running in Simulator mode (`./run.sh --simulator`, ASPNETCORE_ENVIRONMENT=Simulator).
 *
 * Renders directly below the Header inside MainLayout, mirroring DemoModeBanner's
 * pattern, so "which mode am I in" is answerable from any page, not just /simulator.
 */

import { FlaskConical } from 'lucide-react';
import { useHealthVersion } from '@servicehub/ui-shared/hooks/useHealth';

export function SimulatorModeBanner() {
  const { data } = useHealthVersion();

  if (data?.environment !== 'Simulator') return null;

  return (
    <div
      className="flex items-center gap-1.5 px-4 py-1.5 text-amber-900 bg-amber-100 border-b border-amber-300 text-sm shrink-0"
      role="banner"
      aria-label="Simulator mode indicator"
      data-testid="simulator-mode-banner"
    >
      <FlaskConical className="w-4 h-4 shrink-0 text-amber-600" />
      <span className="font-bold">Simulator Mode</span>
      <span className="text-amber-700/80 hidden sm:inline">
        — All data is synthetic. No real cloud credentials required.
      </span>
    </div>
  );
}
