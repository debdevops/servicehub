import { lazy, Suspense } from 'react'
import { useQuery } from '@tanstack/react-query'
import { CheckCircle2, Inbox, Database, RotateCcw, Zap, TriangleAlert, List } from 'lucide-react'
import { NamespaceScope } from '../provider/NamespaceScope'
import { scopeQuery, type ScopeChoice } from '../provider/scopeChoice'
import { WhyMessagesFailed } from '../insights/WhyMessagesFailed'
import { RecoveryOutcomes } from '../insights/RecoveryOutcomes'
import { RecentDeadLetters } from '../message/RecentDeadLetters'
import { RecentActivity } from '../RecentActivity'
import { StatTile } from '../ui/StatTile'
import { CapabilityLine } from './CapabilityLine'
import { LookNowBar } from './LookNowBar'
import { QueuesCard } from './QueuesCard'
import { SubscriptionsCard } from './SubscriptionsCard'
import { TwoColumnStack } from './TwoColumnStack'
import { columnHelp } from '../../content/columns'
import { useProviderSummary } from '../../hooks/useProviderSummary'
import { useRecoverySummary } from '../../hooks/useRecoverySummary'
import { useDeadLetters } from '../../hooks/useDeadLetters'
import { fetchRules } from '../../lib/api/rules'
import { fetchFleet } from '../../lib/api/fleet'
import type { CloudProvider, Namespace } from '../../lib/api/namespaces'
import { providerLabel, providerService, scopeChips } from '../../lib/providers'
import { traitsOf } from '../../lib/home/traits'
import { connectionState } from '../../lib/home/connection'
import type { HomeWindow } from '../../lib/home/scope'
import type { CloudSummary } from '../../lib/homeSummary'
import { Skeleton } from '../ui/Skeleton'
import { heldWords } from '../../lib/heldWords'

const TrendChart = lazy(() => import('../TrendChart'))

/**
 * One cloud's detail (plan §5.3). Which cards show up is decided by `traitsOf` — never by `provider`
 * directly — so this file reads the same for Azure, AWS and Google Cloud; only the words differ.
 */
