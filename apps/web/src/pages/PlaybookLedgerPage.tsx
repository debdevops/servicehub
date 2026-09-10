import { useCallback, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import {
  ClipboardList, RefreshCw, Sparkles, Clock, CheckCircle2, XCircle, Target, Eye, Search, Network,
  Shield, RotateCcw, ChevronRight, ArrowRight, AlertTriangle, Lightbulb, Hand, GraduationCap, Info,
} from 'lucide-react';
import {
  usePlaybookEntries, usePlaybookEntry, useMarkPlaybookEntryUnderReview, useDispositionPlaybookEntry,
  useCorrelationAccountability, useBacktestReport,
} from '@servicehub/ui-shared/hooks/usePlaybookLedger';
import { useMe } from '@servicehub/ui-shared/hooks/useMe';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { PLAYBOOK_STATE_EXPLANATIONS, type PillarKind, type PlaybookEntry } from '@servicehub/ui-shared/lib/api/playbook';
import { EnvironmentBadge } from '@/components/EnvironmentBadge';
import {
  Callout, Card, CloudBadge, DetailPanel, EmptyBlock, ErrorBlock, FilterSelect, IconTile, LoadingBlock, PageHeader,
  Pagination, Pill, SearchInput, StatCard, buttonClass, formatDateTime, formatFullDateTime, navPrefixFor, plural,
} from '@/components/autonomy/ui';
import {
  PILLAR_META, PILLAR_ORDER, STATE_META, approvalMeaning, computePlaybookStats, humanizeIdentifier, parseJsonObject,
  proposalKindLabel, stateGroup, summarizeProposal, toReadableRows, type StateGroup,
} from '@/components/autonomy/playbookSummary';
import { isGovernanceRole, roleMeets } from '@/components/autonomy/governanceModel';

const PAGE_SIZE = 15;
const FETCH_LIMIT = 500;

const PILLAR_ICONS: Record<PillarKind, typeof Search> = { Investigate: Search, Correlate: Network, Prevent: Shield, Recover: RotateCcw };

type PillarTab = 'All' | PillarKind;
const STATE_FILTERS: ReadonlyArray<{ value: '' | StateGroup; label: string }> = [
  { value: '', label: 'All states' },
  { value: 'awaiting', label: 'Awaiting decision' },
  { value: 'approved', label: 'Approved' },
  { value: 'rejected', label: 'Rejected' },
  { value: 'closed', label: 'Expired, superseded or revoked' },
];

function AiSuggestionBadge() {
  return (
    <Pill
      tone="violet"
      icon={Sparkles}
      title="Proposed by the reasoning companion — an optional, self-hosted advisory service. It has no access to any ledger or broker and can only propose; a human decides."
    >
      AI suggestion
    </Pill>
  );
}

function StateBadge({ entry }: { entry: PlaybookEntry }) {
  const meta = STATE_META[entry.state];
  return <Pill tone={meta.tone} title={PLAYBOOK_STATE_EXPLANATIONS[entry.state]}>{meta.label}</Pill>;
}

/**
 * `/playbook` — the Playbook Ledger: what ServiceHub has noticed and proposed, what a human
 * decided, and whether later evidence bore it out. Organized around the learning loop
 * (observe → correlate → propose → human decision → corroborated learning) and the four pillars,
 * each phrased as the question it answers.
 *
 * Nothing here executes anything. Approving a proposal records that a human agrees it is sound —
 * for Prevent that means an observe-only rule, for Recover it still leaves the replay itself to the
 * Approval Queue and the Eligibility Gate. `?pillar=`, `?state=awaiting` and `?entry=` deep-link.
 */
export default function PlaybookLedgerPage() {
  const { isDemoMode, cloudProvider } = useDemoContext();
  const navPrefix = navPrefixFor(isDemoMode, cloudProvider);
  const [searchParams, setSearchParams] = useSearchParams();

  const pillarParam = searchParams.get('pillar');
  const pillar: PillarTab = PILLAR_ORDER.includes(pillarParam as PillarKind) ? (pillarParam as PillarKind) : 'All';
  const stateParam = searchParams.get('state');
  const stateFilter = (STATE_FILTERS.some(s => s.value === stateParam) ? stateParam : '') as '' | StateGroup;
  const namespaceId = searchParams.get('namespace') || undefined;
  const selectedId = searchParams.get('entry');

  const [search, setSearch] = useState('');
  const [proposer, setProposer] = useState<'' | 'System' | 'ReasoningAgent'>('');
  const [page, setPage] = useState(0);

  const { data: entries, isLoading, isError, refetch, isFetching } = usePlaybookEntries({ namespaceId, limit: FETCH_LIMIT });
  const { data: accountability } = useCorrelationAccountability();
  const { data: backtest } = useBacktestReport();
  const { data: namespaces } = useNamespaces();

  const updateParam = useCallback(
    (key: string, value: string | null) => {
      setSearchParams(prev => {
        const next = new URLSearchParams(prev);
        if (value) next.set(key, value);
        else next.delete(key);
        return next;
      });
      setPage(0);
    },
    [setSearchParams],
  );
  const closeDetail = useCallback(() => updateParam('entry', null), [updateParam]);

  const all = useMemo(() => entries ?? [], [entries]);
  const stats = useMemo(() => computePlaybookStats(all), [all]);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return all.filter(entry => {
      if (pillar !== 'All' && entry.pillarKind !== pillar) return false;
      if (stateFilter && stateGroup(entry.state) !== stateFilter) return false;
      if (proposer === 'ReasoningAgent' && entry.proposerKind !== 'ReasoningAgent') return false;
      if (proposer === 'System' && entry.proposerKind === 'ReasoningAgent') return false;
      if (q) {
        const s = summarizeProposal(entry);
        const haystack = [s.title, s.detail, entry.proposalKind, entry.namespaceNameSnapshot, entry.signatureHashSnapshot, entry.id]
          .filter(Boolean)
          .join(' ')
          .toLowerCase();
        if (!haystack.includes(q)) return false;
      }
      return true;
    });
  }, [all, pillar, stateFilter, proposer, search]);

  const pageCount = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const safePage = Math.min(page, pageCount - 1);
  const visible = filtered.slice(safePage * PAGE_SIZE, (safePage + 1) * PAGE_SIZE);
  const selected = all.find(e => e.id === selectedId) ?? null;
  const filtersActive = !!(search || stateFilter || proposer || namespaceId || pillar !== 'All');

  const resetFilters = () => {
    setSearch('');
    setProposer('');
    setSearchParams(prev => {
      const next = new URLSearchParams(prev);
      ['pillar', 'state', 'namespace'].forEach(k => next.delete(k));
      return next;
    });
    setPage(0);
  };

  const decided = stats.approved + stats.rejected;
  const truncated = all.length >= FETCH_LIMIT;

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      <PageHeader
        icon={ClipboardList}
        tone="indigo"
        title="Playbook Ledger"
        subtitle="What ServiceHub noticed and proposed, what a person decided, and whether later evidence proved it right. Approving here never executes anything."
        actions={
          <button type="button" onClick={() => refetch()} disabled={isFetching || isDemoMode} className={buttonClass.secondary}>
            <RefreshCw className={`w-4 h-4 ${isFetching ? 'animate-spin' : ''}`} />
            Refresh
          </button>
        }
      >
        {isDemoMode && (
          <Callout tone="amber" className="mt-3">
            Demo Mode — the Playbook Ledger has no fixture data, so this view is always empty here.
          </Callout>
        )}
      </PageHeader>

      <div className="flex-1 overflow-auto p-4 sm:p-6">
        <div className="@container max-w-7xl mx-auto space-y-5">
          <div className="grid grid-cols-2 @4xl:grid-cols-5 gap-3">
            <StatCard icon={ClipboardList} tone="indigo" label="Proposals" value={truncated ? '500+' : stats.total} hint={stats.aiAuthored > 0 ? `${stats.aiAuthored} from the AI companion` : 'From detection workers'} />
            <StatCard icon={Clock} tone="amber" label="Awaiting a decision" value={stats.awaiting} hint="Proposed or under review" />
            <StatCard
              icon={CheckCircle2}
              tone="green"
              label="Approved"
              value={stats.approved}
              hint={stats.approvalRate != null ? `${Math.round(stats.approvalRate * 100)}% of ${decided} decided` : 'None decided yet'}
            />
            <StatCard icon={XCircle} tone="red" label="Rejected" value={stats.rejected} hint={`${stats.closed} expired or superseded`} />
            <StatCard
              icon={Target}
              tone="teal"
              label="Borne out later"
              value={backtest && backtest.totalBacktested > 0 ? `${backtest.corroboratedCount}/${backtest.totalBacktested}` : '—'}
              hint={backtest && backtest.corroborationRate != null ? `${Math.round(backtest.corroborationRate * 100)}% corroborated by recovery evidence` : 'Nothing backtested yet'}
              title="Backtesting: whether dispositioned anomaly and drift findings were followed by real recovery activity for the same entity."
            />
          </div>

          <LearningLoop
            observations={stats.byPillar.Investigate}
            patterns={stats.byPillar.Correlate}
            patternApproval={accountability?.approvalRate ?? null}
            proposals={stats.total}
            decisions={decided}
            corroborated={backtest?.corroboratedCount ?? null}
            approvalRate={stats.approvalRate}
          />

          {/* Pillars as questions */}
          <div className="grid grid-cols-2 @4xl:grid-cols-5 gap-2" role="tablist" aria-label="Filter by pillar">
            {(['All', ...PILLAR_ORDER] as PillarTab[]).map(p => {
              const active = p === pillar;
              const Icon = p === 'All' ? ClipboardList : PILLAR_ICONS[p];
              return (
                <button
                  key={p}
                  type="button"
                  role="tab"
                  aria-selected={active}
                  onClick={() => updateParam('pillar', p === 'All' ? null : p)}
                  className={`text-left rounded-xl border p-3 transition-all ${
                    active ? 'border-primary-400 bg-primary-50 ring-1 ring-primary-200' : 'border-gray-200 bg-white hover:border-primary-200'
                  }`}
                >
                  <div className="flex items-center gap-2">
                    <IconTile icon={Icon} tone={p === 'All' ? 'indigo' : PILLAR_META[p].tone} size="sm" />
                    <span className="text-sm font-semibold text-gray-900">{p === 'All' ? 'All proposals' : p}</span>
                    <span className="ml-auto text-xs text-gray-500 tabular-nums">{p === 'All' ? stats.total : stats.byPillar[p]}</span>
                  </div>
                  <div className="text-xs text-gray-500 mt-1.5">{p === 'All' ? 'Everything ServiceHub proposed' : PILLAR_META[p].question}</div>
                </button>
              );
            })}
          </div>
          {pillar !== 'All' && (
            <p className="text-xs text-gray-600 -mt-2 flex items-start gap-1.5">
              <Info className="w-3.5 h-3.5 shrink-0 mt-0.5 text-primary-600" />
              {PILLAR_META[pillar].explainer}
            </p>
          )}

          <div className="bg-white border border-gray-200 rounded-xl shadow-sm p-3 flex flex-wrap items-center gap-2">
            <SearchInput value={search} onChange={v => { setSearch(v); setPage(0); }} placeholder="Search proposals, entities, namespaces or signatures…" label="Search proposals" />
            <FilterSelect label="Filter by state" value={stateFilter} onChange={v => updateParam('state', v || null)} options={STATE_FILTERS} />
            <FilterSelect
              label="Filter by proposer"
              value={proposer}
              onChange={v => { setProposer(v); setPage(0); }}
              options={[{ value: '', label: 'All proposers' }, { value: 'System', label: 'Detection workers' }, { value: 'ReasoningAgent', label: 'AI companion' }]}
            />
            {!isDemoMode && (namespaces?.length ?? 0) > 0 && (
              <FilterSelect
                label="Filter by namespace"
                value={namespaceId ?? ''}
                onChange={v => updateParam('namespace', v || null)}
                options={[{ value: '', label: 'All namespaces' }, ...(namespaces ?? []).map(ns => ({ value: ns.id, label: ns.displayName || ns.name }))]}
              />
            )}
            {filtersActive && (
              <button type="button" onClick={resetFilters} className="text-sm text-gray-500 hover:text-gray-800 px-2">Reset</button>
            )}
          </div>
          {namespaceId && (
            <p className="text-xs text-gray-500 -mt-2">Fleet-wide proposals (such as cross-namespace correlations) are hidden while a namespace is selected.</p>
          )}

          <div className="grid grid-cols-1 @6xl:grid-cols-[minmax(0,1fr)_420px] gap-5 items-start">
            <Card title={`Proposals${entries ? ` (${filtered.length})` : ''}`} icon={ClipboardList} tone="indigo" bodyClassName="p-0">
              {isLoading ? (
                <LoadingBlock label="Loading proposals…" />
              ) : isError ? (
                <ErrorBlock title="Failed to load Playbook Ledger entries" onRetry={() => refetch()} />
              ) : all.length === 0 ? (
                <EmptyBlock icon={ClipboardList} title="No proposals recorded">
                  Anomaly, drift, correlation and replay-plan findings above the significance threshold appear here for a human to decide.
                </EmptyBlock>
              ) : filtered.length === 0 ? (
                <EmptyBlock
                  icon={Search}
                  title="No proposals match these filters"
                  action={<button type="button" onClick={resetFilters} className={buttonClass.secondary}>Reset filters</button>}
                />
              ) : (
                <>
                  <div className="overflow-x-auto">
                    <table className="w-full text-sm" aria-label="Playbook Ledger entries">
                      <thead className="bg-gray-50 border-b border-gray-200 text-xs text-gray-500">
                        <tr>
                          <th scope="col" className="px-4 py-2.5 text-left font-semibold whitespace-nowrap">Proposed</th>
                          <th scope="col" className="px-4 py-2.5 text-left font-semibold">Pillar</th>
                          <th scope="col" className="px-4 py-2.5 text-left font-semibold">Proposal</th>
                          <th scope="col" className="px-4 py-2.5 text-left font-semibold">Scope</th>
                          <th scope="col" className="px-4 py-2.5 text-left font-semibold">State</th>
                          <th scope="col" className="w-8"><span className="sr-only">Open</span></th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-gray-100">
                        {visible.map(entry => {
                          const summary = summarizeProposal(entry);
                          const isSelected = entry.id === selectedId;
                          return (
                            <tr
                              key={entry.id}
                              onClick={() => updateParam('entry', entry.id)}
                              onKeyDown={e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); updateParam('entry', entry.id); } }}
                              tabIndex={0}
                              aria-selected={isSelected}
                              className={`cursor-pointer transition-colors focus:outline-none focus:bg-primary-50 ${isSelected ? 'bg-primary-50' : 'hover:bg-gray-50'}`}
                            >
                              <td className="px-4 py-3 text-gray-500 whitespace-nowrap text-xs tabular-nums">{formatDateTime(entry.proposedAt)}</td>
                              <td className="px-4 py-3">
                                <Pill tone={PILLAR_META[entry.pillarKind].tone}>{entry.pillarKind}</Pill>
                                <div className="text-[11px] text-gray-500 mt-1">{proposalKindLabel(entry.proposalKind)}</div>
                              </td>
                              <td className="px-4 py-3 max-w-[22rem]">
                                <div className="flex items-center gap-1.5 flex-wrap">
                                  <span className="text-gray-900 font-medium text-xs">{summary.title}</span>
                                  {entry.proposerKind === 'ReasoningAgent' && <AiSuggestionBadge />}
                                </div>
                                {summary.detail && <div className="text-xs text-gray-500 truncate mt-0.5" title={summary.detail}>{summary.detail}</div>}
                              </td>
                              <td className="px-4 py-3">
                                {entry.namespaceNameSnapshot ? (
                                  <div className="flex items-center gap-1 flex-wrap">
                                    <CloudBadge provider={entry.providerSnapshot} />
                                    <EnvironmentBadge env={entry.environmentSnapshot} />
                                    <span className="text-xs text-gray-500 truncate max-w-[9rem]">{entry.namespaceNameSnapshot}</span>
                                  </div>
                                ) : (
                                  <span className="text-xs text-gray-400">Fleet-wide</span>
                                )}
                              </td>
                              <td className="px-4 py-3"><StateBadge entry={entry} /></td>
                              <td className="pr-3 text-gray-400"><ChevronRight className="w-4 h-4" /></td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  </div>
                  <Pagination page={safePage} pageCount={pageCount} total={filtered.length} pageSize={PAGE_SIZE} onPage={setPage} noun="proposals" />
                </>
              )}
            </Card>

            {selected ? (
              <ProposalDetail entry={selected} navPrefix={navPrefix} onClose={closeDetail} />
            ) : (
              <Card title="How to read a proposal" icon={GraduationCap} tone="indigo">
                <ul className="space-y-2.5 text-xs text-gray-600">
                  <li className="flex gap-2"><Eye className="w-4 h-4 text-gray-400 shrink-0" /> A proposal is something ServiceHub noticed — never an action it took.</li>
                  <li className="flex gap-2"><Hand className="w-4 h-4 text-gray-400 shrink-0" /> Approving means “a person agrees this is sound.” It doesn't replay, purge or change autonomy.</li>
                  <li className="flex gap-2"><Shield className="w-4 h-4 text-gray-400 shrink-0" /> Approved prevention rules are observe-only: they record recurrences and never act.</li>
                  <li className="flex gap-2"><Target className="w-4 h-4 text-gray-400 shrink-0" /> Backtesting later checks decided findings against what actually happened.</li>
                </ul>
                {stats.awaiting > 0 && (
                  <button type="button" onClick={() => updateParam('state', 'awaiting')} className={`${buttonClass.primary} w-full mt-4`}>
                    Review {plural(stats.awaiting, 'waiting proposal')}
                  </button>
                )}
              </Card>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

function LearningLoop({
  observations, patterns, patternApproval, proposals, decisions, corroborated, approvalRate,
}: {
  observations: number;
  patterns: number;
  patternApproval: number | null;
  proposals: number;
  decisions: number;
  corroborated: number | null;
  approvalRate: number | null;
}) {
  const steps: Array<{ label: string; value: string; hint: string; icon: typeof Eye }> = [
    { label: 'Incident', value: '—', hint: 'A message dead-letters', icon: AlertTriangle },
    { label: 'Observation', value: String(observations), hint: 'Anomalies spotted', icon: Eye },
    { label: 'Pattern', value: String(patterns), hint: patternApproval != null ? `${Math.round(patternApproval * 100)}% approved` : 'Related failures', icon: Network },
    { label: 'Proposal', value: String(proposals), hint: 'Written to the ledger', icon: Lightbulb },
    { label: 'Human decision', value: String(decisions), hint: 'Approved or rejected', icon: Hand },
    { label: 'Corroborated', value: corroborated != null ? String(corroborated) : '—', hint: 'Borne out by evidence', icon: Target },
    { label: 'Better decisions', value: approvalRate != null ? `${Math.round(approvalRate * 100)}%` : '—', hint: 'Proposal approval rate', icon: GraduationCap },
  ];
  return (
    <Card title="The learning loop" icon={GraduationCap} tone="indigo" bodyClassName="p-3">
      <ol className="grid grid-cols-2 @xl:grid-cols-4 @5xl:grid-cols-7 gap-2" aria-label="Learning loop">
        {steps.map((step, i) => (
          <li key={step.label} className="relative rounded-lg bg-gray-50/70 border border-gray-100 px-3 py-2.5">
            <div className="flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-wide text-gray-500">
              <step.icon className="w-3.5 h-3.5" /> {step.label}
            </div>
            <div className="text-lg font-bold text-gray-900 mt-1 tabular-nums">{step.value}</div>
            <div className="text-[11px] text-gray-500">{step.hint}</div>
            {i < steps.length - 1 && <ArrowRight className="hidden xl:block absolute -right-2 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-gray-300 bg-white rounded-full" />}
          </li>
        ))}
      </ol>
    </Card>
  );
}

function ProposalDetail({ entry, navPrefix, onClose }: { entry: PlaybookEntry; navPrefix: string; onClose: () => void }) {
  const { isDemoMode } = useDemoContext();
  const { data: detail, isLoading } = usePlaybookEntry(entry.id);
  const { data: me } = useMe();
  const markUnderReview = useMarkPlaybookEntryUnderReview();
  const disposition = useDispositionPlaybookEntry();
  const [rejecting, setRejecting] = useState(false);
  const [rejectReason, setRejectReason] = useState('');

  const summary = summarizeProposal(entry);
  const isActionable = stateGroup(entry.state) === 'awaiting';
  const evidenceRows = toReadableRows(entry.evidenceRefJson);
  const proposalRows = toReadableRows(entry.proposalJson);
  const reasoning = entry.proposalKind === 'ReasoningCompanionObservation' ? parseJsonObject(entry.proposalJson) : null;
  const considerations = Array.isArray(reasoning?.Considerations) ? (reasoning!.Considerations as unknown[]).filter((c): c is string => typeof c === 'string') : [];
  const fleetRole = me?.governanceRole;
  const mayLackRole = isGovernanceRole(fleetRole) ? !roleMeets(fleetRole, 'Approver') : fleetRole == null && !isDemoMode;

  return (
    <DetailPanel
      title={summary.title}
      badge={<StateBadge entry={entry} />}
      subtitle={`${proposalKindLabel(entry.proposalKind)} · proposed ${formatFullDateTime(entry.proposedAt)}`}
      onClose={onClose}
      footer={
        isActionable ? (
          <div className="space-y-2">
            {mayLackRole && (
              <p className="text-[11px] text-amber-800 flex items-start gap-1">
                <AlertTriangle className="w-3.5 h-3.5 shrink-0" />
                Deciding needs the Approver role for this namespace and pillar. Your fleet-wide role is {fleetRole ?? 'none'} — the server
                will refuse unless a scoped grant covers it.
              </p>
            )}
            {rejecting ? (
              <div className="space-y-2">
                <input
                  type="text"
                  value={rejectReason}
                  onChange={e => setRejectReason(e.target.value)}
                  placeholder="Why is this not worth acting on? (required)"
                  aria-label="Rejection reason"
                  className="w-full text-sm border border-gray-200 rounded-lg px-3 py-2 focus:outline-none focus:ring-2 focus:ring-primary-500"
                />
                <div className="flex gap-2">
                  <button
                    type="button"
                    onClick={() => disposition.mutate({ entryId: entry.id, disposition: 'Rejected', reason: rejectReason.trim() }, { onSuccess: () => setRejecting(false) })}
                    disabled={!rejectReason.trim() || disposition.isPending}
                    className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 text-sm font-medium text-white bg-red-600 hover:bg-red-700 rounded-lg disabled:opacity-50"
                  >
                    Confirm reject
                  </button>
                  <button type="button" onClick={() => setRejecting(false)} className={buttonClass.secondary}>Cancel</button>
                </div>
              </div>
            ) : (
              <div className="flex gap-2">
                {entry.state === 'Proposed' && (
                  <button type="button" onClick={() => markUnderReview.mutate(entry.id)} disabled={markUnderReview.isPending} className={buttonClass.secondary} title="Signal that you're looking at this">
                    <Eye className="w-4 h-4" /> Reviewing
                  </button>
                )}
                <button type="button" onClick={() => setRejecting(true)} className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 text-sm font-medium text-red-700 bg-white border border-red-200 hover:bg-red-50 rounded-lg">
                  <XCircle className="w-4 h-4" /> Reject
                </button>
                <button
                  type="button"
                  onClick={() => disposition.mutate({ entryId: entry.id, disposition: 'Approved' })}
                  disabled={disposition.isPending}
                  className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 text-sm font-medium text-white bg-green-600 hover:bg-green-700 rounded-lg disabled:opacity-50"
                >
                  <CheckCircle2 className="w-4 h-4" /> Approve
                </button>
              </div>
            )}
          </div>
        ) : undefined
      }
    >
      <div className="flex items-center gap-1.5 flex-wrap">
        <Pill tone={PILLAR_META[entry.pillarKind].tone}>{entry.pillarKind}</Pill>
        {entry.proposerKind === 'ReasoningAgent' && <AiSuggestionBadge />}
        {entry.namespaceNameSnapshot ? (
          <>
            <CloudBadge provider={entry.providerSnapshot} />
            <EnvironmentBadge env={entry.environmentSnapshot} />
            <span className="text-xs text-gray-500">{entry.namespaceNameSnapshot}</span>
          </>
        ) : (
          <Pill tone="gray">Fleet-wide</Pill>
        )}
      </div>

      {summary.detail && <p className="text-sm text-gray-700">{summary.detail}</p>}

      {reasoning && (
        <Callout tone="violet" icon={Sparkles} title="AI-suggested observation">
          {considerations.length > 0 && (
            <ul className="list-disc pl-4 space-y-0.5">
              {considerations.map(c => <li key={c}>{c}</li>)}
            </ul>
          )}
        </Callout>
      )}

      {(summary.severity != null || summary.recommendedActions.length > 0) && (
        <section>
          <h3 className="text-xs font-semibold text-gray-500 mb-1.5">Suggested next step</h3>
          {summary.severity != null && <p className="text-xs text-gray-600 mb-1">Detector severity: <span className="font-semibold text-gray-800">{summary.severity}/100</span></p>}
          {summary.recommendedActions.length > 0 && (
            <ul className="list-disc pl-4 text-xs text-gray-700 space-y-0.5">
              {summary.recommendedActions.map(a => <li key={a}>{a}</li>)}
            </ul>
          )}
        </section>
      )}

      <Callout tone={entry.pillarKind === 'Prevent' ? 'teal' : 'gray'} icon={Info} title="What approving means">
        {approvalMeaning(entry)}
      </Callout>

      <section>
        <h3 className="text-xs font-semibold text-gray-500 mb-1.5">Evidence</h3>
        {evidenceRows.length === 0 ? (
          <p className="text-xs text-gray-400">No structured evidence reference recorded.</p>
        ) : (
          <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-xs">
            {evidenceRows.map(r => (
              <div key={r.label} className="contents">
                <dt className="text-gray-500">{r.label}</dt>
                <dd className="text-gray-800 break-all">{r.value}</dd>
              </div>
            ))}
          </dl>
        )}
        <div className="flex flex-wrap gap-3 mt-2 text-xs">
          {entry.relatedRecoveryOperationId && (
            <Link to={`${navPrefix}/recovery?op=${entry.relatedRecoveryOperationId}`} className="text-primary-700 hover:underline">Related recovery →</Link>
          )}
          {entry.signatureHashSnapshot && (
            <Link to={`${navPrefix}/signatures/${entry.signatureHashSnapshot}`} className="text-primary-700 hover:underline">Failure signature →</Link>
          )}
        </div>
      </section>

      {proposalRows.length > 0 && (
        <details className="group">
          <summary className="text-xs font-semibold text-gray-500 cursor-pointer select-none">Proposal details</summary>
          <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-xs mt-2">
            {proposalRows.map(r => (
              <div key={r.label} className="contents">
                <dt className="text-gray-500">{r.label}</dt>
                <dd className="text-gray-800 break-all">{r.value}</dd>
              </div>
            ))}
          </dl>
        </details>
      )}

      <section>
        <h3 className="text-xs font-semibold text-gray-500 mb-1.5">History</h3>
        {isLoading ? (
          <p className="text-xs text-gray-400">Loading…</p>
        ) : (detail?.events ?? []).length === 0 ? (
          <p className="text-xs text-gray-400">No events loaded.</p>
        ) : (
          <ol className="space-y-2">
            {(detail?.events ?? []).map(evt => (
              <li key={evt.id} className="text-xs flex items-start gap-2">
                <span className="w-1.5 h-1.5 rounded-full bg-primary-400 mt-1.5 shrink-0" />
                <div className="min-w-0">
                  <span className="font-medium text-gray-800">{humanizeIdentifier(evt.eventType)}</span>
                  <span className="text-gray-500"> · {evt.actorKind === 'ReasoningAgent' ? 'AI companion' : evt.actorIdentity}</span>
                  <div className="text-gray-400">{formatFullDateTime(evt.occurredAt)}</div>
                </div>
              </li>
            ))}
          </ol>
        )}
        {isActionable && <p className="text-[11px] text-gray-500 mt-2">Expires {formatDateTime(entry.expiresAt)} if no one decides.</p>}
      </section>
    </DetailPanel>
  );
}
