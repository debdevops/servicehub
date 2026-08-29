import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  Sparkles,
  Network,
  TrendingUp,
  FileOutput,
  Loader2,
  Info,
  Copy,
  Check,
} from 'lucide-react';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import {
  useGenerateNarrations,
  useDetectCorrelationFindings,
  useForecastBacklog,
  useExportContractViolations,
} from '@servicehub/ui-shared/hooks/useProactiveInsights';
import type {
  NarrationInfo,
  CorrelationFindingInfo,
  BacklogForecastInfo,
} from '@servicehub/ui-shared/lib/api/proactiveInsights';

type InsightTab = 'narrations' | 'correlations' | 'forecasts' | 'contract-export';

const TABS: { key: InsightTab; label: string; icon: typeof Sparkles }[] = [
  { key: 'narrations', label: 'Narrations', icon: Sparkles },
  { key: 'correlations', label: 'Correlation Findings', icon: Network },
  { key: 'forecasts', label: 'Backlog Forecasts', icon: TrendingUp },
  { key: 'contract-export', label: 'Contract Violations', icon: FileOutput },
];

function severityStyle(severity: number): string {
  if (severity >= 75) return 'bg-red-50 text-red-700 border-red-300';
  if (severity >= 50) return 'bg-amber-50 text-amber-700 border-amber-300';
  if (severity >= 25) return 'bg-blue-50 text-blue-700 border-blue-300';
  return 'bg-gray-100 text-gray-600 border-gray-300';
}

