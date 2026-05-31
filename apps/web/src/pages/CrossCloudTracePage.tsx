import { useState } from 'react';
import { Route, Cloud, AlertCircle, CheckCircle2, Clock, ChevronDown, ChevronRight, Info } from 'lucide-react';
import { useCrossCloudTrace } from '@/hooks/useCrossCloudTrace';
import { useNamespaces } from '@/hooks/useNamespaces';
import type { CrossCloudTraceHop, CrossCloudNamespaceSummary, CloudProviderType } from '@/lib/api/types';

// ── Cloud provider visual identity ───────────────────────────────────────────

const CLOUD_COLORS: Record<CloudProviderType, string> = {
  azure: 'bg-sky-100 text-sky-700 border-sky-200',
  aws: 'bg-orange-100 text-orange-700 border-orange-200',
  gcp: 'bg-green-100 text-green-700 border-green-200',
};

const CLOUD_BADGE_DOT: Record<CloudProviderType, string> = {
  azure: 'bg-sky-500',
  aws: 'bg-orange-500',
  gcp: 'bg-green-500',
};

const CLOUD_LABELS: Record<CloudProviderType, string> = {
  azure: 'Azure',
  aws: 'AWS',
  gcp: 'GCP',
};

const CLOUD_CONNECTOR_COLORS: Record<CloudProviderType, string> = {
  azure: 'border-sky-400',
  aws: 'border-orange-400',
  gcp: 'border-green-400',
};

function CloudBadge({ provider }: { provider: CloudProviderType }) {
  return (
    <span className={`inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-xs font-semibold border ${CLOUD_COLORS[provider]}`}>
      <span className={`w-1.5 h-1.5 rounded-full ${CLOUD_BADGE_DOT[provider]}`} />
      {CLOUD_LABELS[provider]}
    </span>
  );
}

// ── Cloud topology flow diagram ───────────────────────────────────────────────

interface FlowDiagramProps {
  clouds: CloudProviderType[];
  hops: CrossCloudTraceHop[];
  summaries: CrossCloudNamespaceSummary[];
}

function FlowDiagram({ clouds, hops, summaries }: FlowDiagramProps) {
  const hopsPerCloud = (cloud: CloudProviderType) =>
    hops.filter(h => h.cloudProvider === cloud).length;
  const wasSearched = (cloud: CloudProviderType) =>
    summaries.some(s => s.cloudProvider === cloud && s.wasSearched);

  return (
    <div className="flex items-center gap-0 overflow-x-auto pb-2">
      {clouds.map((cloud, idx) => (
        <div key={cloud} className="flex items-center">
          {/* Cloud node */}
          <div className={`flex flex-col items-center gap-1.5 px-5 py-3 rounded-xl border-2 ${CLOUD_CONNECTOR_COLORS[cloud]} bg-white shadow-sm min-w-[110px]`}>
            <CloudBadge provider={cloud} />
            <span className="text-xs text-gray-500">
              {hopsPerCloud(cloud)} hop{hopsPerCloud(cloud) !== 1 ? 's' : ''}
            </span>
            {!wasSearched(cloud) && (
              <span className="text-xs text-amber-600 font-medium">Phase 2</span>
            )}
          </div>
          {/* Connector arrow (not after last node) */}
          {idx < clouds.length - 1 && (
            <div className="flex items-center mx-1">
              <div className="h-px w-8 bg-gray-300" />
              <div className="w-0 h-0 border-t-4 border-b-4 border-l-[6px] border-t-transparent border-b-transparent border-l-gray-400" />
            </div>
          )}
        </div>
      ))}
    </div>
  );
}

// ── Hop card ─────────────────────────────────────────────────────────────────

interface HopCardProps {
  hop: CrossCloudTraceHop;
}

