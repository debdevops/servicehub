import { lazy, Suspense } from 'react'
import { CheckCircle2, Inbox, Database, TriangleAlert } from 'lucide-react'
import { toProblem } from '../lib/api/client'
import { Link, useSearchParams } from 'react-router-dom'
import { AgentBar } from '../components/agent/AgentBar'
import { NeedsYouStrip } from '../components/pending/NeedsYouStrip'
import { NamespaceScope } from '../components/provider/NamespaceScope'
import { resolveScope, scopeQuery, type ScopeChoice } from '../components/provider/scopeChoice'
import { QueueDepth } from '../components/insights/QueueDepth'
import { RecoveryOutcomes } from '../components/insights/RecoveryOutcomes'
import { WhyMessagesFailed } from '../components/insights/WhyMessagesFailed'
import { Welcome } from '../components/connect/Welcome'
import { FleetCard } from '../components/FleetCard'
import { MessageDrawer } from '../components/message/MessageDrawer'
import { DeadLettersView } from '../components/message/DeadLettersView'
import { ActiveMessagesTab } from '../components/message/ActiveMessagesTab'
import { ReplayedTab } from '../components/message/ReplayedTab'
import { QueuesNeedingAttention } from '../components/QueuesNeedingAttention'
import { RecentActivity } from '../components/RecentActivity'
import { RecentDeadLetters } from '../components/message/RecentDeadLetters'
import { useProviderScope } from '../components/provider/providerScope'
import { columnHelp } from '../content/columns'
import { StatTile } from '../components/ui/StatTile'
import { useNamespaces } from '../hooks/useNamespaces'
import { useProviderSummary } from '../hooks/useProviderSummary'
import type { CloudProvider, Namespace } from '../lib/api/namespaces'
import type { CloudSummary } from '../lib/homeSummary'
import { connectedProviders, providerLabel, providerService, scopeChips } from '../lib/providers'

// Recharts is heavy and only Home's overview draws it, so it is its own chunk (rule: bundle budget).
const TrendChart = lazy(() => import('../components/TrendChart'))

/**
 * Home. With nothing connected it is the welcome (D45 — there is no Connect page). Otherwise it is
 * ONE cloud's home: the cloud chosen in the sidebar, with numbers from that cloud's namespaces only.
 *
 * First pass: the tiles that have a source today (dead letters, active). Replayed-today and
 * Auto Replay have no source until Waves 2–3, so they are not drawn — a tile with nothing behind
 * it is not shown as 0 (R5). "Needs you" and the Agent bar arrive whole in Waves 4–5.
 */
export function HomePage() {
  const namespaces = useNamespaces()
  const { selected } = useProviderScope()
  const [params] = useSearchParams()

  if (namespaces.isError) {
    // The sidebar says it too, but a blank page reads as broken: say it where the person is looking.
    return (
      <section role="alert" className="mx-auto max-w-xl px-6 py-16 text-center">
        <TriangleAlert className="mx-auto mb-3 h-8 w-8 text-[var(--color-warning)]" aria-hidden="true" />
        <h1 className="text-xl font-semibold text-[var(--color-text)]">ServiceHub couldn’t load your clouds</h1>
        <p className="mt-2 text-sm text-[var(--color-text-muted)]">{toProblem(namespaces.error).message}</p>
        <button type="button" onClick={() => void namespaces.refetch()} className="mt-5 rounded-lg bg-[var(--color-primary-600)] px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--color-primary-700)]">
          Try again
        </button>
      </section>
    )
  }
  if (!namespaces.isSuccess) return null
  if (namespaces.data.length === 0) return <Welcome />
  if (selected === null) return null

  const inCloud = namespaces.data.filter((n) => n.provider === selected)
  // `?ns=` / `?env=` narrow Home to one namespace or one environment of this cloud; anything stale is ignored.
  const choice = resolveScope(inCloud, params)
  const mine = choice.namespaces
  const cloudCount = connectedProviders(namespaces.data).length
  return (
    <>
      <DrawerAside>
        <CloudHome provider={selected} namespaces={mine} allInCloud={inCloud} choice={choice} otherCloudsConnected={cloudCount > 1} />
      </DrawerAside>
      <MessageDrawer />
    </>
  )
}

