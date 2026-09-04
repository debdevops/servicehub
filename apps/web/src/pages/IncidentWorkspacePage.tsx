import { useMemo } from 'react';
import { useParams, useSearchParams, useNavigate, Link } from 'react-router-dom';
import {
  ArrowLeft,
  AlertCircle,
  RefreshCw,
  ClipboardList,
  Lightbulb,
  Wrench,
  Sparkles,
  Activity as ActivityIcon,
} from 'lucide-react';
import { useIncident } from '@servicehub/ui-shared/hooks/useIncident';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { extractApiError } from '@servicehub/ui-shared/lib/api/errors';
import type { ApiError } from '@servicehub/ui-shared/lib/api/types';
import { ProviderBadge } from '@servicehub/ui-shared/lib/providerStyles';
import { PLAYBOOK_STATE_EXPLANATIONS } from '@servicehub/ui-shared/lib/api/playbook';
import type { PlaybookEntry, PlaybookEntryState } from '@servicehub/ui-shared/lib/api/playbook';
import type { RecoveryLedgerEntry } from '@servicehub/ui-shared/lib/api/recovery';
import { EnvironmentBadge } from '@/components/EnvironmentBadge';
import { EmptyState } from '@/components/EmptyState';
import { StatusBadge } from '@/components/dlq';
import { RecoveryStateBadge } from '@/components/recovery/RecoveryStateBadge';

const EVIDENCE_PROPOSAL_KINDS = new Set(['AnomalyFlag', 'DriftFinding', 'CorrelationHypothesis', 'ReasoningCompanionObservation']);
const RECOVERY_PROPOSAL_KINDS = new Set(['ReplayPlan', 'PreventionTrigger']);

const PLAYBOOK_STATE_COLORS: Record<PlaybookEntryState, string> = {
  Proposed: 'bg-amber-100 text-amber-700',
  UnderReview: 'bg-blue-100 text-blue-700',
  Approved: 'bg-green-100 text-green-700',
  Edited: 'bg-purple-100 text-purple-700',
  Rejected: 'bg-red-100 text-red-700',
  Expired: 'bg-gray-100 text-gray-600',
  Superseded: 'bg-gray-100 text-gray-600',
  Revoked: 'bg-gray-100 text-gray-600',
};

const TABS = [
  { id: 'summary', label: 'Summary' },
  { id: 'evidence', label: 'Evidence' },
  { id: 'recovery', label: 'Recommended Recovery' },
  { id: 'activity', label: 'Activity' },
] as const;

type TabId = (typeof TABS)[number]['id'];

