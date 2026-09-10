import { useMemo, useState, type ComponentType, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import {
  Gauge, ShieldAlert, RefreshCw, Info, ShieldCheck, ClipboardList, Users, CheckCircle2, Clock,
  ShieldX, Lock, Eye, Network, Sparkles, RotateCcw, ArrowRight, TrendingUp, TrendingDown, Zap,
  Cloud, Bot, User, Search, Lightbulb, Hand, BadgeCheck, CircleSlash,
} from 'lucide-react';
import {
  useAutonomyDashboard, useApprovalQueue, useOutcomeMetrics, useRecoveryOperations, useRecoveryEntries,
} from '@servicehub/ui-shared/hooks/useRecoveryLedger';
import { usePlaybookEntries } from '@servicehub/ui-shared/hooks/usePlaybookLedger';
import { useProviderCapabilities } from '@servicehub/ui-shared/hooks/useCloudBridge';
import { useMe } from '@servicehub/ui-shared/hooks/useMe';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useGovernanceGrants } from '@servicehub/ui-shared/hooks/useGovernanceGrants';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import type { CloudProviderType } from '@servicehub/ui-shared/lib/api/types';
import type { AutonomyDashboardOverview, RecoveryLedgerEntry, RecoveryOperation } from '@servicehub/ui-shared/lib/api/recovery';
import type { PlaybookEntry } from '@servicehub/ui-shared/lib/api/playbook';
import {
  Card, Callout, ErrorBlock, FilterSelect, IconTile, LoadingBlock, PageHeader, Pill, StatCard,
  buttonClass, formatDateTime, navPrefixFor, plural, truncateMiddle, PROVIDER_LABELS, type Tone,
} from '@/components/autonomy/ui';
import { computePlaybookStats, summarizeProposal } from '@/components/autonomy/playbookSummary';
import { displayActor, groupEntriesByOperation, summarizeOperation, type OperationOutcome } from '@/components/autonomy/recoveryOutcome';

type IconType = ComponentType<{ className?: string }>;

// The six levels of the backend's AutonomyLevel enum. L0-L2 never execute anything, so they are
// always on; L3 is the permanent human-approved floor every signature starts at and can be
// demoted back to; L4/L5 are earned per failure signature from verified outcomes — never set.
const LADDER: ReadonlyArray<{ level: number; name: string; blurb: string }> = [
  { level: 0, name: 'Observe', blurb: 'Watches queues' },
  { level: 1, name: 'Explain', blurb: 'Classifies failures' },
  { level: 2, name: 'Recommend', blurb: 'Proposes actions' },
  { level: 3, name: 'Approve', blurb: 'A human approves' },
  { level: 4, name: 'Standing', blurb: 'Earned, per signature' },
  { level: 5, name: 'Unattended', blurb: 'Earned, per signature' },
];

// The Approval Queue and Playbook reads are capped by the API; a list that comes back exactly at
// the cap means "at least this many", so it is shown as "100+" rather than as an exact count.
const APPROVAL_QUEUE_LIMIT = 100;
const PLAYBOOK_LIMIT = 500;

function atLeast(count: number, limit: number): string {
  return count >= limit ? `${count}+` : String(count);
}

const PROVIDER_KEYS: ReadonlyArray<[CloudProviderType, string]> = [['azure', 'Azure'], ['aws', 'Aws'], ['gcp', 'Gcp']];

function levelCount(overview: AutonomyDashboardOverview | undefined, level: number): number {
  return (overview?.levelCounts ?? []).filter(c => c.level === level).reduce((sum, c) => sum + c.count, 0);
}

type ActivityKind = 'promotion' | 'demotion' | 'recovery' | 'decision';

interface ActivityItem {
  key: string;
  at: string;
  kind: ActivityKind;
  title: string;
  detail: string;
  href: string;
  tone: Tone;
  icon: IconType;
}