function HopCard({ hop }: HopCardProps) {
  const [expanded, setExpanded] = useState(false);
  const stateColors: Record<string, string> = {
    Active: 'bg-blue-100 text-blue-700',
    DeadLettered: 'bg-red-100 text-red-700',
    Scheduled: 'bg-purple-100 text-purple-700',
    Deferred: 'bg-amber-100 text-amber-700',
    Resolved: 'bg-green-100 text-green-700',
    Replayed: 'bg-teal-100 text-teal-700',
  };
  const stateClass = stateColors[hop.state] ?? 'bg-gray-100 text-gray-700';
  const ts = new Date(hop.timestamp);

  return (
    <div className="rounded-lg border border-gray-200 bg-white shadow-sm overflow-hidden">
      <button
        className="w-full flex items-center gap-3 p-4 text-left hover:bg-gray-50 transition-colors"
        onClick={() => setExpanded(prev => !prev)}
        aria-expanded={expanded}
      >
        {/* Hop index badge */}
        <span className="flex-shrink-0 w-7 h-7 rounded-full bg-gray-100 text-gray-600 text-xs font-bold flex items-center justify-center">
          {hop.hopIndex + 1}
        </span>
        {/* Cloud badge */}
        <CloudBadge provider={hop.cloudProvider} />
        {/* Namespace & entity */}
        <div className="flex-1 min-w-0">
          <p className="text-sm font-medium text-gray-800 truncate">{hop.namespaceDisplayName}</p>
          <p className="text-xs text-gray-500 truncate">{hop.entityPath ?? hop.entityName}</p>
        </div>
        {/* State */}
        <span className={`flex-shrink-0 px-2 py-0.5 rounded-full text-xs font-semibold ${stateClass}`}>
          {hop.state}
        </span>
        {/* Timestamp */}
        <span className="flex-shrink-0 text-xs text-gray-400 hidden sm:block">
          {ts.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' })}
        </span>
        {/* Expand icon */}
        {expanded
          ? <ChevronDown className="w-4 h-4 text-gray-400 flex-shrink-0" />
          : <ChevronRight className="w-4 h-4 text-gray-400 flex-shrink-0" />}
      </button>

      {expanded && (
        <div className="border-t border-gray-100 bg-gray-50 px-4 py-3 grid grid-cols-2 gap-x-6 gap-y-2 text-xs">
          <div><span className="text-gray-400">Message ID</span><p className="font-mono text-gray-700 truncate">{hop.messageId}</p></div>
          <div><span className="text-gray-400">Sequence #</span><p className="font-mono text-gray-700">{hop.sequenceNumber}</p></div>
          <div><span className="text-gray-400">Timestamp</span><p className="text-gray-700">{ts.toLocaleString()}</p></div>
          <div><span className="text-gray-400">Size</span><p className="text-gray-700">{hop.sizeInBytes.toLocaleString()} bytes</p></div>
          <div><span className="text-gray-400">Source</span><p className="text-gray-700">{hop.source}</p></div>
          {hop.deadLetterReason && (
            <div className="col-span-2"><span className="text-gray-400">DLQ Reason</span><p className="text-red-600">{hop.deadLetterReason}</p></div>
          )}
          {hop.bodyPreview && (
            <div className="col-span-2">
              <span className="text-gray-400">Body Preview</span>
              <pre className="mt-1 text-gray-700 bg-white border border-gray-200 rounded p-2 overflow-x-auto whitespace-pre-wrap break-all text-xs">{hop.bodyPreview}</pre>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

// ── Namespace summary panel ────────────────────────────────────────────────────

function NamespaceSummaryRow({ summary }: { summary: CrossCloudNamespaceSummary }) {
  return (
    <div className="flex items-center gap-3 py-2 px-3 rounded-lg border border-gray-100 bg-gray-50 text-sm">
      <CloudBadge provider={summary.cloudProvider} />
      <span className="flex-1 font-medium text-gray-700 truncate">{summary.namespaceDisplayName}</span>
      {summary.wasSearched ? (
        <span className="flex items-center gap-1 text-green-600 text-xs font-medium">
          <CheckCircle2 className="w-3.5 h-3.5" />
          Searched ({summary.hopsFound} found)
        </span>
      ) : (
        <span className="flex items-center gap-1 text-amber-600 text-xs font-medium" title={summary.skipReason ?? ''}>
          <Clock className="w-3.5 h-3.5" />
          {summary.skipReason?.startsWith('AWS') || summary.skipReason?.startsWith('GCP') ? 'Phase 2' : 'Skipped'}
        </span>
      )}
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

export function CrossCloudTracePage() {
  const [traceId, setTraceId] = useState('');
  const [submitted, setSubmitted] = useState('');

  const { data: namespaces } = useNamespaces();
  const { mutate: runTrace, data: result, isPending, isSuccess } = useCrossCloudTrace();

  // Compute distinct clouds connected
  const connectedClouds = new Set((namespaces ?? []).map(n => n.cloudProvider ?? 'azure'));
  const hasMultiCloud = connectedClouds.size >= 2;

  const handleTrace = () => {
    const trimmed = traceId.trim();
    if (!trimmed) return;
    setSubmitted(trimmed);
    runTrace(trimmed);
  };

  return (
    <div className="p-6 max-w-5xl mx-auto space-y-6">
      {/* Page header */}
      <div className="flex items-center gap-3">
        <div className="w-10 h-10 rounded-xl bg-violet-100 flex items-center justify-center">
          <Route className="w-5 h-5 text-violet-600" />
        </div>
        <div>
          <h1 className="text-xl font-bold text-gray-900">Multi-Cloud Trace</h1>
          <p className="text-sm text-gray-500">Trace a message as it routes across Azure, AWS, and GCP</p>
        </div>
      </div>

      {/* Multi-cloud gate */}
      {!hasMultiCloud && (
        <div className="flex items-start gap-3 p-4 rounded-xl border border-amber-200 bg-amber-50">
          <Info className="w-5 h-5 text-amber-600 flex-shrink-0 mt-0.5" />
          <div>
            <p className="text-sm font-semibold text-amber-800">Multi-cloud connection required</p>
            <p className="text-sm text-amber-700 mt-0.5">
              Connect namespaces from at least two different cloud providers (Azure, AWS, GCP) on the
              <strong> Connect</strong> page to enable cross-cloud tracing.
              Currently connected: {connectedClouds.size === 0 ? 'none' : [...connectedClouds].map(c => CLOUD_LABELS[c as CloudProviderType]).join(', ')}.
            </p>
          </div>
        </div>
      )}

      {/* Search bar */}
      <div className="flex gap-3">
        <input
          type="text"
          className="flex-1 rounded-lg border border-gray-300 bg-white px-4 py-2.5 text-sm text-gray-900 placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-violet-400 focus:border-transparent disabled:opacity-50 disabled:cursor-not-allowed font-mono"
          placeholder="Enter Correlation ID or Trace ID…"
          value={traceId}
          onChange={e => setTraceId(e.target.value)}
          onKeyDown={e => e.key === 'Enter' && hasMultiCloud && handleTrace()}
          disabled={!hasMultiCloud || isPending}
          aria-label="Trace ID"
        />
        <button
          onClick={handleTrace}
          disabled={!hasMultiCloud || !traceId.trim() || isPending}
          className="flex items-center gap-2 px-5 py-2.5 rounded-lg bg-violet-600 hover:bg-violet-700 disabled:opacity-50 disabled:cursor-not-allowed text-white text-sm font-semibold transition-colors"
        >
          {isPending ? (
            <>
              <div className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
              Tracing…
            </>
          ) : (
            <>
              <Route className="w-4 h-4" />
              Trace Across Clouds
            </>
          )}
        </button>
      </div>

      {/* Results */}
      {isSuccess && result && (
        <div className="space-y-6">
          {/* Summary bar */}
          <div className="flex flex-wrap items-center gap-4 p-4 rounded-xl border border-gray-200 bg-white shadow-sm">
            <div className="flex items-center gap-2">
              {result.totalHops > 0
                ? <CheckCircle2 className="w-5 h-5 text-green-500" />
                : <AlertCircle className="w-5 h-5 text-amber-500" />}
              <span className="text-sm font-semibold text-gray-800 font-mono truncate max-w-xs">{submitted}</span>
            </div>
            <div className="flex gap-4 text-sm text-gray-600 ml-auto">
              <span><strong className="text-gray-900">{result.totalHops}</strong> hops</span>
              <span><strong className="text-gray-900">{result.cloudsInvolved}</strong> cloud{result.cloudsInvolved !== 1 ? 's' : ''}</span>
              <span><strong className="text-gray-900">{result.namespacesSearched}</strong> namespaces searched</span>
              <span><strong className="text-gray-900">{result.searchDurationMs}</strong> ms</span>
            </div>
            {result.isPartialResult && (
              <span className="text-xs text-amber-600 bg-amber-50 px-2 py-0.5 rounded-full font-medium border border-amber-200">
                Partial — timed out
              </span>
            )}
          </div>

          {/* Visual flow diagram */}
          {result.cloudProviders.length > 0 && (
            <div>
              <h2 className="text-sm font-semibold text-gray-700 mb-3 uppercase tracking-wide">Routing Path</h2>
              <FlowDiagram
                clouds={result.cloudProviders as CloudProviderType[]}
                hops={result.hops}
                summaries={result.namespaceSummaries}
              />
            </div>
          )}

          {/* Hop timeline */}
          {result.totalHops > 0 ? (
            <div>
              <h2 className="text-sm font-semibold text-gray-700 mb-3 uppercase tracking-wide">
                Message Timeline ({result.totalHops} hop{result.totalHops !== 1 ? 's' : ''})
              </h2>
              <div className="space-y-2">
                {result.hops.map((hop, idx) => (
                  <HopCard key={`${hop.namespaceId}-${hop.messageId}-${idx}`} hop={hop} />
                ))}
              </div>
            </div>
          ) : (
            <div className="flex flex-col items-center gap-3 py-12 text-center">
              <Cloud className="w-10 h-10 text-gray-300" />
              <p className="text-gray-500 text-sm">No messages found with trace ID <code className="font-mono">{submitted}</code></p>
              <p className="text-gray-400 text-xs">The message may have already been consumed or expired.</p>
            </div>
          )}

          {/* Namespace coverage panel */}
          {result.namespaceSummaries.length > 0 && (
            <div>
              <h2 className="text-sm font-semibold text-gray-700 mb-3 uppercase tracking-wide">Search Coverage</h2>
              <div className="space-y-1.5">
                {result.namespaceSummaries.map(s => (
                  <NamespaceSummaryRow key={s.namespaceId} summary={s} />
                ))}
              </div>
              {result.namespaceSummaries.some(s => !s.wasSearched && (s.cloudProvider === 'aws' || s.cloudProvider === 'gcp')) && (
                <p className="mt-2 text-xs text-gray-400 flex items-center gap-1">
                  <Info className="w-3.5 h-3.5" />
                  AWS and GCP namespace search will be available in Phase 2.
                </p>
              )}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