export function CloudView({ provider, allInCloud, choice, window }: {
  provider: CloudProvider
  allInCloud: readonly Namespace[]
  choice: ScopeChoice
  window: HomeWindow
}) {
  const cloud = providerLabel[provider]
  const namespaces = choice.namespaces
  const nsQuery = scopeQuery(choice)
  const summary = useProviderSummary(namespaces)
  const fleet = useQuery({ queryKey: ['fleet', window], queryFn: () => fetchFleet(window) })
  const fleetCloud = fleet.data?.clouds.find((c) => c.provider === provider)
  const traits = traitsOf(namespaces, fleetCloud)
  const recovery = useRecoverySummary({ window, provider, namespaceId: choice.ns?.id, environment: choice.env ?? undefined })
  const rules = useQuery({ queryKey: ['rules', provider], queryFn: () => fetchRules(provider) })
  const connection = connectionState(allInCloud)
  const chips = scopeChips(provider, allInCloud)

  return (
    <div className="space-y-3.5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="flex items-center gap-2 text-[17px] font-extrabold tracking-tight text-[var(--color-text)]">{cloud}</h2>
          <p className="text-[12.5px] text-[var(--color-text-muted)]">How your {providerService[provider]} is holding up.</p>
          {allInCloud.length > 1 && <div className="mt-1.5"><NamespaceScope namespaces={allInCloud} cloud={cloud} compact /></div>}
        </div>
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <span className={`rounded-full px-[13px] py-1.5 text-xs font-semibold ${connection.ok === false ? 'bg-[var(--color-warning-light)] text-[#92400e]' : connection.ok ? 'bg-[var(--color-success-light)] text-[#047857]' : 'bg-[var(--color-surface-muted)]'}`}>
            {connection.label}
          </span>
          {chips.map((c) => (
            <span key={c.label} className="rounded-[10px] border border-[var(--color-border)] bg-[var(--color-surface)] px-[13px] py-1.5 shadow-[var(--shadow-card)]">
              <span className="text-[var(--color-text-muted)]">{c.label}</span> <b className="font-medium">{c.value}</b>
            </span>
          ))}
          <a href={`/?provider=${provider}&tab=dlq${nsQuery}`} className="text-[12.5px] font-semibold text-[var(--color-primary-700)] hover:underline">See all dead letters in {cloud} →</a>
        </div>
      </div>
      <CapabilityLine traits={traits} full />

      {!traits.watched && <LookNowBar provider={provider} namespaceIds={namespaces.map((n) => n.id)} namespaceId={choice.ns?.id} environment={choice.env ?? undefined} />}

      {summary.status === 'loading' && <p role="status" className="text-sm text-[var(--color-text-muted)]">Reading {cloud}…</p>}
      {summary.status === 'error' && (
        <div role="alert" className="rounded-xl border border-[var(--color-warning)] bg-[var(--color-warning-light)] p-4 text-sm">
          <p className="flex items-center gap-2 font-semibold"><TriangleAlert className="h-4 w-4" aria-hidden="true" /> ServiceHub couldn’t read {cloud} just now.</p>
          <button type="button" onClick={summary.retry} className="mt-2 font-medium text-[var(--color-primary-700)] hover:underline">Try again</button>
        </div>
      )}

      {summary.status === 'ready' && (
        <>
          <Verdict cloud={cloud} summary={summary.summary} />

          <div className="grid items-stretch gap-3.5 sm:grid-cols-2 xl:grid-cols-4">
            {traits.listsSubscriptionsOnly ? (
              <RecordedDeadLettersTile provider={provider} namespaceId={choice.ns?.id} environment={choice.env ?? undefined} nsQuery={nsQuery} />
            ) : (
              <StatTile
                label="Dead letters"
                value={summary.summary.deadLetters}
                note={traits.watched ? 'right now' : 'recorded'}
                unavailable={`${cloud} does not report message counts.`}
                to={`/?provider=${provider}&tab=dlq${nsQuery}`}
                action="See dead letters"
                tone="red"
                info={columnHelp.tiles.deadLetters}
                icon={Inbox}
              />
            )}
            {traits.listsSubscriptionsOnly ? (
              <EntityCountTile provider={provider} summary={summary.summary} nsQuery={nsQuery} />
            ) : (
              <StatTile
                label="Active messages"
                value={summary.summary.active}
                note={traits.watched ? 'browse them safely' : 'counted only — no browsing'}
                unavailable={`${cloud} does not report message counts.`}
                to={`/?provider=${provider}&tab=active${nsQuery}`}
                action="See active messages"
                tone="blue"
                info={columnHelp.tiles.active}
                icon={Database}
              />
            )}
            <ReplayedTile provider={provider} recovery={recovery.data} traits={traits} cloud={cloud} nsQuery={nsQuery} />
            <RulesTile rules={rules.data} traits={traits} />
          </div>

          {allInCloud.length > 1 && namespaces.length > 1 && (
            <NamespaceScope namespaces={allInCloud} cloud={cloud} />
          )}

          {traits.watched ? (
            <>
              <div className="grid items-start gap-3.5 xl:grid-cols-12">
                <div className="xl:col-span-5">
                  <Suspense fallback={<Skeleton label="Reading the trend…" variant="block" />}>
                    <TrendChart provider={provider} namespaceId={choice.ns?.id} environment={choice.env ?? undefined} />
                  </Suspense>
                </div>
                <div className="xl:col-span-4"><WhyMessagesFailed provider={provider} namespaceId={choice.ns?.id} environment={choice.env ?? undefined} scopeQuery={nsQuery} /></div>
                <div className="xl:col-span-3"><RecoveryOutcomes provider={provider} namespaceId={choice.ns?.id} environment={choice.env ?? undefined} window={window === '7d' ? '7d' : '24h'} /></div>
              </div>
              <div className="grid items-start gap-3.5 md:grid-cols-2">
                <QueuesCard summary={summary.summary} namespaces={namespaces} />
                <RecentDeadLetters provider={provider} namespaces={namespaces} choice={choice} />
              </div>
            </>
          ) : (
            <TwoColumnStack
              left={[
                <WhyMessagesFailed key="why" provider={provider} namespaceId={choice.ns?.id} environment={choice.env ?? undefined} scopeQuery={nsQuery} />,
                traits.listsSubscriptionsOnly ? <SubscriptionsCard key="subs" namespaces={namespaces} /> : <QueuesCard key="queues" summary={summary.summary} namespaces={namespaces} />,
              ]}
              right={[
                <RecoveryOutcomes key="rec" provider={provider} namespaceId={choice.ns?.id} environment={choice.env ?? undefined} window={window === '7d' ? '7d' : '24h'} />,
                <RecentDeadLetters key="latest" provider={provider} namespaces={namespaces} choice={choice} recorded />,
              ]}
            />
          )}

          <RecentActivity provider={provider} namespaceId={choice.ns?.id} environment={choice.env ?? undefined} />
        </>
      )}
    </div>
  )
}

/** Tile 1 for a cloud that can't count messages at all (plan §5.3, GCP-shaped): what Pub/Sub gives is
 *  no number, so the honest tile is what ServiceHub itself has recorded, not a live count. */
