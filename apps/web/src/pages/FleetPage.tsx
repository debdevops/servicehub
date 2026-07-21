import { useMemo, useState, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  Layers,
  AlertTriangle,
  Inbox,
  CheckCircle,
  RefreshCw,
  Clock,
  ArrowRight,
} from 'lucide-react';
import { LineChart, Line, XAxis, Tooltip, ResponsiveContainer, CartesianGrid } from 'recharts';
import { useFleetOverview } from '@/hooks/useFleet';
import type { FleetHealthSeverity, FleetNamespaceHealth } from '@/lib/api/fleet';

const WINDOW_OPTIONS = [
  { label: '24h', hours: 24 },
  { label: '3d', hours: 72 },
  { label: '7d', hours: 168 },
];

const severityStyles: Record<FleetHealthSeverity, { dot: string; text: string; label: string }> = {
  Critical: { dot: 'bg-red-500', text: 'text-red-700', label: 'Critical' },
  Warning: { dot: 'bg-amber-500', text: 'text-amber-700', label: 'Warning' },
  Healthy: { dot: 'bg-emerald-500', text: 'text-emerald-700', label: 'Healthy' },
};

function relativeAge(iso: string | null): string {
  if (!iso) return '—';
  const ms = Date.now() - new Date(iso).getTime();
  if (ms < 0) return 'just now';
  const mins = Math.floor(ms / 60000);
  if (mins < 60) return `${mins}m`;
  const hours = Math.floor(mins / 60);
  if (hours < 24) return `${hours}h`;
  return `${Math.floor(hours / 24)}d`;
}

function StatTile({
  icon,
  label,
  value,
  tone,
}: {
  icon: ReactNode;
  label: string;
  value: number | string;
  tone: string;
}) {
  return (
    <div className="bg-white rounded-xl border border-gray-200 shadow-sm p-4 flex items-center gap-3">
      <div className={`w-10 h-10 rounded-lg flex items-center justify-center ${tone}`}>{icon}</div>
      <div>
        <div className="text-2xl font-semibold text-gray-900 leading-none">{value}</div>
        <div className="text-xs text-gray-500 mt-1">{label}</div>
      </div>
    </div>
  );
}

