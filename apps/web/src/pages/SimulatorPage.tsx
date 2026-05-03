import { useState } from 'react';
import {
  FlaskConical,
  RefreshCw,
  AlertCircle,
  Zap,
  Clock,
  Trash2,
  RotateCcw,
  ChevronDown,
} from 'lucide-react';
import {
  useSimulatorStatus,
  useInjectFault,
  useClearFaults,
  useResetSimulator,
  useAdvanceTime,
  useInjectDlqFlood,
} from '@/hooks/useSimulator';
import type { SimulatorNamespaceSummary, InjectFaultRequest, DlqFloodRequest } from '@/lib/api/simulator';

// ── Fault type metadata ────────────────────────────────────────────────────

const FAULT_TYPES: { value: InjectFaultRequest['faultType']; label: string; description: string }[] = [
  { value: 'MaxDelivery',       label: 'MaxDelivery',       description: 'Flood DLQ with max delivery exceeded messages' },
  { value: 'VisibilityExpiry',  label: 'VisibilityExpiry',  description: 'Expire AWS SQS visibility windows' },
  { value: 'AckDeadlineStorm', label: 'AckDeadlineStorm', description: 'Expire GCP ack deadlines immediately' },
  { value: 'KmsError',          label: 'KmsError',          description: 'Simulate KMS key not accessible (AWS)' },
  { value: 'OrderingStall',     label: 'OrderingStall',     description: 'Stall a GCP ordering key' },
  { value: 'NetworkTimeout',    label: 'NetworkTimeout',    description: 'Return timeout errors from the provider' },
];

const DLQ_REASONS = [
  'MaxDeliveryCountExceeded',
  'TTLExpiredException',
  'Application',
  'MaxReceiveCount',
  'nack',
];

// ── Provider card colors ───────────────────────────────────────────────────

function providerStyle(provider: string): { border: string; bg: string; badge: string; label: string } {
  switch (provider.toLowerCase()) {
    case 'aws':
      return { border: 'border-orange-200', bg: 'bg-orange-50', badge: 'bg-orange-100 text-orange-700', label: 'AWS' };
    case 'gcp':
      return { border: 'border-green-200', bg: 'bg-green-50', badge: 'bg-green-100 text-green-700', label: 'GCP' };
    default:
      return { border: 'border-blue-200', bg: 'bg-blue-50', badge: 'bg-blue-100 text-blue-700', label: 'Azure' };
  }
}

function formatUtc(iso: string): string {
  try {
    return new Date(iso).toUTCString().replace(' GMT', ' UTC');
  } catch {
    return iso;
  }
}

// ── Provider Card ──────────────────────────────────────────────────────────

function ProviderCard({ ns }: { ns: SimulatorNamespaceSummary }) {
  const style = providerStyle(ns.provider);
  const hasData = ns.entityCount > 0 || ns.activeMessageCount > 0;
  return (
    <div className={`rounded-xl border-2 ${style.border} ${style.bg} p-5 flex flex-col gap-3`}>
      <div className="flex items-center justify-between">
        <span className={`text-xs font-bold px-2 py-0.5 rounded-full ${style.badge}`}>
          {style.label}
        </span>
        <span className={`text-xs font-medium px-2 py-1 rounded-full ${hasData ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-500'}`}>
          {hasData ? '● Active' : '○ No data'}
        </span>
      </div>
      <div>
        <div className="font-semibold text-gray-900 text-sm truncate">{ns.name}</div>
        <div className="text-xs text-gray-500 font-mono mt-0.5">{ns.id}</div>
      </div>
      <div className="grid grid-cols-3 gap-2 text-center">
        <div className="bg-white/70 rounded-lg p-2">
          <div className="text-lg font-bold text-gray-900">{ns.entityCount}</div>
          <div className="text-xs text-gray-500">Entities</div>
        </div>
        <div className="bg-white/70 rounded-lg p-2">
          <div className="text-lg font-bold text-gray-900">{ns.activeMessageCount}</div>
          <div className="text-xs text-gray-500">Messages</div>
        </div>
        <div className="bg-white/70 rounded-lg p-2">
          <div className="text-lg font-bold text-red-600">{ns.dlqMessageCount}</div>
          <div className="text-xs text-gray-500">DLQ</div>
        </div>
      </div>
    </div>
  );
}