function RecordedDeadLettersTile({ provider, namespaceId, environment, nsQuery }: {
  provider: CloudProvider
  namespaceId?: string
  environment?: import('../../lib/api/namespaces').EnvironmentKind
  nsQuery: string
}) {
  const recorded = useDeadLetters({ provider, namespaceId, environment, status: 'active', page: 1, pageSize: 1 })
  const newest = recorded.data?.items[0]?.detectedAtUtc
  return (
    <StatTile
      label="Recorded dead letters"
      value={recorded.data?.paging.total ?? null}
      note={newest ? `newest ${new Date(newest).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}` : ''}
      unavailable="can’t count them — what ServiceHub recorded at your last look"
      to={`/?provider=${provider}&tab=dlq${nsQuery}`}
      action="See dead letters"
      tone="red"
      icon={Inbox}
    />
  )
}

function EntityCountTile({ provider, summary, nsQuery }: { provider: CloudProvider; summary: CloudSummary; nsQuery: string }) {
  const topics = summary.entityCounts.find((e) => e.kind === 'topic')?.count ?? 0
  const subs = summary.entityCounts.find((e) => e.kind === 'subscription')?.count ?? 0
  return (
    <StatTile
      label="Topics · subscriptions"
      value={topics + subs}
      note="no message counts here"
      unavailable="No topics or subscriptions were found yet."
      to={`/?provider=${provider}&tab=active${nsQuery}`}
      action="See topics"
      tone="blue"
      icon={List}
    />
  )
}

function ReplayedTile({ provider, recovery, traits, cloud, nsQuery }: {
  provider: CloudProvider
  recovery: import('../../lib/api/recovery').RecoverySummary | undefined
  traits: ReturnType<typeof traitsOf>
  cloud: string
  nsQuery: string
}) {
  const count = (name: string) => recovery?.states.find((s) => s.state === name)?.count ?? 0
  const note = !recovery
    ? ''
    : traits.confirms
      ? `${count('Recovered')} verified · ${count('Observing')} being watched`
      : recovery.total > 0
        ? `sent back — ${cloud} can’t confirm a fix held yet`
        : 'nothing replayed yet'
  return (
    <StatTile
      label="Replayed"
      value={recovery?.total ?? null}
      note={note}
      unavailable="ServiceHub hasn’t read its replay history yet."
      to={`/?provider=${provider}&tab=replayed${nsQuery}`}
      action="See what was replayed"
      tone="green"
      icon={RotateCcw}
    />
  )
}

function RulesTile({ rules, traits }: { rules: import('../../lib/api/rules').Rule[] | undefined; traits: ReturnType<typeof traitsOf> }) {
  if (!rules) return <StatTile label="Auto Replay rules" value={null} note="" unavailable="Reading the rules…" to="?panel=rules" action="Manage rules" tone="amber" icon={Zap} />
  const enabled = rules.filter((r) => r.enabled).length
  const stopped = rules.filter((r) => !r.enabled && r.disabledReason === 'CircuitBreaker').length
  const asked = rules.reduce((n, r) => n + r.askedCount, 0)
  const askedWords = heldWords(rules)
  const note = traits.confirms
    ? stopped > 0 ? `${stopped} stopped by its safety check` : `${rules.length - enabled} paused`
    : asked > 0 ? `${askedWords} matches held for a person` : 'replays here wait for a person'
  return (
    <StatTile
      label="Auto Replay rules"
      value={enabled}
      note={note}
      unavailable="No rules yet."
      to="?panel=rules"
      action="Manage rules"
      tone="amber"
      icon={Zap}
    />
  )
}

/** The plain answer to "is anything wrong?" — good news is a sentence, not a blank panel. */
function Verdict({ cloud, summary }: { cloud: string; summary: CloudSummary }) {
  if (summary.deadLetters === null) {
    return (
      <p className="text-sm text-[var(--color-text-muted)]">
        {cloud} does not report message counts, so ServiceHub can’t say how many dead letters there are.
      </p>
    )
  }
  if (summary.deadLetters > 0) {
    return (
      <p className="text-sm text-[var(--color-text)]">
        <b>{summary.deadLetters.toLocaleString()}</b> {summary.deadLetters === 1 ? 'message is' : 'messages are'} dead-lettered in {cloud}.
      </p>
    )
  }
  const seen = summary.entityCounts.map((e) => `${e.count.toLocaleString()} ${e.count === 1 ? e.kind : `${e.kind}s`}`)
  return (
    <p className="flex items-center gap-2 rounded-xl bg-[var(--color-success-light)] px-4 py-3 text-sm text-[var(--color-text)]">
      <CheckCircle2 className="h-4 w-4 shrink-0 text-[var(--color-success)]" aria-hidden="true" />
      <span>No dead letters in {cloud}.{seen.length > 0 && ` ServiceHub can see ${seen.join(' and ')}.`}</span>
    </p>
  )
}
