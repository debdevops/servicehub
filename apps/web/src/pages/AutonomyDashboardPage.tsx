import { Gauge, ShieldAlert, RefreshCw, AlertCircle, Info, Zap, History } from 'lucide-react';
import { useAutonomyDashboard } from '@servicehub/ui-shared/hooks/useRecoveryLedger';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import type {
  AutonomyGrantSummary,
  AutonomyTransitionSummary,
  CircuitBreakerTrip,
} from '@servicehub/ui-shared/lib/api/recovery';

// L0-L2 never appear here (no AutonomyGrant row exists until a signature is first promoted past
// the L3 floor — see AutonomyGrant's doc comment); L3 is the permanent human-approved floor a
// demotion can land back on, L4/L5 are earned unattended trust.
const LEVEL_STYLES: Record<number, string> = {
  3: 'bg-gray-100 text-gray-700 border-gray-300',
  4: 'bg-blue-50 text-blue-700 border-blue-300',
  5: 'bg-green-50 text-green-700 border-green-300',
};

function LevelBadge({ level, label }: { level: number; label: string }) {
  const style = LEVEL_STYLES[level] ?? 'bg-gray-100 text-gray-700 border-gray-300';
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium border ${style}`}>
      {label}
    </span>
  );
}

function truncateHash(hash: string): string {
  return hash.length > 16 ? `${hash.substring(0, 16)}…` : hash;
}

function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short', day: '2-digit', hour: '2-digit', minute: '2-digit',
  });
}

/**
 * `/autonomy-dashboard` — the fleet-wide autonomy dashboard (roadmap §11 item 5, §15 item 9):
 * how many signatures currently stand at each autonomy level, every currently standing
 * `AutonomyGrant`, every `AutoReplayRule` the success-rate circuit breaker has tripped, the
 * owner-scoped emergency-stop status, and the most recent promotions/demotions. Pure read-side
 * aggregation over `GET /recovery/autonomy-dashboard` — this page never itself grants, revokes,
 * or otherwise decides autonomy; it only reports what the backend's ledger already decided.
 */
export default function AutonomyDashboardPage() {
  const { isDemoMode } = useDemoContext();
  const { data: overview, isLoading, isError, refetch, isFetching } = useAutonomyDashboard();

  const levelCounts = overview?.levelCounts ?? [];
  const grants = overview?.grants ?? [];
  const circuitBreakerTrips = overview?.circuitBreakerTrips ?? [];
  const recentTransitions = overview?.recentTransitions ?? [];

  const standingCount = levelCounts.filter(c => c.level >= 4).reduce((sum, c) => sum + c.count, 0);

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      <div className="bg-white border-b border-gray-200 px-6 py-4 shrink-0">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-xl font-bold text-gray-900 flex items-center gap-2">
              <Gauge className="w-5 h-5 text-blue-600" />
              Autonomy Dashboard
            </h1>
            <p className="text-sm text-gray-500 mt-0.5">
              How much unattended trust the fleet has actually earned, and what's currently
              constraining it — read directly from the Recovery Evidence Ledger.
            </p>
          </div>
          <button
            onClick={() => refetch()}
            disabled={isFetching || isDemoMode}
            className="flex items-center gap-1.5 px-3 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-200 rounded-lg hover:bg-gray-50 shadow-sm transition-all disabled:opacity-50"
          >
            <RefreshCw className={`w-4 h-4 ${isFetching ? 'animate-spin' : ''}`} />
            Refresh
          </button>
        </div>

        {isDemoMode && (
          <div className="mt-3 flex items-center gap-2 px-3 py-2 rounded-lg bg-amber-50 border border-amber-200 text-xs text-amber-800">
            <Info className="w-4 h-4 shrink-0" />
            Demo Mode — there is no live Recovery Evidence Ledger here, so this dashboard is
            honestly empty rather than fabricated.
          </div>
        )}

        {overview?.emergencyStopActive && (
          <div className="mt-3 flex items-center gap-2 px-3 py-2 rounded-lg bg-red-50 border border-red-200 text-xs text-red-800">
            <ShieldAlert className="w-4 h-4 shrink-0" />
            <span className="font-medium">Emergency stop is active</span> — no new unattended
            (Automation/System) execution can proceed for this owner until it's cleared.
          </div>
        )}
      </div>

      <div className="flex-1 overflow-auto p-6">
        {isLoading ? (
          <div className="flex items-center justify-center h-64">
            <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600" />
          </div>
        ) : isError ? (
          <div className="flex items-center justify-center h-64">
            <div className="text-center">
              <AlertCircle className="w-10 h-10 text-red-400 mx-auto mb-3" />
              <p className="text-gray-600 font-medium">Failed to load the autonomy dashboard</p>
              <button onClick={() => refetch()} className="mt-3 px-4 py-2 text-sm text-blue-600 hover:text-blue-700 border border-blue-300 rounded-lg hover:bg-blue-50">
                Try Again
              </button>
            </div>
          </div>
        ) : (
          <div className="space-y-6 max-w-5xl">
            {/* Summary tiles */}
            <div className="grid grid-cols-3 gap-4">
              <div className="bg-white border border-gray-200 rounded-lg p-4 shadow-sm">
                <div className="text-2xl font-bold text-gray-900">{overview?.totalSignatures ?? 0}</div>
                <div className="text-xs text-gray-500 mt-1">Signatures with a standing grant</div>
              </div>
              <div className="bg-white border border-gray-200 rounded-lg p-4 shadow-sm">
                <div className="text-2xl font-bold text-green-700">{standingCount}</div>
                <div className="text-xs text-gray-500 mt-1">Currently at Standing (L4) or Unattended (L5)</div>
              </div>
              <div className="bg-white border border-gray-200 rounded-lg p-4 shadow-sm">
                <div className={`text-2xl font-bold ${circuitBreakerTrips.length > 0 ? 'text-red-600' : 'text-gray-900'}`}>
                  {circuitBreakerTrips.length}
                </div>
                <div className="text-xs text-gray-500 mt-1">Rules currently tripped by the circuit breaker</div>
              </div>
            </div>

            {/* Level breakdown */}
            {levelCounts.length > 0 && (
              <div className="bg-white border border-gray-200 rounded-lg shadow-sm">
                <div className="px-4 py-3 border-b border-gray-200">
                  <h2 className="text-sm font-semibold text-gray-800">Level breakdown</h2>
                </div>
                <div className="p-4 flex flex-wrap gap-3">
                  {levelCounts.map(c => (
                    <div key={`${c.actionKind}-${c.level}`} className="flex items-center gap-2 px-3 py-2 bg-gray-50 rounded-lg border border-gray-200">
                      <LevelBadge level={c.level} label={c.levelLabel} />
                      <span className="text-xs text-gray-500">{c.actionKind}</span>
                      <span className="text-sm font-semibold text-gray-800">{c.count}</span>
                    </div>
                  ))}
                </div>
              </div>
            )}

            {/* Circuit breaker trips */}
            {circuitBreakerTrips.length > 0 && (
              <div className="bg-white border border-red-200 rounded-lg shadow-sm">
                <div className="px-4 py-3 border-b border-red-100 flex items-center gap-2">
                  <Zap className="w-4 h-4 text-red-500" />
                  <h2 className="text-sm font-semibold text-gray-800">Circuit-breaker-tripped rules</h2>
                </div>
                <ul className="divide-y divide-gray-100">
                  {circuitBreakerTrips.map((trip: CircuitBreakerTrip) => (
                    <li key={trip.ruleId} className="px-4 py-3 text-sm">
                      <div className="font-medium text-gray-800">{trip.ruleName}</div>
                      {trip.disabledReasonDetail && (
                        <div className="text-xs text-gray-500 mt-0.5">{trip.disabledReasonDetail}</div>
                      )}
                    </li>
                  ))}
                </ul>
              </div>
            )}

            {/* Grants table */}
            <div className="bg-white border border-gray-200 rounded-lg shadow-sm">
              <div className="px-4 py-3 border-b border-gray-200">
                <h2 className="text-sm font-semibold text-gray-800">Signature standings</h2>
              </div>
              {grants.length === 0 ? (
                <div className="px-4 py-8 text-center text-sm text-gray-500">
                  No signature has ever been promoted past the Approve (L3) floor for this owner yet.
                </div>
              ) : (
                <table className="w-full text-sm" aria-label="Autonomy grants">
                  <thead className="bg-gray-50 border-b border-gray-200">
                    <tr>
                      <th scope="col" className="px-4 py-2 text-left text-xs font-semibold text-gray-500">Signature</th>
                      <th scope="col" className="px-4 py-2 text-left text-xs font-semibold text-gray-500">Action</th>
                      <th scope="col" className="px-4 py-2 text-left text-xs font-semibold text-gray-500">Level</th>
                      <th scope="col" className="px-4 py-2 text-left text-xs font-semibold text-gray-500">Updated</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100">
                    {grants.map((grant: AutonomyGrantSummary) => (
                      <tr key={`${grant.signatureHash}-${grant.actionKind}`} className="hover:bg-gray-50">
                        <td className="px-4 py-2.5 font-mono text-xs text-gray-700" title={grant.signatureHash}>
                          {truncateHash(grant.signatureHash)}
                        </td>
                        <td className="px-4 py-2.5 text-xs text-gray-600">{grant.actionKind}</td>
                        <td className="px-4 py-2.5">
                          <LevelBadge level={grant.currentLevel} label={grant.levelLabel} />
                        </td>
                        <td className="px-4 py-2.5 text-xs text-gray-500 whitespace-nowrap">{formatDateTime(grant.updatedAtUtc)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>

            {/* Recent transitions */}
            <div className="bg-white border border-gray-200 rounded-lg shadow-sm">
              <div className="px-4 py-3 border-b border-gray-200 flex items-center gap-2">
                <History className="w-4 h-4 text-gray-500" />
                <h2 className="text-sm font-semibold text-gray-800">Recent promotions &amp; demotions</h2>
              </div>
              {recentTransitions.length === 0 ? (
                <div className="px-4 py-8 text-center text-sm text-gray-500">
                  No promotion or demotion has been recorded for this owner yet.
                </div>
              ) : (
                <ul className="divide-y divide-gray-100">
                  {recentTransitions.map((t: AutonomyTransitionSummary, i) => (
                    <li key={i} className="px-4 py-3 text-sm flex items-start justify-between gap-3">
                      <div className="min-w-0">
                        <div className="flex items-center gap-1.5 font-mono text-xs text-gray-700" title={t.signatureHash}>
                          {truncateHash(t.signatureHash)}
                          <span className="text-gray-400 font-sans">({t.actionKind})</span>
                        </div>
                        <div className="text-xs text-gray-500 mt-1">
                          L{t.previousLevel} → L{t.newLevel} — {t.reason}
                        </div>
                      </div>
                      <div className="text-xs text-gray-400 whitespace-nowrap shrink-0">{formatDateTime(t.occurredAtUtc)}</div>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