function SeverityBadge({ severity }: { severity: number }) {
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium border ${severityStyle(severity)}`}>
      Severity {severity}
    </span>
  );
}

function priorityStyle(priority: string): string {
  switch (priority) {
    case 'High': return 'bg-red-50 text-red-700 border-red-300';
    case 'Medium': return 'bg-amber-50 text-amber-700 border-amber-300';
    default: return 'bg-gray-100 text-gray-600 border-gray-300';
  }
}

function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short', day: '2-digit', hour: '2-digit', minute: '2-digit',
  });
}

function RecommendedActions({ actions }: { actions: string[] }) {
  if (actions.length === 0) return null;
  return (
    <ul className="mt-2 space-y-1">
      {actions.map((action, i) => (
        <li key={i} className="text-xs text-gray-600 flex items-start gap-1.5">
          <span className="text-gray-400 mt-0.5">&bull;</span>
          <span>{action}</span>
        </li>
      ))}
    </ul>
  );
}

function EmptyState({ message }: { message: string }) {
  return (
    <div className="px-4 py-10 text-center text-sm text-gray-500 bg-white border border-gray-200 rounded-lg">
      {message}
    </div>
  );
}

// ─── Narrations tab ─────────────────────────────────────────────────────────

function NarrationCard({ narration }: { narration: NarrationInfo }) {
  return (
    <div className="bg-white border border-gray-200 rounded-lg shadow-sm p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <span className="text-[10px] font-semibold uppercase tracking-wide text-gray-400">
              {narration.kind === 'CrossNamespaceCorrelation' ? 'Cross-namespace' : 'Namespace activity'}
            </span>
          </div>
          <h3 className="text-sm font-semibold text-gray-900 mt-0.5">{narration.headline}</h3>
          <p className="text-sm text-gray-600 mt-1">{narration.summary}</p>
        </div>
        <SeverityBadge severity={narration.severity} />
      </div>
      <RecommendedActions actions={narration.recommendedActions} />
      <div className="text-xs text-gray-400 mt-3">{formatDateTime(narration.generatedAt)}</div>
    </div>
  );
}

function NarrationsTab() {
  const { isDemoMode } = useDemoContext();
  const mutation = useGenerateNarrations();

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <p className="text-sm text-gray-500 max-w-2xl">
          Stitches anomaly, drift, and correlation findings from the last 24 hours into one
          plain-English narration per emergent pattern (roadmap I4).
        </p>
        <button
          onClick={() => mutation.mutate()}
          disabled={mutation.isPending || isDemoMode}
          className="flex items-center gap-1.5 px-3 py-2 text-sm font-medium text-white bg-blue-600 rounded-lg hover:bg-blue-700 shadow-sm transition-all disabled:opacity-50 shrink-0"
        >
          {mutation.isPending ? <Loader2 className="w-4 h-4 animate-spin" /> : <Sparkles className="w-4 h-4" />}
          Generate narrations
        </button>
      </div>

      {mutation.isSuccess && mutation.data.narrations.length === 0 && (
        <EmptyState message="No anomaly, drift, or correlation activity in the last 24 hours to narrate." />
      )}

      {mutation.isSuccess && mutation.data.narrations.length > 0 && (
        <div className="space-y-3">
          {mutation.data.narrations.map((n) => (
            <NarrationCard key={n.id} narration={n} />
          ))}
        </div>
      )}
    </div>
  );
}

// ─── Correlation findings tab ───────────────────────────────────────────────

function CorrelationCard({ finding }: { finding: CorrelationFindingInfo }) {
  const isCrossCloud = finding.providers.length > 1;
  return (
    <div className="bg-white border border-gray-200 rounded-lg shadow-sm p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2 flex-wrap">
            <span className={`text-[10px] font-semibold uppercase tracking-wide px-1.5 py-0.5 rounded border ${
              isCrossCloud ? 'bg-violet-50 text-violet-700 border-violet-300' : 'bg-gray-100 text-gray-600 border-gray-300'
            }`}>
              {isCrossCloud ? 'Cross-cloud' : 'Same-provider'}
            </span>
            {finding.providers.map((p) => (
              <span key={p} className="text-[10px] font-medium px-1.5 py-0.5 rounded bg-gray-50 text-gray-500 border border-gray-200">
                {p}
              </span>
            ))}
          </div>
          <p className="text-sm text-gray-800 mt-1.5">{finding.description}</p>
        </div>
        <SeverityBadge severity={finding.severity} />
      </div>

      <div className="mt-3 divide-y divide-gray-100 border-t border-gray-100">
        {finding.members.map((m, i) => (
          <div key={i} className="py-1.5 flex items-center justify-between text-xs text-gray-600">
            <span className="font-mono">{m.entityName}</span>
            <span className="text-gray-400">{m.provider} &middot; {m.anomalyType} &middot; sev {m.severity}</span>
          </div>
        ))}
      </div>

      <RecommendedActions actions={finding.recommendedActions} />
      <div className="text-xs text-gray-400 mt-3">{formatDateTime(finding.detectedAt)}</div>
    </div>
  );
}

function CorrelationsTab() {
  const { isDemoMode } = useDemoContext();
  const mutation = useDetectCorrelationFindings();

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <p className="text-sm text-gray-500 max-w-2xl">
          Detects anomalies that fired together across two or more namespaces in the last 24
          hours — same-provider (C1) or across clouds (C2) — before anyone has to notice by hand.
        </p>
        <button
          onClick={() => mutation.mutate()}
          disabled={mutation.isPending || isDemoMode}
          className="flex items-center gap-1.5 px-3 py-2 text-sm font-medium text-white bg-violet-600 rounded-lg hover:bg-violet-700 shadow-sm transition-all disabled:opacity-50 shrink-0"
        >
          {mutation.isPending ? <Loader2 className="w-4 h-4 animate-spin" /> : <Network className="w-4 h-4" />}
          Detect correlations
        </button>
      </div>

      {mutation.isSuccess && mutation.data.findings.length === 0 && (
        <EmptyState message="No correlated anomalies found across accessible namespaces in the last 24 hours." />
      )}

      {mutation.isSuccess && mutation.data.findings.length > 0 && (
        <div className="space-y-3">
          {mutation.data.findings.map((f) => (
            <CorrelationCard key={f.id} finding={f} />
          ))}
        </div>
      )}
    </div>
  );
}

// ─── Namespace picker (shared by the namespace-scoped tabs) ────────────────

function useSelectedNamespace() {
  const [searchParams] = useSearchParams();
  const { data: namespaces, isLoading } = useNamespaces();
  const active = (namespaces ?? []).filter((ns) => ns.isActive);
  const [namespaceId, setNamespaceId] = useState<string>(searchParams.get('namespace') ?? '');

  useEffect(() => {
    if (!namespaceId && active.length > 0) {
      setNamespaceId(active[0].id);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [active.length]);

  return { namespaces: active, isLoading, namespaceId, setNamespaceId };
}

function NamespaceSelect({
  namespaces,
  value,
  onChange,
}: {
  namespaces: { id: string; name: string; displayName?: string }[];
  value: string;
  onChange: (id: string) => void;
}) {
  return (
    <select
      value={value}
      onChange={(e) => onChange(e.target.value)}
      className="px-3 py-2 text-sm border border-gray-200 rounded-lg bg-white text-gray-700 shadow-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
    >
      {namespaces.length === 0 && <option value="">No namespaces</option>}
      {namespaces.map((ns) => (
        <option key={ns.id} value={ns.id}>{ns.displayName || ns.name}</option>
      ))}
    </select>
  );
}

// ─── Backlog forecasts tab ──────────────────────────────────────────────────

function ForecastRow({ forecast }: { forecast: BacklogForecastInfo }) {
  return (
    <tr className="hover:bg-gray-50">
      <td className="px-4 py-2.5 text-sm font-mono text-gray-700">{forecast.entityName}</td>
      <td className="px-4 py-2.5 text-xs text-gray-600">{forecast.currentBacklogCount}</td>
      <td className="px-4 py-2.5 text-xs text-gray-600">{forecast.growthRatePerHour.toFixed(1)}/hr</td>
      <td className="px-4 py-2.5 text-xs text-gray-600">{forecast.alertThreshold}</td>
      <td className="px-4 py-2.5 text-xs text-gray-600">
        ~{forecast.projectedHoursToBreach.toFixed(1)}h &mdash; {formatDateTime(forecast.projectedBreachAtUtc)}
      </td>
      <td className="px-4 py-2.5"><SeverityBadge severity={forecast.severity} /></td>
    </tr>
  );
}

function ForecastsTab() {
  const { isDemoMode } = useDemoContext();
  const { namespaces, isLoading: namespacesLoading, namespaceId, setNamespaceId } = useSelectedNamespace();
  const mutation = useForecastBacklog();

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between flex-wrap gap-3">
        <p className="text-sm text-gray-500 max-w-2xl">
          Projects, per entity, how many hours remain before its DLQ backlog crosses your alert
          threshold — arithmetic growth-rate extrapolation, not ML (roadmap P4).
        </p>
        <div className="flex items-center gap-2 shrink-0">
          <NamespaceSelect namespaces={namespaces} value={namespaceId} onChange={setNamespaceId} />
          <button
            onClick={() => namespaceId && mutation.mutate({ namespaceId })}
            disabled={mutation.isPending || !namespaceId || namespacesLoading || isDemoMode}
            className="flex items-center gap-1.5 px-3 py-2 text-sm font-medium text-white bg-emerald-600 rounded-lg hover:bg-emerald-700 shadow-sm transition-all disabled:opacity-50"
          >
            {mutation.isPending ? <Loader2 className="w-4 h-4 animate-spin" /> : <TrendingUp className="w-4 h-4" />}
            Forecast
          </button>
        </div>
      </div>

      {mutation.isSuccess && mutation.data.forecasts.length === 0 && (
        <EmptyState message="No entity in this namespace is currently projected to breach its alert threshold." />
      )}

      {mutation.isSuccess && mutation.data.forecasts.length > 0 && (
        <div className="bg-white border border-gray-200 rounded-lg shadow-sm overflow-x-auto">
          <table className="w-full text-sm" aria-label="Backlog forecasts">
            <thead className="bg-gray-50 border-b border-gray-200">
              <tr>
                <th scope="col" className="px-4 py-2 text-left text-xs font-semibold text-gray-500">Entity</th>
                <th scope="col" className="px-4 py-2 text-left text-xs font-semibold text-gray-500">Current backlog</th>
                <th scope="col" className="px-4 py-2 text-left text-xs font-semibold text-gray-500">Growth rate</th>
                <th scope="col" className="px-4 py-2 text-left text-xs font-semibold text-gray-500">Threshold</th>
                <th scope="col" className="px-4 py-2 text-left text-xs font-semibold text-gray-500">Projected breach</th>
                <th scope="col" className="px-4 py-2 text-left text-xs font-semibold text-gray-500">Severity</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {mutation.data.forecasts.map((f) => (
                <ForecastRow key={f.id} forecast={f} />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

// ─── Contract-violation export tab ──────────────────────────────────────────

function ContractExportTab() {
  const { isDemoMode } = useDemoContext();
  const { namespaces, isLoading: namespacesLoading, namespaceId, setNamespaceId } = useSelectedNamespace();
  const mutation = useExportContractViolations();
  const [copied, setCopied] = useState(false);

  const handleCopy = async () => {
    if (!mutation.data) return;
    try {
      await navigator.clipboard.writeText(mutation.data.markdownReport);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // Clipboard access denied — nothing to fall back to here; the report text is still on screen.
    }
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between flex-wrap gap-3">
        <p className="text-sm text-gray-500 max-w-2xl">
          Packages P2's message-shape drift findings from the last 24 hours as a producer-facing
          report — ready to hand to the upstream team that can fix the root cause (roadmap P3).
        </p>
        <div className="flex items-center gap-2 shrink-0">
          <NamespaceSelect namespaces={namespaces} value={namespaceId} onChange={setNamespaceId} />
          <button
            onClick={() => namespaceId && mutation.mutate({ namespaceId })}
            disabled={mutation.isPending || !namespaceId || namespacesLoading || isDemoMode}
            className="flex items-center gap-1.5 px-3 py-2 text-sm font-medium text-white bg-orange-600 rounded-lg hover:bg-orange-700 shadow-sm transition-all disabled:opacity-50"
          >
            {mutation.isPending ? <Loader2 className="w-4 h-4 animate-spin" /> : <FileOutput className="w-4 h-4" />}
            Generate export
          </button>
        </div>
      </div>

      {mutation.isSuccess && mutation.data.violations.length === 0 && (
        <EmptyState message="No contract violations detected in this namespace in the last 24 hours." />
      )}

      {mutation.isSuccess && mutation.data.violations.length > 0 && (
        <>
          <div className="space-y-3">
            {mutation.data.violations.map((v, i) => (
              <div key={i} className="bg-white border border-gray-200 rounded-lg shadow-sm p-4">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="text-sm font-mono text-gray-700">{v.entityName}</div>
                    <div className="text-sm text-gray-800 mt-0.5">{v.violationType}</div>
                  </div>
                  <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium border shrink-0 ${priorityStyle(v.priority)}`}>
                    {v.priority}
                  </span>
                </div>
                <p className="text-xs text-gray-500 mt-2">{v.evidence}</p>
                <RecommendedActions actions={v.suggestedFixes} />
              </div>
            ))}
          </div>

          <div className="bg-white border border-gray-200 rounded-lg shadow-sm">
            <div className="px-4 py-3 border-b border-gray-200 flex items-center justify-between">
              <h2 className="text-sm font-semibold text-gray-800">Markdown report</h2>
              <button
                onClick={handleCopy}
                className="flex items-center gap-1.5 px-2.5 py-1 text-xs font-medium text-gray-600 border border-gray-200 rounded-lg hover:bg-gray-50"
              >
                {copied ? <Check className="w-3.5 h-3.5 text-green-600" /> : <Copy className="w-3.5 h-3.5" />}
                {copied ? 'Copied' : 'Copy'}
              </button>
            </div>
            <pre className="p-4 text-xs text-gray-700 whitespace-pre-wrap overflow-x-auto max-h-96 overflow-y-auto">
              {mutation.data.markdownReport}
            </pre>
          </div>
        </>
      )}
    </div>
  );
}