function buildActivity(
  overview: AutonomyDashboardOverview | undefined,
  operations: readonly RecoveryOperation[],
  entries: readonly RecoveryLedgerEntry[],
  proposals: readonly PlaybookEntry[],
  prefix: string,
): ActivityItem[] {
  const items: ActivityItem[] = [];
  for (const [i, t] of (overview?.recentTransitions ?? []).entries()) {
    const promoted = t.newLevel > t.previousLevel;
    items.push({
      key: `t-${i}`,
      at: t.occurredAtUtc,
      kind: promoted ? 'promotion' : 'demotion',
      title: `${promoted ? 'Promoted' : 'Demoted'} L${t.previousLevel} → L${t.newLevel}`,
      detail: `${t.actionKind} · signature ${truncateMiddle(t.signatureHash, 6)} — ${t.reason}`,
      href: `${prefix}/signatures/${t.signatureHash}`,
      tone: promoted ? 'green' : 'amber',
      icon: promoted ? TrendingUp : TrendingDown,
    });
  }
  // The activity line says what actually happened, from the operation's own ledger entries — an
  // automated attempt the Eligibility Gate refused must never read as "ran unattended".
  const byOp = groupEntriesByOperation(entries);
  const OUTCOME_PHRASE: Record<OperationOutcome, string> = {
    Recovered: 'recovered',
    Purged: 'purged',
    Failed: 'failed',
    Partial: 'partly recovered',
    InProgress: 'being verified',
    NeedsReview: 'unverified',
    Blocked: 'blocked by the Eligibility Gate',
    Unknown: '',
  };
  for (const op of operations.slice(0, 8)) {
    const automated = op.actorKind === 'Automation' || op.actorKind === 'System';
    const { outcome } = summarizeOperation(op, byOp.get(op.id) ?? []);
    const who = automated ? 'Automated' : `${displayActor(op.actorIdentity)}:`;
    const what = `${op.kind.toLowerCase()} of ${plural(op.entryCount, 'message')}`;
    const phrase = OUTCOME_PHRASE[outcome];
    items.push({
      key: `r-${op.id}`,
      at: op.openedAt,
      kind: 'recovery',
      title: `${who} ${what}${phrase ? ` — ${phrase}` : ''}`,
      detail: op.scopeDescription,
      href: `${prefix}/recovery?op=${op.id}`,
      tone: outcome === 'Blocked' ? 'gray' : outcome === 'Failed' ? 'red' : automated ? 'violet' : 'teal',
      icon: automated ? Bot : RotateCcw,
    });
  }
  for (const entry of proposals) {
    if (!entry.closedAt || !entry.disposition) continue;
    items.push({
      key: `p-${entry.id}`,
      at: entry.closedAt,
      kind: 'decision',
      title: `Proposal ${entry.disposition.toLowerCase()}`,
      detail: `${entry.pillarKind} · ${summarizeProposal(entry).title}`,
      href: `${prefix}/playbook?entry=${entry.id}`,
      tone: entry.disposition === 'Approved' ? 'green' : 'gray',
      icon: ClipboardList,
    });
  }
  return items.sort((a, b) => b.at.localeCompare(a.at)).slice(0, 6);
}

function LadderStep({ step, reached, isFloor, count }: { step: (typeof LADDER)[number]; reached: boolean; isFloor: boolean; count?: number }) {
  return (
    <li className="flex-1 min-w-[5.5rem] flex flex-col items-center text-center">
      <div
        className={`w-8 h-8 rounded-full flex items-center justify-center text-xs font-bold ring-2 ${
          reached ? 'bg-primary-600 text-white ring-primary-200' : 'bg-white text-gray-400 ring-gray-200'
        }`}
      >
        L{step.level}
      </div>
      <div className={`mt-1.5 text-xs font-semibold ${reached ? 'text-gray-900' : 'text-gray-400'}`}>{step.name}</div>
      <div className="text-[11px] text-gray-500 leading-tight">{step.blurb}</div>
      {isFloor && <div className="mt-1"><Pill tone="gray">Floor</Pill></div>}
      {count !== undefined && (
        <div className={`mt-1 text-[11px] font-medium ${count > 0 ? 'text-green-700' : 'text-gray-400'}`}>
          {plural(count, 'signature')}
        </div>
      )}
    </li>
  );
}

function GuardrailRow({ icon: Icon, label, status, tone, detail }: { icon: IconType; label: string; status: string; tone: Tone; detail?: string }) {
  return (
    <li className="py-2.5 flex items-start gap-3">
      <Icon className="w-4 h-4 text-gray-400 shrink-0 mt-0.5" />
      <div className="flex-1 min-w-0">
        <div className="flex items-center justify-between gap-2">
          <span className="text-sm text-gray-800">{label}</span>
          <Pill tone={tone}>{status}</Pill>
        </div>
        {detail && <p className="text-xs text-gray-500 mt-0.5">{detail}</p>}
      </div>
    </li>
  );
}

