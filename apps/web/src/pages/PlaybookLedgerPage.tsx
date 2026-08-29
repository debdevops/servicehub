import { Fragment, useState } from 'react';
import { ClipboardList, AlertCircle, RefreshCw, Info, ChevronDown, ChevronRight, CheckCircle2, XCircle, Eye, Gauge, Target } from 'lucide-react';
import {
  usePlaybookEntries,
  usePlaybookEntry,
  useMarkPlaybookEntryUnderReview,
  useDispositionPlaybookEntry,
  useCorrelationAccountability,
  useBacktestReport,
} from '@servicehub/ui-shared/hooks/usePlaybookLedger';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { ProviderBadge } from '@servicehub/ui-shared/lib/providerStyles';
import { EnvironmentBadge } from '@/components/EnvironmentBadge';
import { EmptyState } from '@/components/EmptyState';
import { PLAYBOOK_STATE_EXPLANATIONS } from '@servicehub/ui-shared/lib/api/playbook';
import type { CloudProviderType } from '@servicehub/ui-shared/lib/api/types';
import type { PillarKind, PlaybookEntryState, PlaybookEntry } from '@servicehub/ui-shared/lib/api/playbook';

const KNOWN_PROVIDERS: readonly CloudProviderType[] = ['azure', 'aws', 'gcp'];
const PILLARS: readonly PillarKind[] = ['Investigate', 'Correlate', 'Prevent', 'Recover'];
const STATES: readonly PlaybookEntryState[] = [
  'Proposed', 'UnderReview', 'Approved', 'Edited', 'Rejected', 'Expired', 'Superseded', 'Revoked',
];

const STATE_COLORS: Record<PlaybookEntryState, string> = {
  Proposed: 'bg-amber-100 text-amber-700',
  UnderReview: 'bg-blue-100 text-blue-700',
  Approved: 'bg-green-100 text-green-700',
  Edited: 'bg-purple-100 text-purple-700',
  Rejected: 'bg-red-100 text-red-700',
  Expired: 'bg-gray-100 text-gray-600',
  Superseded: 'bg-gray-100 text-gray-600',
  Revoked: 'bg-gray-100 text-gray-600',
};

function CloudBadge({ provider }: { provider: string | null }) {
  if (!provider) return null;
  const normalized = provider.toLowerCase() as CloudProviderType;
  if (!KNOWN_PROVIDERS.includes(normalized)) return null;
  return <ProviderBadge provider={normalized} />;
}

function StateBadge({ state }: { state: PlaybookEntryState }) {
  return (
    <span
      title={PLAYBOOK_STATE_EXPLANATIONS[state]}
      className={`px-2 py-0.5 text-xs font-medium rounded-full ${STATE_COLORS[state]}`}
    >
      {state}
    </span>
  );
}

/**
 * C4 — correlation accountability (roadmap §5.D, §11 item 17): a compact strip reporting how many
 * correlation hypotheses (C1/C2) ServiceHub has proposed and what fraction of dispositioned ones a
 * human approved — "making correlation quality measurable instead of a black box." Shows an honest
 * "not enough evidence yet" rather than a fabricated rate when nothing has been dispositioned.
 */
function CorrelationAccountabilityStrip() {
  const { data: report, isLoading } = useCorrelationAccountability();

  if (isLoading || !report) return null;

  if (report.totalHypotheses === 0) {
    return (
      <div className="mt-3 flex items-center gap-2 px-3 py-2 rounded-lg bg-gray-50 border border-gray-200 text-xs text-gray-500">
        <Gauge className="w-4 h-4 shrink-0 text-gray-400" />
        Correlation accountability: no correlation hypotheses proposed yet.
      </div>
    );
  }

  const dispositioned = report.approvedCount + report.rejectedCount;

  return (
    <div className="mt-3 flex items-center gap-2 px-3 py-2 rounded-lg bg-indigo-50 border border-indigo-200 text-xs text-indigo-800">
      <Gauge className="w-4 h-4 shrink-0" />
      Correlation accountability: {report.totalHypotheses} hypothes{report.totalHypotheses === 1 ? 'is' : 'es'} proposed
      {report.approvalRate !== null ? (
        <> &middot; {Math.round(report.approvalRate * 100)}% approved ({report.approvedCount} of {dispositioned} dispositioned)</>
      ) : (
        <> &middot; not enough evidence yet ({report.proposedCount + report.underReviewCount} awaiting a decision)</>
      )}
    </div>
  );
}