/** Makes room for the message drawer beside the page: it docks at the right, so the page must not sit under it. */
function DrawerAside({ children }: { children: React.ReactNode }) {
  const [params] = useSearchParams()
  const docked = params.get('message') !== null && params.get('view') !== 'full'
  return <div className={docked ? 'xl:pr-[462px]' : undefined}>{children}</div>
}

function CloudHome({
  provider,
  namespaces,
  allInCloud,
  choice,
  otherCloudsConnected,
}: {
  provider: CloudProvider
  namespaces: readonly Namespace[]
  allInCloud: readonly Namespace[]
  choice: ScopeChoice
  otherCloudsConnected: boolean
}) {
  const cloud = providerLabel[provider]
  const [params] = useSearchParams()
  const tab = params.get('tab')
  const summary = useProviderSummary(namespaces)
  const connection = connectionState(namespaces)
  const chips = scopeChips(provider, namespaces)
  const nsQuery = scopeQuery(choice)

  // The work views of Home's table (D45): `?tab=dlq` is the dead letters, in place of the overview.
  const picker = allInCloud.length > 1 ? <div className="px-[22px] pt-4"><NamespaceScope namespaces={allInCloud} cloud={cloud} /></div> : null
  if (tab === 'dlq') return <>{picker}<DeadLettersView provider={provider} namespaces={namespaces} /></>
  if (tab === 'replayed') return <>{picker}<ReplayedTab provider={provider} choice={choice} /></>
  if (tab === 'active') return <>{picker}<ActiveMessagesTab provider={provider} namespaces={namespaces} /></>

  return (
    <section className="px-[22px] pb-6 pt-5">
      <header className="mb-[18px] flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-extrabold tracking-tight text-[var(--color-text)]">{cloud} — Home</h1>
          {allInCloud.length > 1 && <NamespaceScope namespaces={allInCloud} cloud={cloud} />}
          <p className="mt-[3px] text-[13px] text-[var(--color-text-muted)]">How your {providerService[provider]} is holding up.</p>
        </div>
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <span
            className={`rounded-full px-[13px] py-1.5 text-xs font-semibold ${connection.ok === false ? 'bg-[var(--color-warning-light)] text-[#92400e]' : connection.ok ? 'bg-[var(--color-success-light)] text-[#047857]' : 'bg-[var(--color-surface-muted)]'}`}
          >
            {connection.label}
          </span>
          {chips.map((c) => (
            <span key={c.label} className="rounded-[10px] border border-[var(--color-border)] bg-[var(--color-surface)] px-[13px] py-1.5 shadow-[var(--shadow-card)]">
              <span className="text-[var(--color-text-muted)]">{c.label}</span> <b className="font-medium">{c.value}</b>
            </span>
          ))}
        </div>
      </header>

      <div className="mb-3.5">
        <NeedsYouStrip
          provider={provider}
          namespaceId={choice.ns?.id}
          environment={choice.env ?? undefined}
          watching={
            namespaces.every((n) => n.capabilities?.supportsRepeatablePeek === true)
              ? summary.status === 'ready' ? `The Agent is watching ${summary.summary.entityCounts.filter((e) => e.kind !== 'topic').reduce((n, e) => n + e.count, 0)} queues.` : 'The Agent is watching.'
              : `ServiceHub doesn’t watch ${cloud} on its own — look at its dead letters from the Dead letters tab.`
          }
        />
      </div>
      <div className="mb-5">
        <AgentBar
          cloud={cloud}
          provider={provider}
          namespaces={namespaces}
          queues={summary.status === 'ready' ? summary.summary.entityCounts.filter((e) => e.kind !== 'topic').reduce((n, e) => n + e.count, 0) : null}
          namespaceId={choice.ns?.id}
          environment={choice.env ?? undefined}
        />
      </div>

      {summary.status === 'loading' && <p role="status" className="text-sm text-[var(--color-text-muted)]">Reading {cloud}…</p>}

      {summary.status === 'error' && (
        <div role="alert" className="rounded-xl border border-[var(--color-warning)] bg-[var(--color-warning-light)] p-4 text-sm">
          <p className="flex items-center gap-2 font-semibold"><TriangleAlert className="h-4 w-4" aria-hidden="true" /> ServiceHub couldn’t read {cloud} just now.</p>
          <p className="mt-1">Check that the connection still works, then try again.</p>
          <button type="button" onClick={summary.retry} className="mt-2 font-medium text-[var(--color-primary-700)] hover:underline">
            Try again
          </button>
        </div>
      )}

      {summary.status === 'ready' && (
        <div className="space-y-5">
          <Verdict cloud={cloud} summary={summary.summary} />
          <div className="grid gap-3.5 sm:grid-cols-2 xl:grid-cols-4">
            <StatTile
              label="Dead letters"
              value={summary.summary.deadLetters}
              note="right now"
              unavailable={`${cloud} does not report message counts.`}
              to={`/?tab=dlq${nsQuery}`}
              action="See dead letters"
              tone="red"
              info={columnHelp.tiles.deadLetters}
              icon={Inbox}
            />
            <StatTile
              label="Active messages"
              value={summary.summary.active}
              note="right now"
              unavailable={`${cloud} does not report message counts.`}
              to={`/?tab=active${nsQuery}`}
              action="See active messages"
              tone="blue"
              info={columnHelp.tiles.active}
              icon={Database}
            />
          </div>
          <div className="grid items-start gap-3.5 xl:grid-cols-[1.58fr_1fr]">
            <div className="space-y-3.5">
              {namespaces.every((n) => n.capabilities?.supportsRepeatablePeek === true) ? (
                <Suspense fallback={<p role="status" className="text-sm text-[var(--color-text-muted)]">Reading the trend…</p>}>
                  <TrendChart provider={provider} namespaceId={choice.ns?.id} environment={choice.env ?? undefined} />
                </Suspense>
              ) : (
                // A trend of what ServiceHub has seen is silence, not good news, where it does not look on its own (R5).
                <p className="px-1 text-[12.5px] text-[var(--color-text-muted)]">
                  ServiceHub does not watch {cloud} for dead letters on its own, so there is no day-by-day trend.{' '}
                  <Link to={`/?tab=dlq${nsQuery}`} className="font-medium text-[var(--color-primary-700)] hover:underline">Look at its dead letters now</Link>
                </p>
              )}
              {/* The same two cards on every cloud, from data every cloud can supply. */}
              <div className="grid gap-3.5 md:grid-cols-2">
                <WhyMessagesFailed provider={provider} namespaceId={choice.ns?.id} environment={choice.env ?? undefined} scopeQuery={nsQuery} />
                <RecoveryOutcomes provider={provider} namespaceId={choice.ns?.id} environment={choice.env ?? undefined} />
              </div>
              <QueueDepth summary={summary.summary} namespaces={namespaces} cloud={cloud} />
            </div>
            <div className="space-y-3.5">
              <FleetCard cloud={cloud} summary={summary.summary} namespaces={namespaces} fleetHref={otherCloudsConnected ? '/fleet' : undefined} />
              <QueuesNeedingAttention summary={summary.summary} namespaces={namespaces} />
            </div>
          </div>
          <RecentDeadLetters provider={provider} namespaces={namespaces} choice={choice} />
          <RecentActivity provider={provider} namespaceId={choice.ns?.id} environment={choice.env ?? undefined} />
        </div>
      )}
    </section>
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
      <span>
        No dead letters in {cloud}.{seen.length > 0 && ` ServiceHub can see ${seen.join(' and ')}.`}
      </span>
    </p>
  )
}

/** The connection pill: only what the last real test said, never an assumed "Connected". */
function connectionState(namespaces: readonly Namespace[]): { label: string; ok: boolean | null } {
  if (namespaces.every((n) => n.lastConnectionTestSucceeded === true)) return { label: 'Connected', ok: true }
  if (namespaces.some((n) => n.lastConnectionTestSucceeded === false)) return { label: 'Could not connect at last check', ok: false }
  return { label: 'Not tested yet', ok: null }
}