// ── Confirmation dialog ────────────────────────────────────────────────────

function ConfirmResetDialog({
  onConfirm,
  onCancel,
  isPending,
}: {
  onConfirm: () => void;
  onCancel: () => void;
  isPending: boolean;
}) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div className="bg-white rounded-xl shadow-2xl p-6 max-w-sm w-full mx-4">
        <h3 className="text-lg font-bold text-gray-900 mb-2">Reset Simulator?</h3>
        <p className="text-sm text-gray-600 mb-6">
          This will wipe all message state and reseed with the default dataset (Azure + AWS + GCP).
          All injected faults will be cleared.
        </p>
        <div className="flex gap-3 justify-end">
          <button
            onClick={onCancel}
            disabled={isPending}
            className="px-4 py-2 border border-gray-200 rounded-lg text-sm text-gray-700 hover:bg-gray-50 transition-colors disabled:opacity-50"
          >
            Cancel
          </button>
          <button
            onClick={onConfirm}
            disabled={isPending}
            className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg text-sm font-semibold transition-colors disabled:opacity-50 flex items-center gap-2"
          >
            {isPending && <RefreshCw className="w-3.5 h-3.5 animate-spin" />}
            Reset & Reseed
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Main SimulatorPage ─────────────────────────────────────────────────────

export function SimulatorPage() {
  const { data: status, isLoading, error, refetch } = useSimulatorStatus();
  const injectFaultMutation = useInjectFault();
  const clearFaultsMutation = useClearFaults();
  const resetMutation = useResetSimulator();
  const advanceTimeMutation = useAdvanceTime();
  const dlqFloodMutation = useInjectDlqFlood();

  // Fault injection form state
  const [faultType, setFaultType] = useState<InjectFaultRequest['faultType']>('MaxDelivery');
  const [faultTarget, setFaultTarget] = useState('');
  const [faultNamespaceId, setFaultNamespaceId] = useState('');
  const [faultSeverity, setFaultSeverity] = useState(5);
  const [faultDuration, setFaultDuration] = useState(60);

  // DLQ flood form state
  const [floodNamespaceId, setFloodNamespaceId] = useState('');
  const [floodEntity, setFloodEntity] = useState('');
  const [floodCount, setFloodCount] = useState(10);
  const [floodReason, setFloodReason] = useState('MaxDeliveryCountExceeded');
  const [floodResult, setFloodResult] = useState<string | null>(null);

  // Reset confirmation
  const [showResetConfirm, setShowResetConfirm] = useState(false);
  const [resetResult, setResetResult] = useState<string | null>(null);

  const namespaces = status?.namespaces ?? [];

  // API not available — graceful fallback
  if (error && !status) {
    return (
      <div className="p-6 max-w-5xl mx-auto">
        <div className="rounded-xl border border-amber-200 bg-amber-50 p-6 flex items-start gap-4">
          <FlaskConical className="w-6 h-6 text-amber-600 shrink-0 mt-0.5" />
          <div>
            <h2 className="text-lg font-bold text-amber-900 mb-1">Simulator API not available</h2>
            <p className="text-sm text-amber-800 mb-3">
              Start the API with <code className="font-mono bg-amber-100 px-1 rounded">--environment Simulator</code> to use the control panel.
            </p>
            <pre className="text-xs bg-amber-900 text-green-300 rounded-lg p-4 overflow-x-auto">
{`ASPNETCORE_ENVIRONMENT=Simulator dotnet run \\
  --project services/api/src/ServiceHub.Api/ServiceHub.Api.csproj \\
  --no-launch-profile \\
  --urls http://localhost:5200`}
            </pre>
          </div>
        </div>
      </div>
    );
  }

  const handleInjectFault = () => {
    injectFaultMutation.mutate({
      faultType,
      namespaceId: faultNamespaceId || (namespaces[0]?.id ?? ''),
      targetEntity: faultTarget || undefined,
      severity: faultSeverity,
      durationSeconds: faultDuration,
    });
  };

  const handleDlqFlood = () => {
    const req: DlqFloodRequest = {
      namespaceId: floodNamespaceId || (namespaces[0]?.id ?? ''),
      entityName: floodEntity || 'orders',
      count: floodCount,
      reason: floodReason,
    };
    dlqFloodMutation.mutate(req, {
      onSuccess: () => {
        setFloodResult(`✅ Injected ${floodCount} messages into ${req.entityName} DLQ`);
      },
    });
  };

  const handleReset = () => {
    resetMutation.mutate(undefined, {
      onSuccess: () => {
        setShowResetConfirm(false);
        setResetResult('✅ Simulator reset — 3 namespaces, seeded data restored');
      },
    });
  };

  return (
    <div className="p-6 max-w-5xl mx-auto space-y-8">
      {/* SECTION 0 — Banner */}
      <div className="flex items-center justify-between gap-4 px-5 py-3 bg-amber-50 border border-amber-200 rounded-xl">
        <div className="flex items-center gap-3">
          <FlaskConical className="w-5 h-5 text-amber-600 shrink-0" />
          <span className="text-sm font-semibold text-amber-800">
            ⚗️ Simulator Mode — All data is synthetic. No real cloud credentials required.
          </span>
        </div>
        <div className="flex items-center gap-3 shrink-0">
          {isLoading && <RefreshCw className="w-4 h-4 text-amber-500 animate-spin" />}
          {status?.simulatedUtcNow && (
            <span className="text-xs text-amber-700 font-mono whitespace-nowrap">
              Simulated time: {formatUtc(status.simulatedUtcNow)}
            </span>
          )}
          <button
            onClick={() => refetch()}
            className="p-1.5 hover:bg-amber-100 rounded-lg text-amber-600 transition-colors"
            title="Refresh status"
          >
            <RefreshCw className="w-3.5 h-3.5" />
          </button>
        </div>
      </div>

      {/* SECTION 1 — Provider Status Cards */}
      <div>
        <h2 className="text-base font-semibold text-gray-700 mb-3">Provider Status</h2>
        {isLoading ? (
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            {[0, 1, 2].map((i) => (
              <div key={i} className="rounded-xl border border-gray-200 bg-gray-50 p-5 animate-pulse h-36" />
            ))}
          </div>
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            {namespaces.length > 0
              ? namespaces.map((ns) => <ProviderCard key={ns.id} ns={ns} />)
              : ['Azure', 'AWS', 'GCP'].map((p) => (
                  <div key={p} className="rounded-xl border border-gray-200 bg-gray-50 p-5 text-center text-sm text-gray-400">
                    {p} — no data
                  </div>
                ))}
          </div>
        )}
      </div>

      {/* SECTION 2 — Fault Injection */}
      <div className="rounded-xl border border-gray-200 bg-white shadow-sm overflow-hidden">
        <div className="px-6 py-4 border-b border-gray-200 bg-gray-50">
          <div className="flex items-center gap-2">
            <Zap className="w-5 h-5 text-red-500" />
            <h2 className="text-base font-semibold text-gray-900">Fault Injection</h2>
          </div>
          <p className="text-xs text-gray-500 mt-0.5">Simulate real-world failure conditions to test your application.</p>
        </div>
        <div className="p-6 grid grid-cols-1 md:grid-cols-2 gap-4">
          {/* Fault Type */}
          <div>
            <label className="block text-xs font-semibold text-gray-600 uppercase mb-1">
              Fault Type
            </label>
            <div className="relative">
              <select
                value={faultType}
                onChange={(e) => setFaultType(e.target.value as InjectFaultRequest['faultType'])}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm bg-white focus:ring-2 focus:ring-red-400 focus:border-red-400 appearance-none"
              >
                {FAULT_TYPES.map((f) => (
                  <option key={f.value} value={f.value}>{f.label}</option>
                ))}
              </select>
              <ChevronDown className="absolute right-2 top-2.5 w-4 h-4 text-gray-400 pointer-events-none" />
            </div>
            <p className="text-xs text-gray-400 mt-1">
              {FAULT_TYPES.find((f) => f.value === faultType)?.description}
            </p>
          </div>

          {/* Target Entity */}
          <div>
            <label className="block text-xs font-semibold text-gray-600 uppercase mb-1">
              Target Entity
            </label>
            <input
              type="text"
              value={faultTarget}
              onChange={(e) => setFaultTarget(e.target.value)}
              placeholder="orders / checkout-queue / fulfillment-sub"
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm bg-white focus:ring-2 focus:ring-red-400 focus:border-red-400"
            />
            <p className="text-xs text-gray-400 mt-1">Leave blank to affect all entities.</p>
          </div>

          {/* Namespace */}
          <div>
            <label className="block text-xs font-semibold text-gray-600 uppercase mb-1">
              Namespace
            </label>
            <div className="relative">
              <select
                value={faultNamespaceId}
                onChange={(e) => setFaultNamespaceId(e.target.value)}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm bg-white focus:ring-2 focus:ring-red-400 focus:border-red-400 appearance-none"
              >
                <option value="">All namespaces</option>
                {namespaces.map((ns) => (
                  <option key={ns.id} value={ns.id}>
                    {ns.provider} — {ns.name}
                  </option>
                ))}
              </select>
              <ChevronDown className="absolute right-2 top-2.5 w-4 h-4 text-gray-400 pointer-events-none" />
            </div>
          </div>

          {/* Severity */}
          <div>
            <label className="block text-xs font-semibold text-gray-600 uppercase mb-1">
              Severity: <span className="text-red-600 font-bold">{faultSeverity}</span> / 10
            </label>
            <input
              type="range"
              min={1}
              max={10}
              value={faultSeverity}
              onChange={(e) => setFaultSeverity(Number(e.target.value))}
              className="w-full accent-red-500"
            />
            <div className="flex justify-between text-xs text-gray-400 mt-0.5">
              <span>1 (low)</span>
              <span>10 (critical)</span>
            </div>
          </div>

          {/* Duration */}
          <div>
            <label className="block text-xs font-semibold text-gray-600 uppercase mb-1">
              Duration (seconds)
            </label>
            <input
              type="number"
              min={1}
              max={3600}
              value={faultDuration}
              onChange={(e) => setFaultDuration(Number(e.target.value))}
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm bg-white focus:ring-2 focus:ring-red-400 focus:border-red-400"
            />
          </div>

          {/* Inject Button */}
          <div className="flex items-end">
            <button
              onClick={handleInjectFault}
              disabled={injectFaultMutation.isPending}
              className="flex items-center gap-2 px-5 py-2.5 bg-red-600 hover:bg-red-700 text-white rounded-lg text-sm font-semibold transition-colors disabled:opacity-60 w-full justify-center"
            >
              {injectFaultMutation.isPending
                ? <RefreshCw className="w-4 h-4 animate-spin" />
                : <Zap className="w-4 h-4" />}
              ⚡ Inject Fault
            </button>
          </div>
        </div>

        {/* Active Faults Table */}
        {(status?.activeFaultCount ?? 0) > 0 && (
          <div className="px-6 pb-6">
            <div className="flex items-center justify-between mb-2">
              <h3 className="text-sm font-semibold text-gray-700">
                Active Faults ({status!.activeFaultCount})
              </h3>
              <button
                onClick={() => clearFaultsMutation.mutate()}
                disabled={clearFaultsMutation.isPending}
                className="flex items-center gap-1.5 px-3 py-1.5 border border-red-200 text-red-600 hover:bg-red-50 rounded-lg text-xs font-medium transition-colors disabled:opacity-60"
              >
                <Trash2 className="w-3.5 h-3.5" />
                Clear All Faults
              </button>
            </div>
            <div className="overflow-x-auto rounded-lg border border-red-100">
              <table className="w-full text-xs">
                <thead className="bg-red-50 text-red-700 font-semibold uppercase tracking-wider">
                  <tr>
                    <th className="px-3 py-2 text-left">Type</th>
                    <th className="px-3 py-2 text-left">Target</th>
                    <th className="px-3 py-2 text-center">Severity</th>
                    <th className="px-3 py-2 text-left">Expires</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-red-50">
                  {status!.activeFaults.map((f, i) => (
                    <tr key={i} className="bg-white">
                      <td className="px-3 py-2 font-mono">{f.faultType}</td>
                      <td className="px-3 py-2 text-gray-600">{f.targetEntity || '(all)'}</td>
                      <td className="px-3 py-2 text-center font-bold text-red-600">{f.severity}</td>
                      <td className="px-3 py-2 text-gray-500">
                        {new Date(f.expiresAt).toLocaleTimeString()}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </div>

      {/* SECTION 3 — Time Control */}
      <div className="rounded-xl border border-gray-200 bg-white shadow-sm overflow-hidden">
        <div className="px-6 py-4 border-b border-gray-200 bg-gray-50">
          <div className="flex items-center gap-2">
            <Clock className="w-5 h-5 text-blue-500" />
            <h2 className="text-base font-semibold text-gray-900">Simulated Time</h2>
          </div>
        </div>
        <div className="p-6">
          <div className="text-2xl font-mono font-bold text-gray-900 mb-5 tabular-nums">
            {status?.simulatedUtcNow ? formatUtc(status.simulatedUtcNow) : '—'}
          </div>
          <div className="flex flex-wrap gap-3 mb-4">
            {[
              { label: 'Advance 5 min', seconds: 300 },
              { label: 'Advance 1 hour', seconds: 3600 },
              { label: 'Advance 24 hours', seconds: 86400 },
            ].map(({ label, seconds }) => (
              <button
                key={seconds}
                onClick={() => advanceTimeMutation.mutate(seconds)}
                disabled={advanceTimeMutation.isPending}
                className="flex items-center gap-2 px-4 py-2 border border-blue-200 bg-blue-50 hover:bg-blue-100 text-blue-700 rounded-lg text-sm font-medium transition-colors disabled:opacity-60"
              >
                {advanceTimeMutation.isPending && advanceTimeMutation.variables === seconds
                  ? <RefreshCw className="w-3.5 h-3.5 animate-spin" />
                  : <Clock className="w-3.5 h-3.5" />}
                {label}
              </button>
            ))}
          </div>
          <p className="text-xs text-gray-500">
            Advancing time triggers AWS visibility window expiry and GCP ack deadline expiry.
          </p>
        </div>
      </div>

      {/* SECTION 4 — DLQ Flood Injector */}
      <div className="rounded-xl border border-gray-200 bg-white shadow-sm overflow-hidden">
        <div className="px-6 py-4 border-b border-gray-200 bg-gray-50">
          <div className="flex items-center gap-2">
            <AlertCircle className="w-5 h-5 text-orange-500" />
            <h2 className="text-base font-semibold text-gray-900">DLQ Flood</h2>
          </div>
          <p className="text-xs text-gray-500 mt-0.5">Inject realistic DLQ messages to test your dashboard and forensic engine.</p>
        </div>
        <div className="p-6 grid grid-cols-1 md:grid-cols-2 gap-4">
          {/* Namespace */}
          <div>
            <label className="block text-xs font-semibold text-gray-600 uppercase mb-1">Namespace</label>
            <div className="relative">
              <select
                value={floodNamespaceId}
                onChange={(e) => setFloodNamespaceId(e.target.value)}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm bg-white focus:ring-2 focus:ring-orange-400 focus:border-orange-400 appearance-none"
              >
                <option value="">First namespace</option>
                {namespaces.map((ns) => (
                  <option key={ns.id} value={ns.id}>{ns.provider} — {ns.name}</option>
                ))}
              </select>
              <ChevronDown className="absolute right-2 top-2.5 w-4 h-4 text-gray-400 pointer-events-none" />
            </div>
          </div>

          {/* Entity */}
          <div>
            <label className="block text-xs font-semibold text-gray-600 uppercase mb-1">Entity Name</label>
            <input
              type="text"
              value={floodEntity}
              onChange={(e) => setFloodEntity(e.target.value)}
              placeholder="orders"
              className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm bg-white focus:ring-2 focus:ring-orange-400 focus:border-orange-400"
            />
          </div>

          {/* Count */}
          <div>
            <label className="block text-xs font-semibold text-gray-600 uppercase mb-1">
              Count: <span className="text-orange-600 font-bold">{floodCount}</span>
            </label>
            <input
              type="range"
              min={1}
              max={50}
              value={floodCount}
              onChange={(e) => setFloodCount(Number(e.target.value))}
              className="w-full accent-orange-500"
            />
            <div className="flex justify-between text-xs text-gray-400 mt-0.5">
              <span>1</span><span>50</span>
            </div>
          </div>

          {/* Reason */}
          <div>
            <label className="block text-xs font-semibold text-gray-600 uppercase mb-1">Reason</label>
            <div className="relative">
              <select
                value={floodReason}
                onChange={(e) => setFloodReason(e.target.value)}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm bg-white focus:ring-2 focus:ring-orange-400 focus:border-orange-400 appearance-none"
              >
                {DLQ_REASONS.map((r) => <option key={r} value={r}>{r}</option>)}
              </select>
              <ChevronDown className="absolute right-2 top-2.5 w-4 h-4 text-gray-400 pointer-events-none" />
            </div>
          </div>

          {/* Inject Button */}
          <div className="md:col-span-2 flex items-center gap-4">
            <button
              onClick={handleDlqFlood}
              disabled={dlqFloodMutation.isPending}
              className="flex items-center gap-2 px-5 py-2.5 bg-orange-600 hover:bg-orange-700 text-white rounded-lg text-sm font-semibold transition-colors disabled:opacity-60"
            >
              {dlqFloodMutation.isPending
                ? <RefreshCw className="w-4 h-4 animate-spin" />
                : <AlertCircle className="w-4 h-4" />}
              💥 Inject DLQ Flood
            </button>
            {floodResult && (
              <span className="text-sm text-green-700 font-medium">{floodResult}</span>
            )}
          </div>
        </div>
      </div>

      {/* SECTION 5 — Reset */}
      <div className="rounded-xl border border-red-200 bg-white shadow-sm overflow-hidden">
        <div className="px-6 py-4 border-b border-red-100 bg-red-50">
          <div className="flex items-center gap-2">
            <RotateCcw className="w-5 h-5 text-red-500" />
            <h2 className="text-base font-semibold text-gray-900">Reset Simulator</h2>
          </div>
          <p className="text-xs text-gray-500 mt-0.5">
            Wipe all message state and reseed with the default dataset (Azure + AWS + GCP).
          </p>
        </div>
        <div className="p-6 flex items-center gap-4">
          <button
            onClick={() => setShowResetConfirm(true)}
            className="flex items-center gap-2 px-6 py-3 bg-red-600 hover:bg-red-700 text-white rounded-lg text-sm font-bold transition-colors"
          >
            <RotateCcw className="w-4 h-4" />
            🔄 Reset &amp; Reseed
          </button>
          {resetResult && (
            <span className="text-sm text-green-700 font-medium">{resetResult}</span>
          )}
        </div>
      </div>

      {/* Reset Confirmation Modal */}
      {showResetConfirm && (
        <ConfirmResetDialog
          onConfirm={handleReset}
          onCancel={() => setShowResetConfirm(false)}
          isPending={resetMutation.isPending}
        />
      )}
    </div>
  );
}