/**
 * Counterfactual backtesting (roadmap §11 item 14): a compact strip reporting how often
 * dispositioned anomaly-flag (I3) and drift-finding (P2) proposals were followed by real recovery
 * activity for the same entity — "measurable against what actually happened, not just judged
 * 'looks reasonable'." Shows an honest "not enough evidence yet" when nothing has been
 * backtested.
 */
function BacktestStrip() {
  const { data: report, isLoading } = useBacktestReport();

  if (isLoading || !report) return null;

  if (report.totalBacktested === 0) {
    return (
      <div className="mt-3 flex items-center gap-2 px-3 py-2 rounded-lg bg-gray-50 border border-gray-200 text-xs text-gray-500">
        <Target className="w-4 h-4 shrink-0 text-gray-400" />
        Backtesting: no dispositioned findings to backtest yet.
      </div>
    );
  }

  return (
    <div className="mt-3 flex items-center gap-2 px-3 py-2 rounded-lg bg-teal-50 border border-teal-200 text-xs text-teal-800">
      <Target className="w-4 h-4 shrink-0" />
      Backtesting: {report.totalBacktested} finding{report.totalBacktested === 1 ? '' : 's'} checked against what actually happened
      {report.corroborationRate !== null && (
        <> &middot; {Math.round(report.corroborationRate * 100)}% corroborated ({report.corroboratedCount} of {report.totalBacktested})</>
      )}
    </div>
  );
}

function tryParseJson(raw: string): unknown {
  try {
    return JSON.parse(raw);
  } catch {
    return raw;
  }
}