function formatDate(ts: string): string {
  return new Date(ts).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

function PlaybookStateBadge({ state }: { state: PlaybookEntryState }) {
  return (
    <span
      title={PLAYBOOK_STATE_EXPLANATIONS[state]}
      className={`px-2 py-0.5 text-xs font-medium rounded-full ${PLAYBOOK_STATE_COLORS[state] ?? 'bg-gray-100 text-gray-600'}`}
    >
      {state}
    </span>
  );
}

/** Marks a proposal authored by the optional reasoning companion (roadmap §7, W5) so a reviewer
 * never mistakes an AI-generated observation for a deterministic detection worker's finding —
 * this service has no access to any ledger and can only ever land here as a proposal a human
 * disposes of like any other. */
function AiSuggestionBadge() {
  return (
    <span
      title="Proposed by the reasoning companion — an optional, self-hosted advisory service. It has no access to any ledger or broker and can only propose; a human decides."
      className="inline-flex items-center gap-1 px-2 py-0.5 text-xs font-medium rounded-full bg-violet-100 text-violet-700"
    >
      <Sparkles className="w-3 h-3" /> AI suggestion
    </span>
  );
}

/** Mirrors the JSON `ReasoningCompanionWorker.ProposePlaybookEntryAsync` (services/api) builds for
 * a `ReasoningCompanionObservation` entry's `ProposalJson` — PascalCase, since it is a raw-
 * serialized string, not an MVC response body. Absent for every other proposal kind. */
interface ReasoningCompanionObservationProposal {
  Summary: string;
  Considerations: string[];
}

function parseReasoningCompanionObservation(json: string): ReasoningCompanionObservationProposal | null {
  try {
    const parsed = JSON.parse(json) as Partial<ReasoningCompanionObservationProposal>;
    if (typeof parsed.Summary !== 'string') return null;
    return { Summary: parsed.Summary, Considerations: Array.isArray(parsed.Considerations) ? parsed.Considerations : [] };
  } catch {
    return null;
  }
}

/** Mirrors the JSON `AutoReplayExecutor.ProposeReplayPlanAsync` (services/api) builds for a
 * `ReplayPlan` entry's `ProposalJson` — PascalCase, since it is a raw-serialized string, not an
 * MVC response body. Absent for every other proposal kind. */
interface ReplayPlanProposal {
  EntityName: string;
  MessageId: string;
  TargetAction: string;
  RuleId: number;
  RuleName: string;
}

function parseReplayPlanProposal(json: string): ReplayPlanProposal | null {
  try {
    const parsed = JSON.parse(json) as Partial<ReplayPlanProposal>;
    if (typeof parsed.EntityName !== 'string' || typeof parsed.MessageId !== 'string') return null;
    return parsed as ReplayPlanProposal;
  } catch {
    return null;
  }
}

/**
 * Roadmap W2.5 ("recovery proposal, then verification"): a `ReplayPlan` proposal states its scope
 * in plain language — which message, on which rule, doing what — instead of a raw JSON dump.
 * "Policy" and "stop condition" are static because this proposal kind carries neither field yet
 * (`AutoReplayExecutor.ProposeReplayPlanAsync` only serializes entity/message/rule identity): what
 * approving it actually means, since the ledger write is deliberately decoupled from execution.
 * The raw JSON stays available in a disclosure for full transparency and for every other proposal
 * kind, which this card does not attempt to interpret.
 */
function PlaybookEntryCard({ entry }: { entry: PlaybookEntry }) {
  const replayPlan = entry.proposalKind === 'ReplayPlan' ? parseReplayPlanProposal(entry.proposalJson) : null;
  const reasoningObservation =
    entry.proposalKind === 'ReasoningCompanionObservation' ? parseReasoningCompanionObservation(entry.proposalJson) : null;

  return (
    <div className="bg-white border border-gray-200 rounded-lg p-4">
      <div className="flex items-start justify-between gap-3 mb-2">
        <div>
          <p className="text-sm font-semibold text-gray-900">{entry.proposalKind}</p>
          <p className="text-xs text-gray-500">{entry.pillarKind} &middot; proposed by {entry.proposerIdentity}</p>
        </div>
        <div className="flex items-center gap-2 shrink-0">
          {entry.proposerKind === 'ReasoningAgent' && <AiSuggestionBadge />}
          <PlaybookStateBadge state={entry.state} />
        </div>
      </div>
      <p className="text-xs text-gray-400 mb-2">{formatDate(entry.proposedAt)}</p>

      {reasoningObservation && (
        <div className="mb-2 text-xs text-gray-700">
          <p className="font-semibold text-gray-500 uppercase tracking-wide mb-1">Observation</p>
          <p className="mb-2">{reasoningObservation.Summary}</p>
          {reasoningObservation.Considerations.length > 0 && (
            <>
              <p className="font-semibold text-gray-500 uppercase tracking-wide mb-1">Considerations</p>
              <ul className="list-disc list-inside space-y-0.5">
                {reasoningObservation.Considerations.map((c, i) => (
                  <li key={i}>{c}</li>
                ))}
              </ul>
            </>
          )}
        </div>
      )}

      {replayPlan && (
        <div className="mb-2 grid gap-2 text-xs text-gray-700 sm:grid-cols-2">
          <div>
            <p className="font-semibold text-gray-500 uppercase tracking-wide">Scope</p>
            <p className="truncate">{replayPlan.TargetAction} &middot; {replayPlan.EntityName}</p>
            <p className="text-gray-400 truncate">message {replayPlan.MessageId}</p>
          </div>
          <div>
            <p className="font-semibold text-gray-500 uppercase tracking-wide">Source rule</p>
            <p>{replayPlan.RuleName} (#{replayPlan.RuleId})</p>
          </div>
          <div className="sm:col-span-2">
            <p className="font-semibold text-gray-500 uppercase tracking-wide">Policy &amp; stop condition</p>
            <p>Approving this only records that a human agrees the plan is sound — it does not itself
              replay anything. The actual replay, and its verification, happens separately in the
              Approval Queue or Signature Details.</p>
          </div>
        </div>
      )}

      <details className="text-xs">
        <summary className="cursor-pointer text-primary-600 font-medium">
          View {replayPlan || reasoningObservation ? 'raw proposal JSON' : 'proposal detail'}
        </summary>
        <pre className="mt-2 bg-gray-50 border border-gray-100 rounded-md p-2 overflow-x-auto text-gray-700">
          {(() => {
            try {
              return JSON.stringify(JSON.parse(entry.proposalJson), null, 2);
            } catch {
              return entry.proposalJson;
            }
          })()}
        </pre>
      </details>
    </div>
  );
}

/** Groups recovery ledger entries by operation for a compact per-incident rollup — the full
 * per-entry ledger already lives on the Recovery Operation detail page this links out to. */
function groupRecoveryEntriesByOperation(entries: RecoveryLedgerEntry[]) {
  const byOperation = new Map<string, RecoveryLedgerEntry[]>();
  for (const entry of entries) {
    const group = byOperation.get(entry.operationId) ?? [];
    group.push(entry);
    byOperation.set(entry.operationId, group);
  }
  return Array.from(byOperation.entries());
}

/**
 * The Incident workspace (roadmap W2.3) — Summary, Evidence, Recommended Recovery, and Activity
 * for one failure signature at a single durable URL, downstream of the W2.1 read-model. Every
 * deeper action (replaying the signature, deciding a playbook proposal, auditing a recovery
 * operation's hash chain) stays on the page that already owns it — this workspace links out to
 * those pages rather than duplicating their controls.
 */
export function IncidentWorkspacePage() {
  const { signatureHash } = useParams<{ signatureHash: string }>();
  const [searchParams, setSearchParams] = useSearchParams();
  const namespaceId = searchParams.get('namespace') || undefined;
  const activeTab: TabId = TABS.some((t) => t.id === searchParams.get('tab'))
    ? (searchParams.get('tab') as TabId)
    : 'summary';

  const { data: namespaces } = useNamespaces();
  const namespace = namespaces?.find((ns) => ns.id === namespaceId);
  const { isDemoMode, cloudProvider } = useDemoContext();
  const basePath = isDemoMode && cloudProvider ? `/demo/${cloudProvider}` : '';
  const navigate = useNavigate();

  const {
    data: incident,
    isLoading,
    isError,
    error,
    refetch,
  } = useIncident(namespaceId, signatureHash);

  const evidenceEntries = useMemo(
    () => incident?.playbookEntries.filter((e) => EVIDENCE_PROPOSAL_KINDS.has(e.proposalKind)) ?? [],
    [incident],
  );
  const recoveryProposals = useMemo(
    () => incident?.playbookEntries.filter((e) => RECOVERY_PROPOSAL_KINDS.has(e.proposalKind)) ?? [],
    [incident],
  );
  const recoveryOperations = useMemo(
    () => groupRecoveryEntriesByOperation(incident?.recoveryEntries ?? []),
    [incident],
  );
  const activity = useMemo(() => {
    if (!incident) return [];
    type ActivityRow =
      | { kind: 'recovery'; at: string; entry: RecoveryLedgerEntry }
      | { kind: 'playbook'; at: string; entry: PlaybookEntry };
    const rows: ActivityRow[] = [
      ...incident.recoveryEntries.map((entry): ActivityRow => ({ kind: 'recovery', at: entry.begunAt, entry })),
      ...incident.playbookEntries.map((entry): ActivityRow => ({ kind: 'playbook', at: entry.proposedAt, entry })),
    ];
    return rows.sort((a, b) => new Date(b.at).getTime() - new Date(a.at).getTime());
  }, [incident]);

  const setTab = (tab: TabId) => {
    const next = new URLSearchParams(searchParams);
    next.set('tab', tab);
    setSearchParams(next, { replace: true });
  };

  if (!namespaceId || !signatureHash) {
    return (
      <div className="p-6 max-w-3xl mx-auto">
        <div className="bg-gray-50 border border-gray-200 rounded-xl p-6 text-center text-sm text-gray-600">
          Missing namespace or signature reference.
        </div>
      </div>
    );
  }

  const isNotFound = (error as ApiError | undefined)?.response?.status === 404 || (!isError && !isLoading && !incident);

  return (
    <div className="flex-1 overflow-y-auto min-w-0">
      <div className="p-6 max-w-5xl mx-auto">
        <Link
          to={`${basePath}/home`}
          className="inline-flex items-center gap-1.5 text-sm text-primary-600 hover:text-primary-700 mb-4"
        >
          <ArrowLeft className="w-4 h-4" />
          Back to Home
        </Link>

        {isLoading ? (
          <div className="text-sm text-gray-500">Loading incident…</div>
        ) : isError || !incident ? (
          isNotFound ? (
            <div className="bg-gray-50 border border-gray-200 rounded-xl p-6 text-center">
              <p className="text-sm font-medium text-gray-700">Incident not found</p>
              <p className="text-sm text-gray-500 mt-1">
                Nothing is on record for this signature in this namespace — no lifecycle status, no
                recovery activity, and no playbook proposals.
              </p>
            </div>
          ) : (
            <div className="flex items-start gap-2 rounded-lg bg-red-50 border border-red-200 p-4 text-sm text-red-700">
              <AlertCircle className="w-4 h-4 mt-0.5 shrink-0" />
              <span className="flex-1">{extractApiError(error, 'Failed to load this incident.')}</span>
              <button className="text-xs font-medium underline shrink-0" onClick={() => refetch()}>
                Try Again
              </button>
            </div>
          )
        ) : (
          <>
            {/* Header / Identity */}
            <div className="bg-white border border-gray-200 rounded-xl p-5 mb-4">
              <div className="flex items-start justify-between gap-3 flex-wrap mb-3">
                <div>
                  <h1 className="text-lg font-semibold text-gray-900">
                    {incident.dominantDeadletterReason ?? 'Failure signature'}
                  </h1>
                  <p className="text-xs text-gray-400 font-mono mt-0.5 break-all">Fingerprint: {incident.signatureHash}</p>
                </div>
                <div className="flex items-center gap-2 flex-wrap">
                  <StatusBadge status={incident.lifecycleStatus} size="md" />
                  {namespace?.cloudProvider && <ProviderBadge provider={namespace.cloudProvider} />}
                  {namespace?.environment && <EnvironmentBadge env={namespace.environment} />}
                </div>
              </div>

              {incident.topTerms.length > 0 && (
                <div className="flex flex-wrap gap-1.5 mb-3">
                  {incident.topTerms.map((term) => (
                    <span key={term} className="px-2 py-0.5 text-xs rounded-full bg-gray-100 text-gray-600">
                      {term}
                    </span>
                  ))}
                </div>
              )}

              <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 text-sm mb-3">
                <div>
                  <span className="text-gray-500 block text-xs">Namespace</span>
                  <span className="font-medium text-gray-900">{incident.namespaceName ?? '—'}</span>
                </div>
                <div>
                  <span className="text-gray-500 block text-xs">First Seen</span>
                  <span className="font-medium text-gray-900">{formatDate(incident.firstSeenAt)}</span>
                </div>
                <div>
                  <span className="text-gray-500 block text-xs">Last Active</span>
                  <span className="font-medium text-gray-900">{formatDate(incident.lastSeenAt)}</span>
                </div>
                <div>
                  <span className="text-gray-500 block text-xs">Occurrence Count</span>
                  <span className="font-medium text-gray-900">{incident.occurrenceCount}</span>
                </div>
              </div>

              {incident.summary.pendingDecisionCount > 0 && (
                <div className="flex items-center gap-2 text-sm text-primary-800 bg-primary-50 border border-primary-100 rounded-lg px-3 py-2 mb-3">
                  <Lightbulb className="w-4 h-4 shrink-0" />
                  {incident.summary.pendingDecisionCount} decision{incident.summary.pendingDecisionCount === 1 ? '' : 's'} waiting on a human.
                </div>
              )}

              <button
                onClick={() => navigate(`${basePath}/signatures/${incident.signatureHash}?namespace=${incident.namespaceId}`)}
                className="text-sm font-medium text-primary-600 hover:text-primary-700"
              >
                Open full signature investigation →
              </button>
            </div>

            {/* Tabs */}
            <div className="border-b border-gray-200 mb-4 flex gap-1 overflow-x-auto">
              {TABS.map((tab) => (
                <button
                  key={tab.id}
                  onClick={() => setTab(tab.id)}
                  className={`px-3 py-2 text-sm font-medium border-b-2 whitespace-nowrap ${
                    activeTab === tab.id
                      ? 'border-primary-600 text-primary-700'
                      : 'border-transparent text-gray-500 hover:text-gray-700'
                  }`}
                >
                  {tab.label}
                </button>
              ))}
            </div>

            {activeTab === 'summary' && (
              <div className="bg-white border border-gray-200 rounded-xl p-5">
                <h2 className="text-sm font-semibold text-gray-700 uppercase tracking-wider mb-4">Summary</h2>
                <div className="grid grid-cols-2 sm:grid-cols-4 gap-4 text-sm">
                  <div>
                    <span className="text-gray-500 block text-xs">Recovery Entries</span>
                    <span className="font-medium text-gray-900">{incident.summary.recoveryEntryCount}</span>
                  </div>
                  <div>
                    <span className="text-gray-500 block text-xs">Open Recovery Entries</span>
                    <span className="font-medium text-gray-900">{incident.summary.openRecoveryEntryCount}</span>
                  </div>
                  <div>
                    <span className="text-gray-500 block text-xs">Pending Decisions</span>
                    <span className="font-medium text-gray-900">{incident.summary.pendingDecisionCount}</span>
                  </div>
                  <div>
                    <span className="text-gray-500 block text-xs">Anomaly Flags</span>
                    <span className="font-medium text-gray-900">{incident.summary.anomalyFlagCount}</span>
                  </div>
                  <div>
                    <span className="text-gray-500 block text-xs">Drift Findings</span>
                    <span className="font-medium text-gray-900">{incident.summary.driftFindingCount}</span>
                  </div>
                  <div>
                    <span className="text-gray-500 block text-xs">Correlation Hypotheses</span>
                    <span className="font-medium text-gray-900">{incident.summary.correlationHypothesisCount}</span>
                  </div>
                  <div>
                    <span className="text-gray-500 block text-xs">Prevention Triggers</span>
                    <span className="font-medium text-gray-900">{incident.summary.preventionTriggerCount}</span>
                  </div>
                  <div>
                    <span className="text-gray-500 block text-xs">Replay Plans</span>
                    <span className="font-medium text-gray-900">{incident.summary.replayPlanCount}</span>
                  </div>
                </div>
                <div className="flex flex-wrap gap-3 mt-5 pt-4 border-t border-gray-100 text-sm">
                  <Link to={`${basePath}/recovery`} className="text-primary-600 hover:text-primary-700 font-medium">
                    View Recovery Ledger
                  </Link>
                  <Link to={`${basePath}/playbook`} className="text-primary-600 hover:text-primary-700 font-medium">
                    View Playbook Ledger
                  </Link>
                </div>
              </div>
            )}

            {activeTab === 'evidence' && (
              <div className="space-y-3">
                {evidenceEntries.length === 0 ? (
                  <EmptyState
                    icon={ClipboardList}
                    heading="No evidence recorded"
                    subtext="No anomaly flags, drift findings, correlation hypotheses, or AI-suggested observations have been proposed for this signature."
                    fillHeight={false}
                  />
                ) : (
                  evidenceEntries.map((entry) => <PlaybookEntryCard key={entry.id} entry={entry} />)
                )}
              </div>
            )}

            {activeTab === 'recovery' && (
              <div className="space-y-4">
                <div>
                  <h2 className="text-sm font-semibold text-gray-700 uppercase tracking-wider mb-2">Recommended</h2>
                  {recoveryProposals.length === 0 ? (
                    <EmptyState
                      icon={Wrench}
                      heading="No recovery proposals"
                      subtext="No replay plan or prevention trigger has been proposed for this signature."
                      fillHeight={false}
                    />
                  ) : (
                    <div className="space-y-3">
                      {recoveryProposals.map((entry) => (
                        <PlaybookEntryCard key={entry.id} entry={entry} />
                      ))}
                    </div>
                  )}
                </div>
                <div>
                  <h2 className="text-sm font-semibold text-gray-700 uppercase tracking-wider mb-2">Recovery Operations</h2>
                  {recoveryOperations.length === 0 ? (
                    <EmptyState
                      icon={RefreshCw}
                      heading="No recovery activity yet"
                      subtext="This signature has not been replayed or purged."
                      fillHeight={false}
                    />
                  ) : (
                    <div className="bg-white border border-gray-200 rounded-xl divide-y divide-gray-100">
                      {recoveryOperations.map(([operationId, entries]) => (
                        <Link
                          key={operationId}
                          to={`${basePath}/recovery/${operationId}`}
                          className="flex items-center justify-between gap-3 px-4 py-3 hover:bg-gray-50"
                        >
                          <div className="min-w-0">
                            <p className="text-sm font-medium text-gray-900 truncate">{entries[0].entityNameSnapshot ?? operationId}</p>
                            <p className="text-xs text-gray-500">{entries.length} entr{entries.length === 1 ? 'y' : 'ies'}</p>
                          </div>
                          <div className="flex flex-wrap gap-1 justify-end">
                            {Array.from(new Set(entries.map((e) => e.state))).map((state) => (
                              <RecoveryStateBadge key={state} state={state} />
                            ))}
                          </div>
                        </Link>
                      ))}
                    </div>
                  )}
                </div>
              </div>
            )}

            {activeTab === 'activity' && (
              <div className="bg-white border border-gray-200 rounded-xl p-5">
                <h2 className="text-sm font-semibold text-gray-700 uppercase tracking-wider mb-4">Activity</h2>
                {activity.length === 0 ? (
                  <EmptyState
                    icon={ActivityIcon}
                    heading="No activity yet"
                    subtext="Nothing has been recorded against this signature — no recovery attempt, no playbook proposal."
                    fillHeight={false}
                  />
                ) : (
                  <div className="divide-y divide-gray-100">
                    {activity.map((row) =>
                      row.kind === 'recovery' ? (
                        <Link
                          key={`recovery-${row.entry.id}`}
                          to={`${basePath}/recovery/${row.entry.operationId}`}
                          className="flex items-center justify-between gap-3 py-2.5 hover:bg-gray-50 rounded-lg px-2 -mx-2"
                        >
                          <div className="min-w-0">
                            <p className="text-sm text-gray-900">Recovery entry begun</p>
                            <p className="text-xs text-gray-500">{formatDate(row.at)}</p>
                          </div>
                          <RecoveryStateBadge state={row.entry.state} />
                        </Link>
                      ) : (
                        <div key={`playbook-${row.entry.id}`} className="flex items-center justify-between gap-3 py-2.5 px-2 -mx-2">
                          <div className="min-w-0">
                            <p className="text-sm text-gray-900">{row.entry.proposalKind} proposed</p>
                            <p className="text-xs text-gray-500">{formatDate(row.at)}</p>
                          </div>
                          <div className="flex items-center gap-2 shrink-0">
                            {row.entry.proposerKind === 'ReasoningAgent' && <AiSuggestionBadge />}
                            <PlaybookStateBadge state={row.entry.state} />
                          </div>
                        </div>
                      ),
                    )}
                  </div>
                )}
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}

export default IncidentWorkspacePage;
