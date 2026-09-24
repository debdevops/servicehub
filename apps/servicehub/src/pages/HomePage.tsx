import { lazy, Suspense } from 'react'
import { CheckCircle2, TriangleAlert } from 'lucide-react'
import { useSearchParams } from 'react-router-dom'
import { Welcome } from '../components/connect/Welcome'
import { FleetCard } from '../components/FleetCard'
import { MessageDrawer } from '../components/message/MessageDrawer'
import { DeadLettersView } from '../components/message/DeadLettersView'
import { ReplayedTab } from '../components/message/ReplayedTab'
import { RecentActivity } from '../components/RecentActivity'
import { RecentDeadLetters } from '../components/message/RecentDeadLetters'
import { WorkTabs } from '../components/message/WorkTabs'
import { useProviderScope } from '../components/provider/providerScope'
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

  if (namespaces.isError) return null // the sidebar already says it, with a retry
  if (!namespaces.isSuccess) return null
  if (namespaces.data.length === 0) return <Welcome />
  if (selected === null) return null

  const mine = namespaces.data.filter((n) => n.provider === selected)
  const cloudCount = connectedProviders(namespaces.data).length
  return (
    <>
      <DrawerAside>
        <CloudHome provider={selected} namespaces={mine} otherCloudsConnected={cloudCount > 1} />
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
  otherCloudsConnected,
}: {
  provider: CloudProvider
  namespaces: readonly Namespace[]
  otherCloudsConnected: boolean
}) {
  const cloud = providerLabel[provider]
  const [params] = useSearchParams()
  const tab = params.get('tab')
  const summary = useProviderSummary(namespaces)
  const connection = connectionState(namespaces)
  const chips = scopeChips(provider, namespaces)

  // The work views of Home's table (D45): `?tab=dlq` is the dead letters, in place of the overview.
  if (tab === 'dlq') return <DeadLettersView provider={provider} namespaces={namespaces} />
  if (tab === 'replayed') return <ReplayedTab provider={provider} />
  if (tab === 'active') return <NotBuiltTab tab={tab} cloud={cloud} />

  return (
    <section className="px-6 py-6">
      <header className="mb-5 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold text-[var(--color-text)]">{cloud} — Home</h1>
          <p className="mt-0.5 text-sm text-[var(--color-text-muted)]">How your {providerService[provider]} is holding up.</p>
        </div>
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <span
            className={`rounded-full px-3 py-1 font-medium text-[var(--color-text)] ${connection.ok === false ? 'bg-[var(--color-warning-light)]' : connection.ok ? 'bg-[var(--color-success-light)]' : 'bg-[var(--color-surface-muted)]'}`}
          >
            {connection.label}
          </span>
          {chips.map((c) => (
            <span key={c.label} className="rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-1">
              <span className="text-[var(--color-text-muted)]">{c.label}</span> <b className="font-medium">{c.value}</b>
            </span>
          ))}
        </div>
      </header>

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
          <div className="grid gap-4 sm:grid-cols-2">
            <StatTile
              label="Dead letters"
              value={summary.summary.deadLetters}
              note="right now"
              unavailable={`${cloud} does not report message counts.`}
              to="/?tab=dlq"
              action="See dead letters"
            />
            <StatTile
              label="Active messages"
              value={summary.summary.active}
              note="right now"
              unavailable={`${cloud} does not report message counts.`}
              to="/?tab=active"
              action="See active messages"
            />
          </div>
          {namespaces.every((n) => n.capabilities?.supportsRepeatablePeek === true) ? (
            <Suspense fallback={<p role="status" className="text-sm text-[var(--color-text-muted)]">Reading the trend…</p>}>
              <TrendChart provider={provider} />
            </Suspense>
          ) : (
            // A trend of what ServiceHub has seen is silence, not good news, where it does not look on its own (R5).
            <p className="rounded-xl bg-[var(--color-surface-muted)] px-4 py-3 text-sm text-[var(--color-text-muted)]">
              ServiceHub does not watch {cloud} for dead letters on its own, so there is no trend to draw.
            </p>
          )}
          <RecentDeadLetters provider={provider} namespaces={namespaces} />
          <RecentActivity />
          <FleetCard cloud={cloud} summary={summary.summary} fleetHref={otherCloudsConnected ? '/fleet' : undefined} />
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

/** Active and Replayed are views of the same table, built in their own units. Until then they say so. */
function NotBuiltTab({ tab, cloud }: { tab: 'active'; cloud: string }) {
  const wave = 3
  return (
    <section className="px-6 py-6">
      <h1 className="text-2xl font-semibold text-[var(--color-text)]">
        {cloud} — Active messages
      </h1>
      <div className="mt-4">
        <WorkTabs current={tab} />
      </div>
      <p className="inline-block rounded-full bg-[var(--color-surface-muted)] px-4 py-1.5 text-sm text-[var(--color-text-muted)]">
        Not built yet — Wave {wave}
      </p>
    </section>
  )
}