/** Expanded-row detail: evidence/proposal JSON, the event chain, and the human-disposition actions. */
function EntryDetailRow({ entry }: { entry: PlaybookEntry }) {
  const { data: detail, isLoading } = usePlaybookEntry(entry.id);
  const markUnderReview = useMarkPlaybookEntryUnderReview();
  const disposition = useDispositionPlaybookEntry();
  const [showRejectForm, setShowRejectForm] = useState(false);
  const [rejectReason, setRejectReason] = useState('');

  const isActionable = entry.state === 'Proposed' || entry.state === 'UnderReview' || entry.state === 'Edited';

  return (
    <tr>
      <td colSpan={7} className="px-4 py-4 bg-gray-50 border-t border-gray-100">
        <div className="grid grid-cols-2 gap-4 mb-4">
          <div>
            <div className="text-xs font-semibold text-gray-500 mb-1">Evidence</div>
            <pre className="text-xs bg-white border border-gray-200 rounded p-2 overflow-auto max-h-40">
              {JSON.stringify(tryParseJson(entry.evidenceRefJson), null, 2)}
            </pre>
          </div>
          <div>
            <div className="text-xs font-semibold text-gray-500 mb-1">Proposal</div>
            <pre className="text-xs bg-white border border-gray-200 rounded p-2 overflow-auto max-h-40">
              {JSON.stringify(tryParseJson(entry.proposalJson), null, 2)}
            </pre>
          </div>
        </div>

        <div className="mb-4">
          <div className="text-xs font-semibold text-gray-500 mb-1">Event chain</div>
          {isLoading ? (
            <div className="text-xs text-gray-400">Loading…</div>
          ) : (
            <table className="w-full text-xs">
              <thead>
                <tr className="text-left text-gray-500">
                  <th className="pr-4 py-1">Seq</th>
                  <th className="pr-4 py-1">Event</th>
                  <th className="pr-4 py-1">Actor</th>
                  <th className="pr-4 py-1">Occurred</th>
                  <th className="pr-4 py-1">Detail</th>
                </tr>
              </thead>
              <tbody>
                {(detail?.events ?? []).map(evt => (
                  <tr key={evt.id} className="border-t border-gray-100">
                    <td className="pr-4 py-1 text-gray-400">{evt.seq}</td>
                    <td className="pr-4 py-1 font-medium text-gray-700">{evt.eventType}</td>
                    <td className="pr-4 py-1 text-gray-600">{evt.actorIdentity} ({evt.actorKind})</td>
                    <td className="pr-4 py-1 text-gray-500 whitespace-nowrap">{new Date(evt.occurredAt).toLocaleString()}</td>
                    <td className="pr-4 py-1 text-gray-500">{evt.detailJson ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>

        {isActionable && (
          <div className="flex items-center gap-2 flex-wrap">
            {entry.state === 'Proposed' && (
              <button
                onClick={() => markUnderReview.mutate(entry.id)}
                disabled={markUnderReview.isPending}
                className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-gray-700 bg-white border border-gray-200 rounded-lg hover:bg-gray-50 disabled:opacity-50"
              >
                <Eye className="w-3.5 h-3.5" /> Mark under review
              </button>
            )}
            <button
              onClick={() => disposition.mutate({ entryId: entry.id, disposition: 'Approved' })}
              disabled={disposition.isPending}
              className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-white bg-green-600 hover:bg-green-700 rounded-lg disabled:opacity-50"
            >
              <CheckCircle2 className="w-3.5 h-3.5" /> Approve
            </button>
            {!showRejectForm ? (
              <button
                onClick={() => setShowRejectForm(true)}
                className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-white bg-red-600 hover:bg-red-700 rounded-lg"
              >
                <XCircle className="w-3.5 h-3.5" /> Reject
              </button>
            ) : (
              <div className="flex items-center gap-2">
                <input
                  type="text"
                  value={rejectReason}
                  onChange={e => setRejectReason(e.target.value)}
                  placeholder="Reason (required)"
                  aria-label="Rejection reason"
                  className="text-xs border border-gray-200 rounded-lg px-2 py-1.5 w-56 focus:outline-none focus:ring-2 focus:ring-teal-500"
                />
                <button
                  onClick={() =>
                    disposition.mutate(
                      { entryId: entry.id, disposition: 'Rejected', reason: rejectReason },
                      { onSuccess: () => setShowRejectForm(false) },
                    )
                  }
                  disabled={!rejectReason.trim() || disposition.isPending}
                  className="px-3 py-1.5 text-xs font-medium text-white bg-red-600 hover:bg-red-700 rounded-lg disabled:opacity-50"
                >
                  Confirm reject
                </button>
                <button onClick={() => setShowRejectForm(false)} className="text-xs text-gray-500 hover:text-gray-700">
                  Cancel
                </button>
              </div>
            )}
          </div>
        )}
      </td>
    </tr>
  );
}

/**
 * `/playbook` — filterable list of Playbook Ledger entries (M4 of the persistence wave): what
 * ServiceHub's detection workers (anomaly, drift, correlation) believed was worth a human's
 * attention, and what a human decided about it. Click a row to expand its evidence, proposal, and
 * event chain, and — while non-terminal — mark it under review or disposition it. Nothing here
 * ever authorizes a replay or purge; approving a proposal means "a human agrees this is sound."
 */
export default function PlaybookLedgerPage() {
  const { isDemoMode } = useDemoContext();
  const [pillarFilter, setPillarFilter] = useState<'' | PillarKind>('');
  const [stateFilter, setStateFilter] = useState<'' | PlaybookEntryState>('');
  const [expandedId, setExpandedId] = useState<string | null>(null);

  const { data: entries, isLoading, isError, refetch, isFetching } = usePlaybookEntries({
    pillarKind: pillarFilter || undefined,
    state: stateFilter || undefined,
  });

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      <div className="bg-white border-b border-gray-200 px-6 py-4 shrink-0">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-xl font-bold text-gray-900 flex items-center gap-2">
              <ClipboardList className="w-5 h-5 text-teal-600" />
              Playbook Ledger
            </h1>
            <p className="text-sm text-gray-500 mt-0.5">
              What ServiceHub's detection workers believed was worth doing, and what a human decided
              about it. Nothing here ever authorizes a replay or purge.
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
            Demo Mode — the Playbook Ledger has no fixture data, so this view is always empty here.
          </div>
        )}

        <CorrelationAccountabilityStrip />
        <BacktestStrip />

        <div className="mt-3 flex items-center gap-2">
          <select
            value={pillarFilter}
            onChange={e => setPillarFilter(e.target.value as '' | PillarKind)}
            aria-label="Filter by pillar"
            className="text-sm border border-gray-200 rounded-lg px-3 py-1.5 bg-white text-gray-700 focus:outline-none focus:ring-2 focus:ring-teal-500"
          >
            <option value="">All Pillars</option>
            {PILLARS.map(p => (
              <option key={p} value={p}>{p}</option>
            ))}
          </select>
          <select
            value={stateFilter}
            onChange={e => setStateFilter(e.target.value as '' | PlaybookEntryState)}
            aria-label="Filter by state"
            className="text-sm border border-gray-200 rounded-lg px-3 py-1.5 bg-white text-gray-700 focus:outline-none focus:ring-2 focus:ring-teal-500"
          >
            <option value="">All States</option>
            {STATES.map(s => (
              <option key={s} value={s}>{s}</option>
            ))}
          </select>
        </div>
      </div>

      <div className="flex-1 overflow-auto">
        {isLoading ? (
          <div className="flex items-center justify-center h-64">
            <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-teal-600" />
          </div>
        ) : isError ? (
          <div className="flex items-center justify-center h-64">
            <div className="text-center">
              <AlertCircle className="w-10 h-10 text-red-400 mx-auto mb-3" />
              <p className="text-gray-600 font-medium">Failed to load Playbook Ledger entries</p>
              <button onClick={() => refetch()} className="mt-3 px-4 py-2 text-sm text-teal-600 hover:text-teal-700 border border-teal-300 rounded-lg hover:bg-teal-50">
                Try Again
              </button>
            </div>
          </div>
        ) : (entries ?? []).length === 0 ? (
          <EmptyState
            icon={ClipboardList}
            heading="No proposals recorded"
            subtext="Anomaly, drift, and correlation findings above the significance threshold will appear here for review."
          />
        ) : (
          <table className="w-full text-sm" aria-label="Playbook Ledger entries">
            <thead className="bg-gray-50 border-b border-gray-200 sticky top-0 z-10">
              <tr>
                <th scope="col" className="w-8" />
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500 w-40">Proposed At</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500 w-28">Pillar</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500">Proposal</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500">Namespace</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500 w-28">State</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500 w-24">Disposition</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {(entries ?? []).map(entry => (
                <Fragment key={entry.id}>
                  <tr
                    onClick={() => setExpandedId(expandedId === entry.id ? null : entry.id)}
                    className="hover:bg-teal-50/40 transition-colors cursor-pointer"
                  >
                    <td className="px-2 text-gray-400">
                      {expandedId === entry.id ? <ChevronDown className="w-4 h-4" /> : <ChevronRight className="w-4 h-4" />}
                    </td>
                    <td className="px-4 py-3 text-gray-500 whitespace-nowrap font-mono text-xs">
                      {new Date(entry.proposedAt).toLocaleString(undefined, {
                        month: 'short', day: '2-digit', hour: '2-digit', minute: '2-digit',
                      })}
                    </td>
                    <td className="px-4 py-3 text-gray-700 text-xs font-medium">{entry.pillarKind}</td>
                    <td className="px-4 py-3 text-gray-800 font-medium text-xs">{entry.proposalKind}</td>
                    <td className="px-4 py-3">
                      {entry.namespaceNameSnapshot ? (
                        <div className="flex items-center gap-1 flex-wrap">
                          <CloudBadge provider={entry.providerSnapshot} />
                          <EnvironmentBadge env={entry.environmentSnapshot} />
                          <span className="text-xs text-gray-500 bg-gray-100 px-1.5 py-0.5 rounded">{entry.namespaceNameSnapshot}</span>
                        </div>
                      ) : (
                        <span className="text-xs text-gray-400">Fleet-wide</span>
                      )}
                    </td>
                    <td className="px-4 py-3"><StateBadge state={entry.state} /></td>
                    <td className="px-4 py-3 text-xs text-gray-600">{entry.disposition ?? '—'}</td>
                  </tr>
                  {expandedId === entry.id && <EntryDetailRow entry={entry} />}
                </Fragment>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
