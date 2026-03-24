import { useHealthVersion, useHealthStatus } from '@/hooks/useHealth';
import {
  Activity,
  Server,
  Cpu,
  HardDrive,
  Clock,
  RefreshCw,
} from 'lucide-react';

function formatUptime(isoDuration: string): string {
  // Parse .NET TimeSpan format: "d.hh:mm:ss.fffffff" or "hh:mm:ss.fffffff"
  const match = isoDuration.match(
    /^(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2})/
  );
  if (!match) return isoDuration;
  const [, days, hours, minutes, seconds] = match;
  const parts: string[] = [];
  if (days && Number(days) > 0) parts.push(`${days}d`);
  if (Number(hours) > 0) parts.push(`${hours}h`);
  parts.push(`${minutes}m`);
  parts.push(`${seconds}s`);
  return parts.join(' ');
}

function StatCard({
  icon: Icon,
  label,
  value,
  detail,
  color,
}: {
  icon: React.ElementType;
  label: string;
  value: string | number;
  detail?: string;
  color: string;
}) {
  return (
    <div className="bg-white rounded-xl border border-gray-200 p-5 shadow-sm">
      <div className="flex items-center gap-3 mb-3">
        <div className={`p-2 rounded-lg ${color}`}>
          <Icon className="w-5 h-5 text-white" />
        </div>
        <span className="text-sm font-medium text-gray-500">{label}</span>
      </div>
      <p className="text-2xl font-bold text-gray-900">{value}</p>
      {detail && (
        <p className="text-xs text-gray-400 mt-1">{detail}</p>
      )}
    </div>
  );
}

export function HealthPage() {
  const {
    data: version,
    isLoading: versionLoading,
    error: versionError,
  } = useHealthVersion();
  const {
    data: status,
    isLoading: statusLoading,
    error: statusError,
    refetch,
  } = useHealthStatus();

  const isLoading = versionLoading || statusLoading;
  const hasError = versionError || statusError;

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      {/* Header */}
      <div className="bg-gradient-to-r from-emerald-600 to-emerald-500 px-6 py-4 shrink-0">
        <div className="flex items-center justify-between">
          <h1 className="text-xl font-semibold text-white">
            System Health
          </h1>
          <button
            onClick={() => refetch()}
            className="flex items-center gap-2 px-3 py-1.5 bg-white/20 hover:bg-white/30 rounded-lg text-white text-sm transition-colors"
          >
            <RefreshCw className="w-4 h-4" />
            Refresh
          </button>
        </div>
      </div>

      {/* Content */}
      <div className="flex-1 overflow-auto p-6 bg-gray-50/50">
        {isLoading && (
          <div className="flex items-center justify-center py-20 text-gray-500">
            <RefreshCw className="w-5 h-5 animate-spin mr-2" />
            Loading health data...
          </div>
        )}

        {hasError && !isLoading && (
          <div className="bg-red-50 border border-red-200 rounded-xl p-6 text-center">
            <p className="text-red-700 font-medium">
              Unable to reach the API server
            </p>
            <p className="text-red-500 text-sm mt-1">
              Ensure the backend is running and try again.
            </p>
          </div>
        )}

        {!isLoading && !hasError && (
          <>
            {/* Health badge */}
            <div className="flex items-center gap-3 mb-6">
              <span
                className={`inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-sm font-medium ${
                  status?.isHealthy
                    ? 'bg-green-100 text-green-700'
                    : 'bg-red-100 text-red-700'
                }`}
              >
                <span
                  className={`w-2 h-2 rounded-full ${
                    status?.isHealthy ? 'bg-green-500' : 'bg-red-500'
                  }`}
                />
                {status?.isHealthy ? 'Healthy' : 'Unhealthy'}
              </span>
              {status?.timestamp && (
                <span className="text-xs text-gray-400">
                  as of{' '}
                  {new Date(status.timestamp).toLocaleTimeString()}
                </span>
              )}
            </div>

            {/* Stats Grid */}
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4 mb-8">
              <StatCard
                icon={Clock}
                label="Uptime"
                value={status ? formatUptime(status.uptime) : '—'}
                color="bg-blue-500"
              />
              <StatCard
                icon={HardDrive}
                label="Memory Usage"
                value={status ? `${status.memoryUsageMb} MB` : '—'}
                detail={
                  status
                    ? `GC managed: ${status.gcTotalMemoryMb} MB`
                    : undefined
                }
                color="bg-purple-500"
              />
              <StatCard
                icon={Cpu}
                label="Threads"
                value={status?.threadCount ?? '—'}
                color="bg-amber-500"
              />
              <StatCard
                icon={Activity}
                label="GC Collections"
                value={
                  status
                    ? `${status.gen0Collections} / ${status.gen1Collections} / ${status.gen2Collections}`
                    : '—'
                }
                detail="Gen0 / Gen1 / Gen2"
                color="bg-rose-500"
              />
            </div>

            {/* Version Info */}
            {version && (
              <div className="bg-white rounded-xl border border-gray-200 shadow-sm overflow-hidden">
                <div className="px-5 py-3 bg-gray-50 border-b border-gray-200">
                  <h2 className="text-sm font-semibold text-gray-700 flex items-center gap-2">
                    <Server className="w-4 h-4 text-gray-500" />
                    Server Information
                  </h2>
                </div>
                <div className="divide-y divide-gray-100">
                  {[
                    ['Version', version.version],
                    ['Build', version.informationalVersion],
                    ['Environment', version.environment],
                    ['Machine', version.machineName],
                    ['OS', version.osDescription],
                    ['Framework', version.frameworkDescription],
                    [
                      'Started',
                      new Date(version.startedAt).toLocaleString(),
                    ],
                  ].map(([label, value]) => (
                    <div
                      key={label}
                      className="flex items-center px-5 py-2.5 text-sm"
                    >
                      <span className="w-32 text-gray-500 font-medium">
                        {label}
                      </span>
                      <span className="text-gray-900">{value}</span>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}
