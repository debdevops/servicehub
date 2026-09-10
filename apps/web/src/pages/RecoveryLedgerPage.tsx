import { useCallback, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import {
  ShieldCheck, RefreshCw, CheckCircle2, XCircle, HelpCircle, Activity, Lock, ShieldQuestion,
  Download, Bot, User, ChevronRight, ArrowRight, Hourglass, Circle, AlertTriangle, Ban, ExternalLink,
} from 'lucide-react';
import { useRecoveryOperations, useRecoveryEntries } from '@servicehub/ui-shared/hooks/useRecoveryLedger';
import { useRecoveryOperation, useDownloadRecoveryExport } from '@servicehub/ui-shared/hooks/useRecoveryOperation';
import { useVerifyChain } from '@servicehub/ui-shared/hooks/useChainVerification';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import {
  RECOVERY_LIMITATION_SENTENCE, RECOVERY_STATE_EXPLANATIONS, type RecoveryLedgerEntry, type RecoveryOperation,
} from '@servicehub/ui-shared/lib/api/recovery';
import type { CloudProviderType } from '@servicehub/ui-shared/lib/api/types';
import { EnvironmentBadge } from '@/components/EnvironmentBadge';
import {
  Callout, Card, CloudBadge, DetailPanel, EmptyBlock, ErrorBlock, FilterSelect, LoadingBlock, PageHeader,
  Pagination, Pill, SearchInput, StatCard, Tabs, buttonClass, formatDateTime, formatFullDateTime, navPrefixFor,
  normalizeProvider, plural, type Tone,
} from '@/components/autonomy/ui';
import {
  OUTCOME_META, TRIGGER_LABELS, buildRecoveryLifecycle, displayActor, bucketEntryState, computeLedgerStats, groupEntriesByOperation,
  summarizeOperation, type LifecycleStep, type OperationOutcome, type OperationSummary, type StepStatus,
} from '@/components/autonomy/recoveryOutcome';

const PAGE_SIZE = 15;
// The API's own upper bound for both list endpoints (RecoveryController.MaxLimit).
const FETCH_LIMIT = 500;

type ActorFilter = '' | 'human' | 'automation';

const OUTCOME_OPTIONS: ReadonlyArray<{ value: '' | OperationOutcome; label: string }> = [
  { value: '', label: 'All outcomes' },
  { value: 'Recovered', label: 'Recovered' },
  { value: 'Partial', label: 'Partial' },
  { value: 'Failed', label: 'Failed' },
  { value: 'InProgress', label: 'In progress' },
  { value: 'NeedsReview', label: 'Unverified' },
  { value: 'Blocked', label: 'Blocked by gate' },
  { value: 'Purged', label: 'Purged' },
];

function KindBadge({ kind }: { kind: RecoveryOperation['kind'] }) {
  return kind === 'Purge' ? <Pill tone="red">Purge</Pill> : <Pill tone="blue">Replay</Pill>;
}

function OutcomeBadge({ summary }: { summary: OperationSummary }) {
  const meta = OUTCOME_META[summary.outcome];
  return <Pill tone={meta.tone} title={meta.description}>{meta.label}</Pill>;
}

interface Row {
  operation: RecoveryOperation;
  summary: OperationSummary;
}

/**
 * `/recovery` — Recovery Evidence: the proof center for every replay and purge ServiceHub has
 * executed. Headline outcomes, a searchable ledger, and — for the selected recovery — the story of
 * what happened (Detect → Diagnose → Propose → Approve → Execute → Verify), with the forensic
 * detail (entries, hash chain, export) one tab away rather than on the main page.
 *
 * Outcomes are derived from each entry's recorded state; an operation whose entries aren't all
 * loaded here says so instead of guessing. `?op=` holds the selected recovery so it is a durable,
 * shareable URL; `?namespace=` scopes everything to one namespace.
 */
export default function RecoveryLedgerPage() {
  const { isDemoMode, cloudProvider } = useDemoContext();
  const navPrefix = navPrefixFor(isDemoMode, cloudProvider);
  const [searchParams, setSearchParams] = useSearchParams();
  const namespaceId = searchParams.get('namespace') || undefined;
  const selectedId = searchParams.get('op');

  const [search, setSearch] = useState('');
  const [outcome, setOutcome] = useState<'' | OperationOutcome>('');
  const [kind, setKind] = useState<'' | 'Replay' | 'Purge'>('');
  const [provider, setProvider] = useState<'' | CloudProviderType>('');
  const [actor, setActor] = useState<ActorFilter>('');
  const [page, setPage] = useState(0);

  const { data: operations, isLoading, isError, refetch, isFetching } = useRecoveryOperations(namespaceId, true, FETCH_LIMIT);
  const { data: entries } = useRecoveryEntries({ namespaceId, limit: FETCH_LIMIT });
  const { data: namespaces } = useNamespaces();
  const verifyChain = useVerifyChain();

  const updateParam = useCallback(
    (key: string, value: string | null) => {
      setSearchParams(prev => {
        const next = new URLSearchParams(prev);
        if (value) next.set(key, value);
        else next.delete(key);
        return next;
      });
    },
    [setSearchParams],
  );
  const closeDetail = useCallback(() => updateParam('op', null), [updateParam]);

  const rows: Row[] = useMemo(() => {
    const byOp = groupEntriesByOperation(entries ?? []);
    return (operations ?? []).map(op => ({ operation: op, summary: summarizeOperation(op, byOp.get(op.id) ?? []) }));
  }, [operations, entries]);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return rows.filter(({ operation: op, summary }) => {
      if (kind && op.kind !== kind) return false;
      if (outcome && summary.outcome !== outcome) return false;
      if (provider && normalizeProvider(op.providerSnapshot) !== provider) return false;
      if (actor) {
        const human = op.actorKind === 'User' || op.actorKind === 'ApiKey';
        if ((actor === 'human') !== human) return false;
      }
      if (q) {
        const haystack = [op.id, op.actorIdentity, op.scopeDescription, op.reason, op.namespaceNameSnapshot, op.trigger]
          .filter(Boolean)
          .join(' ')
          .toLowerCase();
        if (!haystack.includes(q)) return false;
      }
      return true;
    });
  }, [rows, search, kind, outcome, provider, actor]);

  const pageCount = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const safePage = Math.min(page, pageCount - 1);
  const visible = filtered.slice(safePage * PAGE_SIZE, (safePage + 1) * PAGE_SIZE);
  const stats = useMemo(() => computeLedgerStats(entries ?? []), [entries]);
  const filtersActive = !!(search || outcome || kind || provider || actor || namespaceId);

  const resetFilters = () => {
    setSearch('');
    setOutcome('');
    setKind('');
    setProvider('');
    setActor('');
    setPage(0);
    updateParam('namespace', null);
  };

  const newestOperationId = operations?.[0]?.id;
  const truncated = (entries?.length ?? 0) >= FETCH_LIMIT;

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      <PageHeader
        icon={ShieldCheck}
        tone="teal"
        title="Recovery Evidence"
        subtitle="Proof of every recovery ServiceHub has executed — who decided, what the provider was asked to do, and what was observed afterwards."
        actions={
          <button type="button" onClick={() => refetch()} disabled={isFetching || isDemoMode} className={buttonClass.secondary}>
            <RefreshCw className={`w-4 h-4 ${isFetching ? 'animate-spin' : ''}`} />
            Refresh
          </button>
        }
      >
        {isDemoMode && (
          <Callout tone="amber" className="mt-3">
            Demo Mode — this is fixture data illustrating the evidence model, not a real recovery record.
          </Callout>
        )}
      </PageHeader>

      <div className="flex-1 overflow-auto p-4 sm:p-6">
        <div className="@container max-w-7xl mx-auto space-y-5">
          {/* Headline numbers */}
          <div className="grid grid-cols-2 @3xl:grid-cols-3 @6xl:grid-cols-6 gap-3">
            <StatCard
              icon={Activity}
              tone="teal"
              label="Recoveries"
              value={operations ? (operations.length >= FETCH_LIMIT ? `${FETCH_LIMIT}+` : operations.length) : '…'}
              hint={stats.counts.inProgress > 0 ? `${stats.counts.inProgress} in progress now` : 'None in progress'}
            />
            <StatCard
              icon={CheckCircle2}
              tone="green"
              label="Recovered"
              value={stats.counts.recovered}
              hint={stats.verifiedRecoveryRate != null ? `${Math.round(stats.verifiedRecoveryRate * 100)}% of verified outcomes` : 'No verified outcomes yet'}
              title="Recovered ÷ (recovered + failed). Unverified and blocked messages are not counted either way."
            />
            <StatCard icon={XCircle} tone="red" label="Failed or returned" value={stats.counts.failed} hint="Rejected, or back in the DLQ" />
            <StatCard
              icon={HelpCircle}
              tone="amber"
              label="Unverified"
              value={stats.counts.unresolved}
              hint="Outcome couldn't be proven"
              title="For example, AWS and GCP cannot prove a replayed message did not return to the dead-letter queue."
            />
            <StatCard
              icon={Ban}
              tone="gray"
              label="Blocked by gate"
              value={stats.counts.blocked}
              hint="Refused before any provider call"
              title="The Eligibility Gate declined these before ServiceHub contacted the provider — for example, a signature that hasn't earned unattended trust."
            />
            <IntegrityCard
              disabled={!newestOperationId}
              isPending={verifyChain.isPending}
              result={verifyChain.data}
              failed={verifyChain.isError}
              onVerify={() => newestOperationId && verifyChain.mutate(newestOperationId)}
            />
          </div>

          {truncated && (
            <p className="text-xs text-gray-500 -mt-2">
              Figures cover the {FETCH_LIMIT} most recent ledger entries. Older recoveries are still in the ledger — filter by namespace, or open one, to see them.
            </p>
          )}

          {/* Filters */}
          <div className="bg-white border border-gray-200 rounded-xl shadow-sm p-3 flex flex-wrap items-center gap-2">
            <SearchInput
              value={search}
              onChange={v => { setSearch(v); setPage(0); }}
              placeholder="Search by actor, scope, reason, namespace or ID…"
              label="Search recoveries"
            />
            <FilterSelect label="Filter by outcome" value={outcome} onChange={v => { setOutcome(v); setPage(0); }} options={OUTCOME_OPTIONS} />
            <FilterSelect
              label="Filter by operation kind"
              value={kind}
              onChange={v => { setKind(v); setPage(0); }}
              options={[{ value: '', label: 'All kinds' }, { value: 'Replay', label: 'Replay' }, { value: 'Purge', label: 'Purge' }]}
            />
            <FilterSelect
              label="Filter by provider"
              value={provider}
              onChange={v => { setProvider(v); setPage(0); }}
              options={[{ value: '', label: 'All providers' }, { value: 'azure', label: 'Azure' }, { value: 'aws', label: 'AWS' }, { value: 'gcp', label: 'GCP' }]}
            />
            <FilterSelect
              label="Filter by actor"
              value={actor}
              onChange={v => { setActor(v); setPage(0); }}
              options={[{ value: '', label: 'All actors' }, { value: 'human', label: 'People & API keys' }, { value: 'automation', label: 'Automation' }]}
            />
            {!isDemoMode && (namespaces?.length ?? 0) > 0 && (
              <FilterSelect
                label="Filter by namespace"
                value={namespaceId ?? ''}
                onChange={v => { updateParam('namespace', v || null); setPage(0); }}
                options={[{ value: '', label: 'All namespaces' }, ...(namespaces ?? []).map(ns => ({ value: ns.id, label: ns.displayName || ns.name }))]}
              />
            )}
            {filtersActive && (
              <button type="button" onClick={resetFilters} className="text-sm text-gray-500 hover:text-gray-800 px-2">
                Reset
              </button>
            )}
          </div>

          <div className="grid grid-cols-1 @6xl:grid-cols-[minmax(0,1fr)_400px] gap-5 items-start">
            {/* Ledger */}
            <Card title={`Recovery ledger${operations ? ` (${filtered.length}${operations.length >= FETCH_LIMIT && !filtersActive ? '+' : ''})` : ''}`} icon={ShieldCheck} tone="teal" bodyClassName="p-0">
              {isLoading ? (
                <LoadingBlock label="Loading recoveries…" />
              ) : isError ? (
                <ErrorBlock title="Failed to load recovery operations" onRetry={() => refetch()} />
              ) : (operations ?? []).length === 0 ? (
                <EmptyBlock icon={ShieldCheck} title="No recovery operations recorded">
                  Replays and purges appear here as they happen — each with its full evidence trail.
                </EmptyBlock>
              ) : filtered.length === 0 ? (
                <EmptyBlock
                  icon={ShieldQuestion}
                  title="No recoveries match these filters"
                  action={<button type="button" onClick={resetFilters} className={buttonClass.secondary}>Reset filters</button>}
                />
              ) : (
                <>
                  <div className="overflow-x-auto">
                    <table className="w-full text-sm" aria-label="Recovery operations">
                      <thead className="bg-gray-50 border-b border-gray-200 text-xs text-gray-500">
                        <tr>
                          <th scope="col" className="px-4 py-2.5 text-left font-semibold whitespace-nowrap">Opened</th>
                          <th scope="col" className="px-4 py-2.5 text-left font-semibold">Recovery</th>
                          <th scope="col" className="px-4 py-2.5 text-left font-semibold">Where</th>
                          <th scope="col" className="px-4 py-2.5 text-left font-semibold">Actor</th>
                          <th scope="col" className="px-4 py-2.5 text-right font-semibold">Messages</th>
                          <th scope="col" className="px-4 py-2.5 text-left font-semibold">Outcome</th>
                          <th scope="col" className="w-8"><span className="sr-only">Open</span></th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-gray-100">
                        {visible.map(({ operation: op, summary }) => {
                          const selected = op.id === selectedId;
                          const human = op.actorKind === 'User' || op.actorKind === 'ApiKey';
                          return (
                            <tr
                              key={op.id}
                              onClick={() => updateParam('op', op.id)}
                              onKeyDown={e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); updateParam('op', op.id); } }}
                              tabIndex={0}
                              aria-selected={selected}
                              className={`cursor-pointer transition-colors focus:outline-none focus:bg-primary-50 ${selected ? 'bg-primary-50' : 'hover:bg-gray-50'}`}
                            >
                              <td className="px-4 py-3 text-gray-500 whitespace-nowrap text-xs tabular-nums">{formatDateTime(op.openedAt)}</td>
                              <td className="px-4 py-3 max-w-[18rem]">
                                <div className="flex items-center gap-1.5">
                                  <KindBadge kind={op.kind} />
                                  <span className="text-xs text-gray-500">{TRIGGER_LABELS[op.trigger]}</span>
                                </div>
                                <div className="text-xs text-gray-700 font-mono truncate mt-1" title={op.scopeDescription}>{op.scopeDescription}</div>
                                {op.reason && <div className="text-xs text-gray-500 truncate" title={op.reason}>“{op.reason}”</div>}
                              </td>
                              <td className="px-4 py-3">
                                <div className="flex items-center gap-1 flex-wrap">
                                  <CloudBadge provider={op.providerSnapshot} />
                                  <EnvironmentBadge env={op.environmentSnapshot} />
                                </div>
                                {op.namespaceNameSnapshot && <div className="text-xs text-gray-500 mt-1 truncate max-w-[10rem]">{op.namespaceNameSnapshot}</div>}
                              </td>
                              <td className="px-4 py-3">
                                <div className="flex items-center gap-1.5 text-xs text-gray-800 font-medium">
                                  {human ? <User className="w-3.5 h-3.5 text-gray-400" /> : <Bot className="w-3.5 h-3.5 text-violet-500" />}
                                  <span className="truncate max-w-[10rem]" title={op.actorIdentity}>{displayActor(op.actorIdentity)}</span>
                                </div>
                              </td>
                              <td className="px-4 py-3 text-right text-gray-800 font-medium tabular-nums">{op.entryCount}</td>
                              <td className="px-4 py-3"><OutcomeBadge summary={summary} /></td>
                              <td className="pr-3 text-gray-400"><ChevronRight className="w-4 h-4" /></td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  </div>
                  <Pagination page={safePage} pageCount={pageCount} total={filtered.length} pageSize={PAGE_SIZE} onPage={setPage} noun="recoveries" />
                </>
              )}
            </Card>

            {/* Detail, or — with nothing selected — what keeps needing recovery */}
            {selectedId ? (
              <RecoveryDetail operationId={selectedId} navPrefix={navPrefix} onClose={closeDetail} />
            ) : (
              <div className="space-y-5">
                <TopReasonsCard entries={entries ?? []} />
                <Card title="Keep evidence trustworthy" icon={Lock} tone="green">
                  <ul className="space-y-2 text-xs text-gray-600">
                    <li>
                      <Link to={`${navPrefix}/recovery/ageing`} className="text-primary-700 hover:underline font-medium">Recovery ageing</Link> — entries
                      stuck without a verdict, and how long they've been open.
                    </li>
                    <li>
                      <Link to={`${navPrefix}/approval-queue`} className="text-primary-700 hover:underline font-medium">Approval Queue</Link> — replays
                      waiting for a human decision.
                    </li>
                    <li>
                      Every verified recovery counts towards its failure signature's trust. See the{' '}
                      <Link to={`${navPrefix}/autonomy`} className="text-primary-700 hover:underline font-medium">Autonomy Control Center</Link>.
                    </li>
                  </ul>
                </Card>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

function IntegrityCard({
  disabled, isPending, result, failed, onVerify,
}: {
  disabled: boolean;
  isPending: boolean;
  result: { isValid: boolean; eventsChecked: number; firstDivergentSeq: number | null; reason: string | null } | undefined;
  failed: boolean;
  onVerify: () => void;
}) {
  const status = result
    ? result.isValid
      ? { tone: 'green' as Tone, text: `Chain intact · ${result.eventsChecked} events`, icon: CheckCircle2 }
      : { tone: 'red' as Tone, text: `Divergence at seq ${result.firstDivergentSeq}`, icon: AlertTriangle }
    : failed
      ? { tone: 'red' as Tone, text: 'Verification failed', icon: AlertTriangle }
      : { tone: 'gray' as Tone, text: 'Not verified this session', icon: Lock };
  const Icon = status.icon;
  return (
    <div className="col-span-2 @3xl:col-span-1 bg-white border border-gray-200 rounded-xl p-4 shadow-sm flex flex-col gap-2">
      <div className="text-xs font-medium text-gray-500">Evidence integrity</div>
      <div className="flex items-center gap-1.5 text-sm font-semibold text-gray-900">
        <Icon className={`w-4 h-4 ${status.tone === 'green' ? 'text-green-600' : status.tone === 'red' ? 'text-red-600' : 'text-gray-400'}`} />
        {status.text}
      </div>
      {result && !result.isValid && result.reason && <p className="text-xs text-red-700">{result.reason}</p>}
      <button type="button" onClick={onVerify} disabled={disabled || isPending} className={`${buttonClass.secondary} py-1.5 text-xs`}>
        <ShieldQuestion className="w-3.5 h-3.5" />
        {isPending ? 'Verifying…' : 'Verify chain'}
      </button>
      <p className="text-[11px] text-gray-400 leading-snug">Recomputes the hash chain. Tamper-evident, not tamper-proof.</p>
    </div>
  );
}

function TopReasonsCard({ entries }: { entries: readonly RecoveryLedgerEntry[] }) {
  const reasons = useMemo(() => {
    const counts = new Map<string, number>();
    for (const e of entries) {
      const reason = e.deadLetterReasonSnapshot ?? e.failureCategorySnapshot;
      if (reason) counts.set(reason, (counts.get(reason) ?? 0) + 1);
    }
    return Array.from(counts.entries()).sort((a, b) => b[1] - a[1]).slice(0, 5);
  }, [entries]);
  const max = reasons[0]?.[1] ?? 0;

  return (
    <Card title="What keeps needing recovery" icon={AlertTriangle} tone="amber">
      {reasons.length === 0 ? (
        <p className="text-xs text-gray-500">No dead-letter reasons recorded on recovered messages yet.</p>
      ) : (
        <ul className="space-y-2.5" aria-label="Most common dead-letter reasons">
          {reasons.map(([reason, count]) => (
            <li key={reason}>
              <div className="flex items-center justify-between text-xs">
                <span className="text-gray-700 truncate" title={reason}>{reason}</span>
                <span className="text-gray-500 tabular-nums ml-2">{count}</span>
              </div>
              <div className="mt-1 h-1.5 bg-gray-100 rounded-full overflow-hidden">
                <div className="h-full bg-amber-400 rounded-full" style={{ width: `${(count / max) * 100}%` }} />
              </div>
            </li>
          ))}
        </ul>
      )}
      <p className="text-[11px] text-gray-500 mt-3">Recurring reasons are candidates for a fix upstream or a prevention rule.</p>
    </Card>
  );
}

const STEP_STYLES: Record<StepStatus, { icon: typeof CheckCircle2; className: string; label: string }> = {
  done: { icon: CheckCircle2, className: 'text-green-600', label: 'Done' },
  failed: { icon: XCircle, className: 'text-red-600', label: 'Failed' },
  pending: { icon: Hourglass, className: 'text-blue-600', label: 'In progress' },
  warn: { icon: AlertTriangle, className: 'text-amber-600', label: 'Needs attention' },
  blocked: { icon: Ban, className: 'text-gray-500', label: 'Blocked' },
  skipped: { icon: Circle, className: 'text-gray-300', label: 'Not applicable' },
};

function Timeline({ steps }: { steps: LifecycleStep[] }) {
  return (
    <ol className="relative" aria-label="Recovery lifecycle">
      {steps.map((step, i) => {
        const style = STEP_STYLES[step.status];
        const Icon = style.icon;
        return (
          <li key={step.key} className="relative pl-8 pb-4 last:pb-0">
            {i < steps.length - 1 && <span className="absolute left-[9px] top-5 bottom-0 w-px bg-gray-200" aria-hidden="true" />}
            <Icon className={`absolute left-0 top-0.5 w-5 h-5 bg-white ${style.className}`} aria-label={style.label} />
            <div className="flex items-baseline justify-between gap-2">
              <span className="text-xs font-semibold uppercase tracking-wide text-gray-500">{step.label}</span>
              {step.at && <span className="text-[11px] text-gray-400 whitespace-nowrap">{formatDateTime(step.at)}</span>}
            </div>
            <div className={`text-sm ${step.status === 'skipped' ? 'text-gray-400' : 'text-gray-900'} font-medium`}>{step.title}</div>
            {step.detail && <p className="text-xs text-gray-500 mt-0.5">{step.detail}</p>}
          </li>
        );
      })}
    </ol>
  );
}

type DetailTab = 'story' | 'messages' | 'evidence';

function RecoveryDetail({ operationId, navPrefix, onClose }: { operationId: string; navPrefix: string; onClose: () => void }) {
  const { data, isLoading, isError } = useRecoveryOperation(operationId);
  const verifyChain = useVerifyChain();
  const downloadExport = useDownloadRecoveryExport();
  const [tab, setTab] = useState<DetailTab>('story');

  if (isLoading || isError || !data) {
    return (
      <DetailPanel title="Recovery details" onClose={onClose}>
        {isLoading ? <LoadingBlock /> : <ErrorBlock title="Recovery operation not found" detail="It may belong to another owner, or the link is out of date." />}
      </DetailPanel>
    );
  }

  const { operation, entries, events } = data;
  const summary = summarizeOperation(operation, entries);
  const meta = OUTCOME_META[summary.outcome];
  const steps = buildRecoveryLifecycle(data);
  const fullRecordHref = `${navPrefix}/recovery/${operation.id}`;

  return (
    <DetailPanel
      title={`${operation.kind} · ${plural(operation.entryCount, 'message')}`}
      badge={<Pill tone={meta.tone}>{meta.label}</Pill>}
      subtitle={<>Opened {formatFullDateTime(operation.openedAt)} · <span className="font-mono">{operation.id.slice(0, 8)}</span></>}
      onClose={onClose}
      footer={
        <Link to={fullRecordHref} className={`${buttonClass.secondary} w-full`}>
          Open full evidence record <ArrowRight className="w-3.5 h-3.5" />
        </Link>
      }
    >
      <Tabs<DetailTab>
        label="Recovery detail sections"
        active={tab}
        onChange={setTab}
        tabs={[
          { id: 'story', label: 'What happened' },
          { id: 'messages', label: 'Messages', count: entries.length },
          { id: 'evidence', label: 'Evidence' },
        ]}
      />

      {tab === 'story' && (
        <>
          <Callout tone={meta.tone} icon={summary.outcome === 'Recovered' ? CheckCircle2 : summary.outcome === 'Failed' ? XCircle : ShieldQuestion} title={meta.label}>
            {meta.description}
          </Callout>
          <Timeline steps={steps} />
          <p className="text-[11px] text-gray-500 border-t border-gray-100 pt-3">{RECOVERY_LIMITATION_SENTENCE}</p>
        </>
      )}

      {tab === 'messages' && (
        <ul className="divide-y divide-gray-100 border border-gray-200 rounded-lg">
          {entries.slice(0, 50).map(entry => {
            const bucket = bucketEntryState(entry.state);
            const tone: Tone =
              bucket === 'recovered' ? 'green' : bucket === 'failed' ? 'red' : bucket === 'inProgress' ? 'blue' : bucket === 'unresolved' ? 'amber' : 'gray';
            return (
              <li key={entry.id} className="px-3 py-2 text-xs flex items-center justify-between gap-2">
                <div className="min-w-0">
                  <div className="font-mono text-gray-700 truncate" title={entry.targetEntity}>{entry.targetEntity}</div>
                  <div className="text-gray-400 truncate">
                    {entry.dlqMessageId != null ? `DLQ #${entry.dlqMessageId}` : entry.bodyHash.slice(0, 16)}
                    {entry.verificationConfidence && ` · ${entry.verificationConfidence.toLowerCase()} match`}
                  </div>
                </div>
                <Pill tone={tone} title={RECOVERY_STATE_EXPLANATIONS[entry.state].summary}>{entry.state}</Pill>
              </li>
            );
          })}
          {entries.length > 50 && (
            <li className="px-3 py-2 text-xs text-gray-500">
              Showing 50 of {entries.length}. <Link to={fullRecordHref} className="text-primary-700 hover:underline">Open the full record</Link> for every entry.
            </li>
          )}
        </ul>
      )}

      {tab === 'evidence' && (
        <>
          <dl className="grid grid-cols-2 gap-x-3 gap-y-2 text-xs">
            <dt className="text-gray-500">Actor</dt>
            <dd className="text-gray-800 break-all">{displayActor(operation.actorIdentity)} <span className="text-gray-400">({operation.actorIdentity === '__spa__' ? 'web UI session' : operation.actorKind})</span></dd>
            <dt className="text-gray-500">Trigger</dt>
            <dd className="text-gray-800">{TRIGGER_LABELS[operation.trigger]}</dd>
            <dt className="text-gray-500">Namespace</dt>
            <dd className="text-gray-800">{operation.namespaceNameSnapshot ?? 'Cross-namespace'}</dd>
            <dt className="text-gray-500">Scope</dt>
            <dd className="text-gray-800 font-mono break-all">{operation.scopeDescription}</dd>
            <dt className="text-gray-500">Ledger events</dt>
            <dd className="text-gray-800">{events.length}</dd>
            <dt className="text-gray-500">ServiceHub version</dt>
            <dd className="text-gray-800">{operation.serviceVersion}</dd>
          </dl>
          <div className="flex flex-wrap gap-2">
            <button type="button" onClick={() => verifyChain.mutate(operation.id)} disabled={verifyChain.isPending} className={buttonClass.secondary}>
              <ShieldQuestion className="w-4 h-4" /> {verifyChain.isPending ? 'Verifying…' : 'Verify chain'}
            </button>
            <button type="button" onClick={() => downloadExport.mutate({ operationId: operation.id, format: 'json' })} disabled={downloadExport.isPending} className={buttonClass.secondary}>
              <Download className="w-4 h-4" /> Export JSON
            </button>
            <button type="button" onClick={() => downloadExport.mutate({ operationId: operation.id, format: 'csv' })} disabled={downloadExport.isPending} className={buttonClass.secondary}>
              <Download className="w-4 h-4" /> CSV
            </button>
          </div>
          {verifyChain.data && (
            <Callout tone={verifyChain.data.isValid ? 'green' : 'red'} title={verifyChain.data.isValid ? 'Chain intact' : 'Chain divergence detected'}>
              {verifyChain.data.isValid
                ? `${verifyChain.data.eventsChecked} events recomputed and matched.`
                : `First divergence at seq ${verifyChain.data.firstDivergentSeq}: ${verifyChain.data.reason}`}
            </Callout>
          )}
          <p className="text-[11px] text-gray-500">
            The chain covers every recovery you own, not just this one. Rehearsing the gate and writing off stuck entries live on the{' '}
            <Link to={fullRecordHref} className="text-primary-700 hover:underline inline-flex items-center gap-0.5">
              full record <ExternalLink className="w-3 h-3" />
            </Link>.
          </p>
        </>
      )}
    </DetailPanel>
  );
}
