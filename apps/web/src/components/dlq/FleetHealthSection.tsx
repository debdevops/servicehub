import { useNavigate, Link } from 'react-router-dom';
import { Layers, ArrowRight, CheckCircle } from 'lucide-react';
import type { FleetHealthSummary } from '@servicehub/ui-shared/hooks/useInvestigationQueue';
import type { FleetHealthSeverity, FleetNamespaceHealth } from '@servicehub/ui-shared/lib/api/fleet';
import { ProviderBadge } from '@servicehub/ui-shared/lib/providerStyles';
import type { CloudProviderType } from '@servicehub/ui-shared/lib/api/types';

// Mirrors FleetPage's severity styling (`severityStyles` in FleetPage.tsx) so the same
// Healthy/Warning/Critical signal reads identically wherever it appears.
const severityStyles: Record<FleetHealthSeverity, { dot: string; text: string; bg: string; label: string }> = {
  critical: { dot: 'bg-red-500', text: 'text-red-700', bg: 'bg-red-50', label: 'Critical' },
  warning: { dot: 'bg-amber-500', text: 'text-amber-700', bg: 'bg-amber-50', label: 'Warning' },
  healthy: { dot: 'bg-emerald-500', text: 'text-emerald-700', bg: 'bg-emerald-50', label: 'Healthy' },
};

interface FleetHealthSectionProps {
  fleetHealth: FleetHealthSummary | null | undefined;
}

/**
 * Fleet Health section (Epic 6 MVP) — composed from the same FleetNamespaceHealth/severity
 * data FleetPage renders, capped server-side to the worst-N namespaces. Answers "which
 * namespaces require attention" inside Incident Center, without duplicating FleetPage's table.
 */
export function FleetHealthSection({ fleetHealth }: FleetHealthSectionProps) {
  const navigate = useNavigate();

  if (!fleetHealth) return null;

  const { namespaceCount, totalActive, topUnhealthyNamespaces } = fleetHealth;

  const goToNamespace = (n: FleetNamespaceHealth) => navigate(`/dlq-history?namespace=${n.namespaceId}`);

  return (
    <div className="mb-6">
      <div className="flex items-center justify-between mb-3">
        <h2 className="text-sm font-semibold text-gray-900 flex items-center gap-2">
          <Layers className="w-4 h-4" /> Fleet Health
        </h2>
        <Link
          to="/fleet"
          className="text-xs font-medium text-blue-700 hover:text-blue-900 inline-flex items-center gap-1"
        >
          View all fleet health
          <ArrowRight className="w-3 h-3" />
        </Link>
      </div>

      {topUnhealthyNamespaces.length === 0 ? (
        <div className="bg-white border border-gray-200 rounded-lg p-8 text-center">
          <CheckCircle className="w-8 h-8 text-green-500 mx-auto mb-2" />
          <p className="text-gray-900 font-medium">All namespaces are healthy</p>
          <p className="text-sm text-gray-600 mt-1">
            {namespaceCount} namespace{namespaceCount !== 1 ? 's' : ''} monitored, {totalActive} active dead-letter
            {totalActive !== 1 ? 's' : ''} fleet-wide.
          </p>
        </div>
      ) : (
        <div className="space-y-2">
          {topUnhealthyNamespaces.map((n) => {
            const sev = severityStyles[n.severity];
            return (
              <div
                key={n.namespaceId}
                className="bg-white border border-gray-200 rounded-lg p-4 hover:shadow-sm transition-shadow"
              >
                <div className="flex items-start justify-between gap-4 mb-3">
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2 flex-wrap mb-2">
                      <span
                        className={`inline-flex items-center gap-1.5 px-2 py-1 rounded-full text-xs font-semibold ${sev.bg} ${sev.text}`}
                      >
                        <span className={`w-1.5 h-1.5 rounded-full ${sev.dot}`} />
                        {sev.label}
                      </span>
                      <ProviderBadge provider={n.provider.toLowerCase() as CloudProviderType} />
                      <span className="text-xs text-gray-500">{n.environment}</span>
                    </div>
                    <p className="font-medium text-gray-900 break-words">{n.namespaceName}</p>
                    <p className="text-sm text-gray-600 mt-1">
                      {n.activeCount} active
                      {n.newInWindow > 0 && `, +${n.newInWindow} new`}
                      {n.topCategory && ` — top category: ${n.topCategory}`}
                    </p>
                  </div>
                </div>
                <div className="flex gap-2 flex-wrap">
                  <button
                    onClick={() => goToNamespace(n)}
                    className="text-xs px-3 py-1.5 bg-blue-50 text-blue-700 rounded-md hover:bg-blue-100 transition-colors font-medium"
                    aria-label={`Open namespace ${n.namespaceName}`}
                  >
                    Open Namespace
                  </button>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