// ─── Page ────────────────────────────────────────────────────────────────────

/**
 * `/insights` — proactive insights (roadmap §3): I4 auto-narration, C1/C2 correlation detection,
 * P4 predictive backlog forecasting, and P3's producer-facing contract-violation export. Every
 * tab here is compute-on-demand over the last 24 hours — none of these findings are persisted
 * server-side beyond a short-lived cache backing a follow-up detail fetch, so there is no list to
 * simply load; the operator triggers detection explicitly, matching how the underlying API works.
 */
export default function ProactiveInsightsPage() {
  const { isDemoMode } = useDemoContext();
  const [searchParams, setSearchParams] = useSearchParams();
  const tab: InsightTab = (['narrations', 'correlations', 'forecasts', 'contract-export'] as const)
    .includes(searchParams.get('tab') as InsightTab)
    ? (searchParams.get('tab') as InsightTab)
    : 'narrations';

  const setTab = (next: InsightTab) => setSearchParams({ tab: next }, { replace: true });

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      <div className="bg-white border-b border-gray-200 px-6 py-4 shrink-0">
        <h1 className="text-xl font-bold text-gray-900 flex items-center gap-2">
          <Sparkles className="w-5 h-5 text-blue-600" />
          Proactive Insights
        </h1>
        <p className="text-sm text-gray-500 mt-0.5">
          What ServiceHub noticed without being asked — narrated, correlated, and forecast, so
          nothing waits to be found by hand.
        </p>

        {isDemoMode && (
          <div className="mt-3 flex items-center gap-2 px-3 py-2 rounded-lg bg-amber-50 border border-amber-200 text-xs text-amber-800">
            <Info className="w-4 h-4 shrink-0" />
            Demo Mode &mdash; these features compute over live data and have no synthetic fixture
            to fall back to, so actions here are disabled rather than fabricated.
          </div>
        )}

        <div className="flex gap-1 mt-4 -mb-4 border-b border-gray-200">
          {TABS.map(({ key, label, icon: Icon }) => (
            <button
              key={key}
              onClick={() => setTab(key)}
              className={`flex items-center gap-1.5 px-3 py-2 text-sm font-medium border-b-2 transition-colors ${
                tab === key
                  ? 'border-blue-600 text-blue-700'
                  : 'border-transparent text-gray-500 hover:text-gray-700'
              }`}
            >
              <Icon className="w-4 h-4" />
              {label}
            </button>
          ))}
        </div>
      </div>

      <div className="flex-1 overflow-auto p-6">
        <div className="max-w-5xl">
          {tab === 'narrations' && <NarrationsTab />}
          {tab === 'correlations' && <CorrelationsTab />}
          {tab === 'forecasts' && <ForecastsTab />}
          {tab === 'contract-export' && <ContractExportTab />}
        </div>
      </div>
    </div>
  );
}