export default function FleetPage() {
  const navigate = useNavigate();
  const [windowHours, setWindowHours] = useState(24);
  const { data, isLoading, isError, refetch, isFetching } = useFleetOverview(windowHours);

  const atRisk = useMemo(
    () => (data?.namespaces ?? []).filter((n) => n.severity !== 'Healthy').length,
    [data]
  );

  const topCategories = useMemo(
    () => Object.entries(data?.topCategories ?? {}).sort((a, b) => b[1] - a[1]).slice(0, 6),
    [data]
  );

  const goToNamespace = (n: FleetNamespaceHealth) =>
    navigate(`/dlq-history?namespace=${n.namespaceId}`);

  return (
    <div className="p-6 max-w-7xl mx-auto space-y-6">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded-lg bg-indigo-50 flex items-center justify-center">
            <Layers className="w-5 h-5 text-indigo-600" />
          </div>
          <div>
            <h1 className="text-xl font-semibold text-gray-900">Fleet Operations</h1>
            <p className="text-sm text-gray-500">
              Dead-letter health across every namespace — what died overnight, at a glance.
            </p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <div className="inline-flex rounded-lg border border-gray-200 bg-white p-0.5">
            {WINDOW_OPTIONS.map((opt) => (
              <button
                key={opt.hours}
                onClick={() => setWindowHours(opt.hours)}
                className={`px-3 py-1.5 text-sm rounded-md transition-colors ${
                  windowHours === opt.hours
                    ? 'bg-indigo-600 text-white'
                    : 'text-gray-600 hover:bg-gray-50'
                }`}
                aria-pressed={windowHours === opt.hours}
              >
                {opt.label}
              </button>
            ))}
          </div>
          <button
            onClick={() => refetch()}
            className="p-2 rounded-lg border border-gray-200 bg-white text-gray-600 hover:bg-gray-50"
            aria-label="Refresh fleet overview"
          >
            <RefreshCw className={`w-4 h-4 ${isFetching ? 'animate-spin' : ''}`} />
          </button>
        </div>
      </div>

      {isError && (
        <div className="bg-red-50 border border-red-200 text-red-700 rounded-lg p-4 text-sm">
          Failed to load the fleet overview. Please try again.
        </div>
      )}

      {isLoading && !data && (
        <div className="text-center text-gray-500 py-16">Loading fleet overview…</div>
      )}

      {data && (
        <>
          {/* Summary tiles */}
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
            <StatTile
              icon={<Inbox className="w-5 h-5 text-purple-600" />}
              label="Active dead-letters"
              value={data.totalActive}
              tone="bg-purple-50"
            />
            <StatTile
              icon={<AlertTriangle className="w-5 h-5 text-red-600" />}
              label={`New in last ${data.windowHours}h`}
              value={data.totalNewInWindow}
              tone="bg-red-50"
            />
            <StatTile
              icon={<CheckCircle className="w-5 h-5 text-emerald-600" />}
              label={`Resolved in ${data.windowHours}h`}
              value={data.totalResolvedInWindow}
              tone="bg-emerald-50"
            />
            <StatTile
              icon={<Layers className="w-5 h-5 text-indigo-600" />}
              label={`Namespaces at risk (of ${data.namespaceCount})`}
              value={atRisk}
              tone="bg-indigo-50"
            />
          </div>

          {/* Trend + categories */}
          <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
            <div className="lg:col-span-2 bg-white rounded-xl border border-gray-200 shadow-sm p-4">
              <h2 className="text-sm font-medium text-gray-700 mb-3">7-day fleet trend</h2>
              <div style={{ width: '100%', height: 180 }}>
                <ResponsiveContainer>
                  <LineChart data={data.dailyTrend}>
                    <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
                    <XAxis
                      dataKey="date"
                      tickFormatter={(d: string) => new Date(d).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}
                      tick={{ fontSize: 11, fill: '#9ca3af' }}
                    />
                    <Tooltip
                      labelFormatter={(d) => new Date(d as string).toLocaleDateString()}
                      formatter={(value, name) => [value as number, name === 'newMessages' ? 'New' : 'Resolved']}
                    />
                    <Line type="monotone" dataKey="newMessages" stroke="#ef4444" strokeWidth={2} dot={false} />
                    <Line type="monotone" dataKey="resolvedMessages" stroke="#10b981" strokeWidth={2} dot={false} />
                  </LineChart>
                </ResponsiveContainer>
              </div>
            </div>
            <div className="bg-white rounded-xl border border-gray-200 shadow-sm p-4">
              <h2 className="text-sm font-medium text-gray-700 mb-3">Top failure categories</h2>
              {topCategories.length === 0 ? (
                <p className="text-sm text-gray-400">No active failures 🎉</p>
              ) : (
                <ul className="space-y-2">
                  {topCategories.map(([cat, count]) => (
                    <li key={cat} className="flex items-center justify-between text-sm">
                      <span className="text-gray-700">{cat}</span>
                      <span className="text-xs font-medium bg-gray-100 text-gray-700 px-2 py-0.5 rounded-full">
                        {count}
                      </span>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </div>

          {/* Per-namespace health */}
          <div className="bg-white rounded-xl border border-gray-200 shadow-sm overflow-hidden">
            <div className="px-4 py-3 border-b border-gray-100">
              <h2 className="text-sm font-medium text-gray-700">Namespaces (worst first)</h2>
            </div>
            {data.namespaces.length === 0 ? (
              <div className="p-8 text-center text-gray-400 text-sm">No namespaces to report.</div>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="text-left text-xs text-gray-500 border-b border-gray-100">
                      <th className="px-4 py-2 font-medium">Namespace</th>
                      <th className="px-4 py-2 font-medium">Env</th>
                      <th className="px-4 py-2 font-medium text-right">Active</th>
                      <th className="px-4 py-2 font-medium text-right">New</th>
                      <th className="px-4 py-2 font-medium">Top entity</th>
                      <th className="px-4 py-2 font-medium">Top category</th>
                      <th className="px-4 py-2 font-medium">Oldest</th>
                      <th className="px-4 py-2"></th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.namespaces.map((n) => {
                      const sev = severityStyles[n.severity];
                      return (
                        <tr
                          key={n.namespaceId}
                          onClick={() => goToNamespace(n)}
                          className="border-b border-gray-50 hover:bg-indigo-50/40 cursor-pointer"
                        >
                          <td className="px-4 py-2.5">
                            <div className="flex items-center gap-2">
                              <span className={`w-2 h-2 rounded-full ${sev.dot}`} title={sev.label} />
                              <span className="font-medium text-gray-800">{n.namespaceName}</span>
                              <span className="text-xs text-gray-400">{n.provider}</span>
                            </div>
                          </td>
                          <td className="px-4 py-2.5 text-gray-500">{n.environment}</td>
                          <td className="px-4 py-2.5 text-right font-medium text-gray-800">{n.activeCount}</td>
                          <td className={`px-4 py-2.5 text-right ${n.newInWindow > 0 ? 'text-red-600 font-medium' : 'text-gray-400'}`}>
                            {n.newInWindow > 0 ? `+${n.newInWindow}` : '0'}
                          </td>
                          <td className="px-4 py-2.5 text-gray-600 truncate max-w-[10rem]">
                            {n.topEntity ?? '—'}
                            {n.topEntityCount > 0 && (
                              <span className="text-xs text-gray-400"> ({n.topEntityCount})</span>
                            )}
                          </td>
                          <td className="px-4 py-2.5 text-gray-600">{n.topCategory ?? '—'}</td>
                          <td className="px-4 py-2.5 text-gray-500">
                            <span className="inline-flex items-center gap-1">
                              <Clock className="w-3 h-3" />
                              {relativeAge(n.oldestActiveDetectedAt)}
                            </span>
                          </td>
                          <td className="px-4 py-2.5 text-right">
                            <ArrowRight className="w-4 h-4 text-gray-300" />
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </>
      )}
    </div>
  );
}