const LIFECYCLE: ReadonlyArray<{ label: string; who: 'Automatic' | 'Human' | 'Earned'; icon: IconType; explain: string }> = [
  { label: 'Detect', who: 'Automatic', icon: Eye, explain: 'Dead-lettered messages are picked up and classified.' },
  { label: 'Investigate', who: 'Automatic', icon: Search, explain: 'Anomalies and drift are spotted per namespace.' },
  { label: 'Correlate', who: 'Automatic', icon: Network, explain: 'Related failures are linked, even across clouds.' },
  { label: 'Propose', who: 'Automatic', icon: Lightbulb, explain: 'Findings become proposals in the Playbook Ledger.' },
  { label: 'Decide', who: 'Human', icon: Hand, explain: 'A person approves the replay or the proposal.' },
  { label: 'Recover', who: 'Earned', icon: RotateCcw, explain: 'Human-approved — or unattended where a signature earned L4/L5.' },
  { label: 'Verify', who: 'Automatic', icon: BadgeCheck, explain: 'ServiceHub watches the DLQ to see whether messages return.' },
  { label: 'Learn', who: 'Automatic', icon: TrendingUp, explain: 'Verified outcomes raise or lower trust, per signature.' },
];

const WHO_TONE: Record<'Automatic' | 'Human' | 'Earned', Tone> = { Automatic: 'blue', Human: 'amber', Earned: 'green' };

/**
 * `/autonomy` — the Autonomy Control Center: the front door of Autonomous ServiceHub. Answers,
 * top to bottom, what ServiceHub can do on its own right now, what still needs a person, why the
 * level is what it is, which guardrails protect execution, what the evidence says, and what to do
 * next.
 *
 * Deliberately a read-only view. Autonomy is earned per failure signature from verified outcomes
 * in the Recovery Evidence Ledger; there is no global on/off switch and no control anywhere that
 * sets a level — not for an Admin, and not for the optional AI reasoning companion, which can only
 * write proposals a human decides (ADR-0005). Every number is a live ledger/governance read or a
 * static provider capability fact; nothing is estimated.
 */
export default function AutonomyPage() {
  const { isDemoMode, cloudProvider } = useDemoContext();
  const prefix = navPrefixFor(isDemoMode, cloudProvider);
  const [days, setDays] = useState<'7' | '30'>('7');

  const { data: overview, isLoading, isError, refetch, isFetching } = useAutonomyDashboard();
  const { data: approvalQueue } = useApprovalQueue(undefined, APPROVAL_QUEUE_LIMIT);
  const { data: capabilities } = useProviderCapabilities();
  const { data: me } = useMe();
  const { data: namespaces } = useNamespaces();
  const outcomes = useOutcomeMetrics(Number(days));
  const { data: operations } = useRecoveryOperations(undefined, true, 10);
  const { data: recentEntries } = useRecoveryEntries({ limit: 200 });
  const proposals = usePlaybookEntries({ limit: PLAYBOOK_LIMIT });
  const isAdmin = me?.governanceRole === 'Admin';
  const grants = useGovernanceGrants(undefined, isAdmin);

  const playbookStats = useMemo(() => computePlaybookStats(proposals.data ?? []), [proposals.data]);
  const approvalQueueCount = approvalQueue?.length ?? 0;
  const approvalQueueLabel = atLeast(approvalQueueCount, APPROVAL_QUEUE_LIMIT);
  const proposalsCapped = (proposals.data?.length ?? 0) >= PLAYBOOK_LIMIT;
  const awaitingLabel = proposalsCapped ? `${playbookStats.awaiting}+` : String(playbookStats.awaiting);
  const circuitBreakerTrips = overview?.circuitBreakerTrips ?? [];
  const emergencyStop = overview?.emergencyStopActive ?? false;

  const l3 = levelCount(overview, 3);
  const l4 = levelCount(overview, 4);
  const l5 = levelCount(overview, 5);
  const highest = l5 > 0 ? 5 : l4 > 0 ? 4 : 3;
  const earnedGrants = (overview?.grants ?? []).filter(g => g.currentLevel >= 4);

  // Connected providers, and whether each can ever reach L4/L5. A provider missing from the
  // capability map is "not reported" — never silently treated as capable.
  const connectedByProvider = useMemo(() => {
    const counts: Record<CloudProviderType, number> = { azure: 0, aws: 0, gcp: 0 };
    for (const ns of namespaces ?? []) {
      const p = ns.cloudProvider?.toLowerCase() as CloudProviderType | undefined;
      if (p && p in counts) counts[p]++;
    }
    return counts;
  }, [namespaces]);
  const providerRows = PROVIDER_KEYS.map(([key, capKey]) => {
    const cap = capabilities?.[capKey];
    const canProve = cap?.canProveDlqAbsence;
    return { key, label: PROVIDER_LABELS[key], connected: connectedByProvider[key], canProve, notes: cap?.notes };
  });
  const cappedConnected = providerRows.filter(r => r.connected > 0 && r.canProve !== true);
  const capableConnected = providerRows.filter(r => r.connected > 0 && r.canProve === true);

  const whyReasons: string[] = [];
  if (emergencyStop) whyReasons.push('The emergency stop is active, so nothing runs unattended until an Admin clears it.');
  if (circuitBreakerTrips.length > 0) {
    whyReasons.push(`${plural(circuitBreakerTrips.length, 'Auto-Replay Rule')} tripped a circuit breaker and ${circuitBreakerTrips.length === 1 ? 'is' : 'are'} disabled.`);
  }
  if (cappedConnected.length > 0) {
    whyReasons.push(
      `${cappedConnected.map(r => r.label).join(' and ')} cannot prove a replayed message stayed out of the dead-letter queue, so ${
        cappedConnected.length === 1 ? 'it is' : 'they are'
      } permanently capped at Approve (L3). This is a provider fact, not a maturity gap.`,
    );
  }
  if (highest === 3) {
    whyReasons.push(
      capableConnected.length > 0
        ? 'No failure signature has met the evidence bar yet: Standing (L4) needs at least 10 verified outcomes at a 95%+ success rate; Unattended (L5) needs 30 at 99%+.'
        : 'Connect a provider that can prove DLQ absence (Azure today) for any signature to be able to earn L4/L5.',
    );
  } else {
    whyReasons.push(
      `${plural(l4 + l5, 'failure signature')} earned unattended trust from verified outcomes. Every other signature stays at Approve (L3). Two consecutive failures demote a signature immediately.`,
    );
  }

  const nextSteps: Array<{ label: string; detail: string; to: string; primary?: boolean }> = [];
  if (approvalQueueCount > 0) {
    nextSteps.push({ label: `Review ${approvalQueueLabel} escalated replay${approvalQueueCount === 1 ? '' : 's'}`, detail: 'Waiting in the Approval Queue for a human decision.', to: `${prefix}/approval-queue`, primary: true });
  }
  if (playbookStats.awaiting > 0) {
    nextSteps.push({ label: `Decide ${awaitingLabel} proposal${playbookStats.awaiting === 1 ? '' : 's'}`, detail: 'Findings awaiting a human decision in the Playbook Ledger.', to: `${prefix}/playbook?state=awaiting`, primary: nextSteps.length === 0 });
  }
  if (circuitBreakerTrips.length > 0) {
    nextSteps.push({ label: 'Inspect tripped rules', detail: 'A circuit breaker disabled an Auto-Replay Rule after repeated failures.', to: `${prefix}/rules` });
  }
  if (nextSteps.length === 0) {
    nextSteps.push({
      label: 'Review Auto-Replay Rules',
      detail: 'Rules opt a failure class into evaluation. Every approved recovery adds evidence that can earn trust.',
      to: `${prefix}/rules`,
    });
  }

  const activity = buildActivity(overview, operations ?? [], recentEntries ?? [], proposals.data ?? [], prefix);
  const outcomesValue = (v: number | undefined) => (outcomes.isError ? '—' : outcomes.data ? v ?? 0 : '…');

  const safetyStatus = emergencyStop
    ? { value: 'Emergency stop', tone: 'red' as Tone, hint: 'Unattended recovery halted' }
    : circuitBreakerTrips.length > 0
      ? { value: `${circuitBreakerTrips.length} tripped`, tone: 'amber' as Tone, hint: 'Circuit breakers disabled rules' }
      : { value: 'Guardrails on', tone: 'green' as Tone, hint: 'No stop, no tripped rules' };

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      <PageHeader
        icon={Gauge}
        tone="blue"
        title="Autonomy Control Center"
        subtitle="What ServiceHub can safely do on its own, why, and what still needs a person. Autonomy is earned from evidence — it can't be switched on."
        actions={
          <>
            <FilterSelect
              label="Outcome window"
              value={days}
              onChange={setDays}
              options={[{ value: '7', label: 'Last 7 days' }, { value: '30', label: 'Last 30 days' }]}
            />
            <button type="button" onClick={() => refetch()} disabled={isFetching || isDemoMode} className={buttonClass.secondary}>
              <RefreshCw className={`w-4 h-4 ${isFetching ? 'animate-spin' : ''}`} />
              Refresh
            </button>
          </>
        }
      >
        {isDemoMode && (
          <Callout tone="amber" className="mt-3">
            Demo Mode — there is no live ledger here, so evidence-backed numbers are honestly empty rather than fabricated. Provider
            constraints and guardrails are real product facts and still shown.
          </Callout>
        )}
        {emergencyStop && (
          <Callout tone="red" icon={ShieldAlert} title="Emergency stop is active" className="mt-3">
            No new unattended (Automation/System) recovery can start until an Admin clears it. Manual, human-started recovery is unaffected.
          </Callout>
        )}
      </PageHeader>

      <div className="flex-1 overflow-auto p-4 sm:p-6">
        {isLoading ? (
          <LoadingBlock label="Loading autonomy…" />
        ) : isError ? (
          <ErrorBlock title="Failed to load the autonomy overview" onRetry={() => refetch()} />
        ) : (
          <div className="space-y-5 @container max-w-7xl mx-auto">
            {/* At a glance */}
            <div className="grid grid-cols-2 @4xl:grid-cols-5 gap-3">
              <StatCard
                icon={CheckCircle2}
                tone="green"
                label="Messages recovered"
                value={outcomesValue(outcomes.data?.messagesRecovered)}
                hint={outcomes.isError ? "Couldn't load outcomes" : `Verified, last ${days} days`}
                to={`${prefix}/recovery`}
              />
              <StatCard
                icon={Bot}
                tone="violet"
                label="Recovered unattended"
                value={outcomesValue(outcomes.data?.autonomousRecoveries)}
                hint="Under earned L4/L5 trust"
                to={`${prefix}/recovery`}
              />
              <StatCard
                icon={Clock}
                tone="amber"
                label="Waiting for a human"
                value={approvalQueueCount >= APPROVAL_QUEUE_LIMIT || proposalsCapped ? `${approvalQueueCount + playbookStats.awaiting}+` : approvalQueueCount + playbookStats.awaiting}
                hint={`${approvalQueueLabel} replays · ${awaitingLabel} proposals`}
                to={approvalQueueCount > 0 ? `${prefix}/approval-queue` : `${prefix}/playbook?state=awaiting`}
              />
              <StatCard
                icon={ShieldX}
                tone="blue"
                label="Unsafe attempts refused"
                value={outcomesValue(outcomes.data?.gateRefusals)}
                hint="Blocked by the Eligibility Gate"
              />
              <StatCard icon={ShieldCheck} tone={safetyStatus.tone} label="Safety status" value={<span className="text-lg">{safetyStatus.value}</span>} hint={safetyStatus.hint} />
            </div>

            {/* Current autonomy + guardrails */}
            <div className="grid grid-cols-1 @5xl:grid-cols-3 gap-5">
              <Card title="Current autonomy" icon={Gauge} tone="blue" className="@5xl:col-span-2" bodyClassName="p-5">
                <div className="flex items-start gap-4 flex-wrap">
                  <div className="flex-1 min-w-[16rem]">
                    <div className="flex items-center gap-2 flex-wrap">
                      <span className="text-2xl font-bold text-gray-900">
                        {highest === 5 ? 'Unattended (L5)' : highest === 4 ? 'Standing (L4)' : 'Approve (L3)'}
                      </span>
                      {highest === 3 ? (
                        <Pill tone="amber" icon={Hand}>Human approval required</Pill>
                      ) : (
                        <Pill tone="green" icon={BadgeCheck}>Earned by {plural(highest === 5 ? l5 : l4, 'signature')}</Pill>
                      )}
                    </div>
                    <p className="text-sm text-gray-600 mt-1.5">
                      {highest === 3
                        ? 'ServiceHub detects, explains and proposes recovery on its own, but a person approves every replay.'
                        : 'Signatures that proved themselves replay without waiting for a person. Everything else still needs approval.'}
                    </p>
                  </div>
                  <Link to={`${prefix}/advanced-servicehub`} className="text-xs text-primary-700 hover:underline inline-flex items-center gap-1">
                    How autonomy is earned <ArrowRight className="w-3 h-3" />
                  </Link>
                </div>

                <ol className="mt-5 flex items-start gap-1 overflow-x-auto pb-1" aria-label="Autonomy levels">
                  {LADDER.map(step => (
                    <LadderStep
                      key={step.level}
                      step={step}
                      reached={step.level <= highest}
                      isFloor={step.level === 3}
                      count={step.level === 4 ? l4 : step.level === 5 ? l5 : undefined}
                    />
                  ))}
                </ol>

                <div className="mt-4 rounded-lg bg-gray-50 border border-gray-200 p-3.5">
                  <div className="text-xs font-semibold text-gray-800 flex items-center gap-1.5">
                    <Info className="w-3.5 h-3.5 text-primary-600" /> Why this level?
                  </div>
                  <ul className="mt-1.5 space-y-1 text-xs text-gray-600 list-disc pl-5">
                    {whyReasons.map(r => <li key={r}>{r}</li>)}
                    {l3 > 0 && <li>{plural(l3, 'signature')} earned trust before and {l3 === 1 ? 'was' : 'were'} demoted back to Approve (L3).</li>}
                  </ul>
                </div>

                {earnedGrants.length > 0 && (
                  <div className="mt-4">
                    <div className="text-xs font-semibold text-gray-500 mb-1.5">Signatures with earned trust</div>
                    <ul className="divide-y divide-gray-100 border border-gray-200 rounded-lg">
                      {earnedGrants.slice(0, 5).map(g => (
                        <li key={`${g.signatureHash}-${g.actionKind}`} className="px-3 py-2 flex items-center justify-between gap-3 text-xs">
                          <Link to={`${prefix}/signatures/${g.signatureHash}`} className="font-mono text-gray-700 hover:text-primary-700 hover:underline" title={g.signatureHash}>
                            {truncateMiddle(g.signatureHash, 8)}
                          </Link>
                          <span className="text-gray-500">{g.actionKind}</span>
                          <Pill tone={g.currentLevel === 5 ? 'green' : 'blue'}>{g.levelLabel}</Pill>
                          <span className="text-gray-400 whitespace-nowrap">{formatDateTime(g.updatedAtUtc)}</span>
                        </li>
                      ))}
                    </ul>
                  </div>
                )}

                <p className="mt-4 text-xs text-gray-500 flex items-start gap-1.5">
                  <Lock className="w-3.5 h-3.5 shrink-0 mt-0.5" />
                  There is no global autonomy switch. Trust is earned per failure signature from verified outcomes, and no person, role or AI
                  can set it.
                </p>
              </Card>

              <Card title="Safety & guardrails" icon={ShieldCheck} tone="green" bodyClassName="px-4 py-1">
                <ul className="divide-y divide-gray-100">
                  <GuardrailRow
                    icon={ShieldAlert}
                    label="Emergency stop"
                    status={emergencyStop ? 'Active' : 'Not active'}
                    tone={emergencyStop ? 'red' : 'green'}
                    detail={emergencyStop ? 'Unattended recovery is halted.' : undefined}
                  />
                  <GuardrailRow
                    icon={Zap}
                    label="Circuit breakers"
                    status={circuitBreakerTrips.length > 0 ? `${circuitBreakerTrips.length} tripped` : 'None tripped'}
                    tone={circuitBreakerTrips.length > 0 ? 'amber' : 'green'}
                    detail={circuitBreakerTrips.length > 0 ? circuitBreakerTrips.map(t => t.ruleName).join(', ') : undefined}
                  />
                  <GuardrailRow icon={ShieldCheck} label="Eligibility Gate" status="Every recovery" tone="blue" detail="Checked on every replay and purge, human or automated." />
                  <GuardrailRow icon={Lock} label="Production floor" status="Enforced" tone="blue" detail="Recovery on a Production namespace needs a time-boxed, approved elevation." />
                  <GuardrailRow icon={CircleSlash} label="Purge" status="Never automated" tone="blue" detail="Only a person can purge." />
                  <GuardrailRow
                    icon={Sparkles}
                    label="AI companion"
                    status="Advisory only"
                    tone="violet"
                    detail={
                      playbookStats.aiAuthored > 0
                        ? `${plural(playbookStats.aiAuthored, 'proposal')} recorded. It can propose — never execute, approve or promote.`
                        : 'Not enabled — no AI proposals recorded. It can never execute, approve or promote.'
                    }
                  />
                </ul>
              </Card>
            </div>

            {/* Lifecycle */}
            <Card title="How a failure moves through ServiceHub" icon={RotateCcw} tone="indigo">
              <ol className="grid grid-cols-2 @xl:grid-cols-4 @5xl:grid-cols-8 gap-3">
                {LIFECYCLE.map((step, i) => (
                  <li key={step.label} className="relative rounded-lg border border-gray-200 p-3 bg-gray-50/60">
                    <div className="flex items-center gap-2">
                      <IconTile icon={step.icon} tone={WHO_TONE[step.who]} size="sm" />
                      <span className="text-xs text-gray-400 font-medium">{i + 1}</span>
                    </div>
                    <div className="mt-2 text-sm font-semibold text-gray-900">{step.label}</div>
                    <div className="mt-0.5"><Pill tone={WHO_TONE[step.who]}>{step.who === 'Earned' ? 'Human or earned' : step.who}</Pill></div>
                    <p className="mt-1.5 text-[11px] text-gray-500 leading-snug">{step.explain}</p>
                  </li>
                ))}
              </ol>
            </Card>

            {/* Automatic vs human + provider boundaries */}
            <div className="grid grid-cols-1 @5xl:grid-cols-2 gap-5">
              <Card title="Automatic vs. waits for you" icon={Hand} tone="amber" bodyClassName="p-0">
                <div className="grid grid-cols-1 @xl:grid-cols-2 divide-y sm:divide-y-0 sm:divide-x divide-gray-100">
                  <div className="p-4">
                    <div className="text-xs font-semibold text-blue-700 flex items-center gap-1.5 mb-2"><Bot className="w-3.5 h-3.5" /> ServiceHub does automatically</div>
                    <ul className="space-y-1.5 text-xs text-gray-700">
                      <li>Detect and classify dead-lettered messages</li>
                      <li>Spot anomalies, drift and related failures</li>
                      <li>Write proposals to the Playbook Ledger</li>
                      <li>Verify every recovery and update trust</li>
                      <li>
                        Replay signatures that earned L4/L5 —{' '}
                        <span className="font-medium">{l4 + l5 > 0 ? plural(l4 + l5, 'signature') + ' today' : 'none today'}</span>
                      </li>
                    </ul>
                  </div>
                  <div className="p-4">
                    <div className="text-xs font-semibold text-amber-700 flex items-center gap-1.5 mb-2"><User className="w-3.5 h-3.5" /> Always waits for a person</div>
                    <ul className="space-y-1.5 text-xs text-gray-700">
                      <li>
                        Any replay below L4 —{' '}
                        <Link to={`${prefix}/approval-queue`} className="text-primary-700 hover:underline">{approvalQueueLabel} waiting</Link>
                      </li>
                      <li>
                        Deciding proposals —{' '}
                        <Link to={`${prefix}/playbook?state=awaiting`} className="text-primary-700 hover:underline">{awaitingLabel} waiting</Link>
                      </li>
                      <li>Every purge</li>
                      <li>Any change on a Production namespace</li>
                      <li>Clearing an emergency stop</li>
                    </ul>
                  </div>
                </div>
              </Card>

              <Card
                title="Provider boundaries"
                icon={Cloud}
                tone="blue"
                bodyClassName="p-0"
                action={<Link to={`${prefix}/cloud-bridge`} className="text-xs text-primary-700 hover:underline">Capabilities →</Link>}
              >
                <div className="overflow-x-auto">
                  <table className="w-full text-sm" aria-label="Provider autonomy boundaries">
                    <thead className="bg-gray-50 text-xs text-gray-500">
                      <tr>
                        <th scope="col" className="px-4 py-2 text-left font-semibold">Provider</th>
                        <th scope="col" className="px-4 py-2 text-left font-semibold">Connected</th>
                        <th scope="col" className="px-4 py-2 text-left font-semibold">Can prove DLQ absence</th>
                        <th scope="col" className="px-4 py-2 text-left font-semibold">Highest possible level</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                      {providerRows.map(row => (
                        <tr key={row.key} title={row.notes}>
                          <td className="px-4 py-2.5 font-medium text-gray-800">{row.label}</td>
                          <td className="px-4 py-2.5 text-xs text-gray-600">
                            {isDemoMode ? '—' : row.connected > 0 ? plural(row.connected, 'namespace') : 'Not connected'}
                          </td>
                          <td className="px-4 py-2.5">
                            {!capabilities ? (
                              <span className="text-xs text-gray-400">Loading…</span>
                            ) : row.canProve === undefined ? (
                              <Pill tone="gray">Not reported</Pill>
                            ) : (
                              <Pill tone={row.canProve ? 'green' : 'gray'}>{row.canProve ? 'Yes' : 'No'}</Pill>
                            )}
                          </td>
                          <td className="px-4 py-2.5 text-xs text-gray-700">
                            {row.canProve === true ? 'Unattended (L5), once earned' : 'Approve (L3) — human approval always'}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
                <p className="px-4 py-2.5 text-[11px] text-gray-500 border-t border-gray-100">
                  L3 caps are provider facts, not a maturity gap: without proof a replayed message stayed out of the DLQ, ServiceHub cannot
                  verify an unattended outcome.
                </p>
              </Card>
            </div>

            {/* Pillars */}
            <div className="grid grid-cols-1 @3xl:grid-cols-3 gap-5">
              <PillarSummary
                icon={ShieldCheck}
                tone="teal"
                title="Recovery Evidence"
                question="What happened — and can we prove it?"
                metrics={[
                  { label: `recovered (${days}d)`, value: outcomesValue(outcomes.data?.messagesRecovered) },
                  { label: `written off (${days}d)`, value: outcomesValue(outcomes.data?.messagesAbandoned) },
                ]}
                cta="View evidence"
                to={`${prefix}/recovery`}
              />
              <PillarSummary
                icon={ClipboardList}
                tone="indigo"
                title="Playbook Ledger"
                question="What has ServiceHub learned or proposed?"
                metrics={
                  proposals.isError
                    ? [{ label: "couldn't load", value: '—' }]
                    : [
                        { label: 'proposals', value: playbookStats.total >= 500 ? '500+' : playbookStats.total },
                        { label: 'awaiting a decision', value: awaitingLabel },
                      ]
                }
                cta="View playbook"
                to={`${prefix}/playbook`}
              />
              <PillarSummary
                icon={Users}
                tone="red"
                title="Governance"
                question="Who may approve and manage decisions?"
                metrics={[
                  { label: 'your fleet-wide role', value: <span className="text-base">{me?.governanceRole ?? 'None'}</span> },
                  { label: 'active grants', value: isAdmin ? grants.data?.length ?? '…' : <span className="text-base text-gray-400" title="Only a Governance Admin can list grants">Admin only</span> },
                ]}
                cta={isAdmin ? 'Manage governance' : 'View governance'}
                to={`${prefix}/governance`}
              />
            </div>

            {/* Activity + next steps */}
            <div className="grid grid-cols-1 @5xl:grid-cols-3 gap-5">
              <Card
                title="Recent autonomy activity"
                icon={Clock}
                tone="gray"
                className="@5xl:col-span-2"
                bodyClassName="p-0"
                action={<Link to={`${prefix}/audit`} className="text-xs text-primary-700 hover:underline">Full record: Audit Trail →</Link>}
              >
                {activity.length === 0 ? (
                  <p className="px-4 py-8 text-center text-sm text-gray-500">
                    No recoveries, trust changes or proposal decisions recorded yet.
                  </p>
                ) : (
                  <ul className="divide-y divide-gray-100">
                    {activity.map(item => (
                      <li key={item.key}>
                        <Link to={item.href} className="px-4 py-2.5 flex items-center gap-3 hover:bg-gray-50">
                          <IconTile icon={item.icon} tone={item.tone} size="sm" />
                          <div className="flex-1 min-w-0">
                            <div className="text-sm text-gray-800 truncate">{item.title}</div>
                            <div className="text-xs text-gray-500 truncate">{item.detail}</div>
                          </div>
                          <span className="text-xs text-gray-400 whitespace-nowrap">{formatDateTime(item.at)}</span>
                        </Link>
                      </li>
                    ))}
                  </ul>
                )}
              </Card>

              <Card title="Next steps" icon={Lightbulb} tone="amber">
                <ul className="space-y-3">
                  {nextSteps.map(step => (
                    <li key={step.label}>
                      <Link to={step.to} className={step.primary ? `${buttonClass.primary} w-full` : `${buttonClass.secondary} w-full`}>
                        {step.label}
                      </Link>
                      <p className="text-xs text-gray-500 mt-1">{step.detail}</p>
                    </li>
                  ))}
                </ul>
              </Card>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

function PillarSummary({
  icon, tone, title, question, metrics, cta, to,
}: {
  icon: IconType;
  tone: Tone;
  title: string;
  question: string;
  metrics: Array<{ label: string; value: ReactNode }>;
  cta: string;
  to: string;
}) {
  return (
    <section className="bg-white border border-gray-200 rounded-xl shadow-sm p-4 flex flex-col">
      <div className="flex items-start gap-3">
        <IconTile icon={icon} tone={tone} />
        <div>
          <h2 className="text-sm font-semibold text-gray-900">{title}</h2>
          <p className="text-xs text-gray-500">{question}</p>
        </div>
      </div>
      <dl className="mt-4 grid grid-cols-2 gap-3 flex-1">
        {metrics.map(m => (
          <div key={m.label}>
            <dd className="text-2xl font-bold text-gray-900 leading-tight">{m.value}</dd>
            <dt className="text-xs text-gray-500">{m.label}</dt>
          </div>
        ))}
      </dl>
      <Link to={to} className={`${buttonClass.secondary} mt-4 w-full`}>
        {cta} <ArrowRight className="w-3.5 h-3.5" />
      </Link>
    </section>
  );
}
