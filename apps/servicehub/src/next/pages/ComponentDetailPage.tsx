import { useQueryClient } from '@tanstack/react-query'
import { ChevronRight, Info, RefreshCw } from 'lucide-react'
import { Link, useLocation, useParams, useSearchParams } from 'react-router-dom'
import { EvidencePanel } from '../components/home/EvidencePanel'
import { NotWatchedNotice } from '../components/home/NotWatchedNotice'
import { SignaturePanel } from '../components/home/SignaturePanel'
import { AccessTab } from '../components/detail/AccessTab'
import { AuditTab } from '../components/detail/AuditTab'
import { ConfigurationTab } from '../components/detail/ConfigurationTab'
import { DeadLettersTab } from '../components/detail/DeadLettersTab'
import { DetailTabs } from '../components/detail/DetailTabs'
import { RecoveryTab } from '../components/detail/RecoveryTab'
import { EntityTable } from '../components/detail/EntityTable'
import { fullName } from '../lib/entityRows'
import { MessagesPreview } from '../components/detail/MessagesPreview'
import { useComponent } from '../../hooks/useComponents'
import { useCoverage } from '../../hooks/useCoverage'
import { useMe } from '../../hooks/useIdentity'
import { useEntities, useNamespace } from '../../hooks/useNamespaces'
import { toProblem } from '../../lib/api/client'
import type { ConnectedComponent, Count } from '../../lib/api/components'
import { formatWhen } from '../../lib/format'
import { NotFoundPage } from '../../pages/NotFoundPage'
import { cloudLabel, cloudOf, parseScope } from '../lib/clouds'
import { countsWords, credentialWords, homeTotals, lookingAtOne, statusWords } from '../lib/words'
import { componentTabs, nextEntry, nextHref } from '../nav/navigation'
import { NextPlaceholderPage } from './NextPlaceholderPage'

const n = (value: number) => value.toLocaleString('en-US')
const small = 'text-xs text-[var(--color-text-muted)]'

function Tile({ title, children, tone }: { title: string; children: React.ReactNode; tone?: string }) {
  return (
    <section aria-label={title} className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
      <h2 className="text-sm font-semibold">{title}</h2>
      <div className={tone}>{children}</div>
    </section>
  )
}

function Info2({ label, children }: { label: string; children: React.ReactNode }) {
  return <div className="flex gap-4 border-b border-[var(--color-border)] py-2 text-sm last:border-0"><dt className="w-32 shrink-0 text-[var(--color-text-muted)]">{label}</dt><dd className="min-w-0 font-semibold">{children}</dd></div>
}

/**
 * One connection of one component (build-plan unit D2: the Overview tab and the page around every tab; designs
 * `azure-service-bus-detail`, `aws-sqs-detail`, `aws-sns-detail`, `gcp-pubsub-detail`). One file for all four: what differs between them
 * is what the data holds, so the tiles and the table's columns come from the counts and places the connection returns, not from the cloud.
 */
export default function ComponentDetailPage() {
  const params = useParams()
  const cloud = cloudOf(parseScope(params.cloud))
  return cloud === null || !params.component || !params.namespaceId ? <NotFoundPage /> : <Detail cloudKey={cloud} componentKey={params.component} namespaceId={params.namespaceId} />
}

function Detail({ cloudKey: cloud, componentKey, namespaceId }: { cloudKey: NonNullable<ReturnType<typeof cloudOf>>; componentKey: string; namespaceId: string }) {
  const { pathname } = useLocation()
  const [search] = useSearchParams()
  const client = useQueryClient()
  const one = useComponent(namespaceId, componentKey)
  const ns = useNamespace(namespaceId)
  const entities = useEntities(namespaceId)
  const coverage = useCoverage()
  const me = useMe()

  if (one.isError) {
    return toProblem(one.error).status === 404
      ? <NotFoundPage />
      : <p role="alert" className="m-6 rounded-xl border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800">This connection could not be read. Nothing is shown rather than numbers that may be wrong.</p>
  }
  if (coverage.isError) return <p role="alert" className="m-6 rounded-xl border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800">What ServiceHub covers could not be read.</p>
  if (one.isPending || coverage.isPending) return <p role="status" className="px-6 py-6 text-sm text-[var(--color-text-muted)]">Loading…</p>
  const c: ConnectedComponent = one.data
  const row = coverage.data.rows.find((r) => r.cloud === cloud && r.componentKey === componentKey && r.inSidebar)
  if (c.cloud !== cloud || !row) return <NotFoundPage />

  const cloudName = cloudLabel[cloud]
  const caps = ns.data?.capabilities
  const place = ns.data?.awsRegion ?? ns.data?.gcpProjectId ?? null
  const totals = homeTotals([c])
  const first = totals.first
  const status = statusWords(c)
  const now = new Date()
  const activeByName = new Map((entities.data?.entities ?? []).map((e) => [e.name, e.activeMessages]))
  const activeOf = (path: string): Count | null => {
    const v = activeByName.get(path)
    return v === undefined || v === null ? null : { value: v, kind: caps?.countsAreApproximate ? 'approximate' : 'exact' }
  }
  const holders = c.entities.map((e) => activeOf(fullName(e))).filter((x): x is Count => x !== null)
  const activeTotal = holders.length > 0 ? holders.reduce((sum, x) => sum + (x.value ?? 0), 0) : null
  const withStore = c.entities.filter((e) => e.deadLetterStore !== null).length
  const notLooked = c.watched ? 0 : c.entities.filter((e) => e.recorded?.messages === null).length
  const tab = componentTabs.find((t) => t.value === search.get('tab'))?.value ?? 'overview'
  const here = nextHref(nextEntry('component-detail'), { cloud, component: componentKey, namespaceId })
  const hasStuck = coverage.data.rows.some((r) => r.id === `${componentKey}.stuck`)
  const deadLetterCount = c.watched ? (c.deadLetters.value === null ? '—' : n(c.deadLetters.value)) : `${first.label === 'Dead letters recorded' ? n(first.value ?? 0) : n(first.recorded ?? 0)} recorded`
  const refresh = () => {
    void client.invalidateQueries({ queryKey: ['components'] })
    void client.invalidateQueries({ queryKey: ['namespaces'] })
    void client.invalidateQueries({ queryKey: ['dead-letters'] })
  }
  const detail = (entity: string) => `${here}?tab=dead-letters&entity=${encodeURIComponent(entity)}`

  return (
    <div className="space-y-4 px-6 py-5">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <nav aria-label="Breadcrumb" className="mb-1 flex flex-wrap items-center gap-1 text-sm text-[var(--color-text-muted)]">
            <Link to={nextHref(nextEntry('home'), { cloud })} className="hover:underline">{cloudName}</Link>
            <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
            <Link to={nextHref(nextEntry('component-list'), { cloud, component: componentKey })} className="hover:underline">{row.name}</Link>
            <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
            <span aria-current="page" className="font-semibold text-[var(--color-primary-700)]">{c.name}</span>
          </nav>
          <h1 className="flex flex-wrap items-center gap-3 text-2xl font-bold">{c.name}
            <span className={`rounded-md px-2 py-0.5 text-xs font-semibold ${status.tone === 'bad' ? 'bg-red-50 text-red-700' : status.tone === 'warn' ? 'bg-amber-50 text-amber-800' : 'bg-green-50 text-green-800'}`}>
              {c.needsYou > 0 ? `Needs you (${c.needsYou})` : status.text}
            </span>
          </h1>
          <p className="text-sm text-[var(--color-text-muted)]">{c.componentName}{place ? ` · ${place}` : ''} · <span className="capitalize">{c.environment}</span></p>
        </div>
        <div className="flex gap-3">
          <aside aria-label="What you’re looking at" className="flex max-w-xl gap-2.5 rounded-xl border border-[var(--color-primary-200)] bg-[var(--color-primary-50)] p-3 text-sm">
            <Info className="mt-0.5 h-4 w-4 shrink-0 text-[var(--color-primary-700)]" aria-hidden="true" />
            <div><p className="font-semibold">What you’re looking at</p><p>{lookingAtOne(cloudName, c)}</p></div>
          </aside>
          <div className="flex items-start gap-3 rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-3 text-sm">
            <div><p className="text-[var(--color-text-muted)]">{c.watched ? 'Last observed' : 'Last look'}</p><p className="font-semibold">{c.observedAtUtc ? formatWhen(c.observedAtUtc, now) : '—'}</p></div>
            <button type="button" aria-label="Refresh" onClick={refresh} className="rounded-md p-1.5 hover:bg-[var(--color-surface-muted)]"><RefreshCw className="h-4 w-4" aria-hidden="true" /></button>
          </div>
        </div>
      </div>

      <div className="flex flex-wrap items-end justify-between gap-3">
        <DetailTabs basePath={here} active={tab} hasStuck={hasStuck} deadLetterCount={deadLetterCount} />
        <Link to={`${nextHref(nextEntry('send'), {}, pathname)}&ns=${encodeURIComponent(namespaceId)}`} className="rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3.5 py-2 text-xs font-bold hover:bg-[var(--color-surface-muted)]">Send a test message</Link>
      </div>

      {tab === 'dead-letters' ? (
        <>
          {!c.watched && ns.data && <NotWatchedNotice cloudName={cloudName} provider={ns.data.provider} namespaceIds={[namespaceId]} me={me.data} onLook={refresh} />}
          <DeadLettersTab component={c} me={me.data} />
        </>
      ) : tab === 'audit' ? (
        <AuditTab component={c} />
      ) : tab === 'configuration' ? (
        <ConfigurationTab component={c} namespace={ns.data} cloudName={cloudName} />
      ) : tab === 'access' ? (
        <AccessTab component={c} namespace={ns.data} />
      ) : tab === 'recovery' ? (
        <RecoveryTab component={c} canProve={caps?.canProveDlqAbsence === true} />
      ) : tab !== 'overview' ? (
        <NextPlaceholderPage entry={nextEntry('component-detail')} tab={componentTabs.find((t) => t.value === tab)} />
      ) : (
        <>
          <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-5">
            <Tile title={first.label}>
              <p className="text-3xl font-bold leading-tight" data-testid="tile-first">
                {first.value === null ? <span className="text-lg font-semibold">{first.unreadable ? 'Can’t read' : 'Not looked yet'}</span> : n(first.value)}
                {first.value !== null && first.approximate && <span className="ml-1.5 text-sm font-normal text-[var(--color-text-muted)]">(approx)</span>}
              </p>
              <p className={small}>Across {c.entities.length} {c.entities.length === 1 ? 'place' : 'places'}{first.label === 'Dead letters recorded' ? ' · at your last look' : ''}</p>
            </Tile>
            {first.recorded !== null && (
              <Tile title="Recorded at last look"><p className="text-3xl font-bold leading-tight" data-testid="tile-recorded">{n(first.recorded)}</p><p className={small}>What ServiceHub kept</p></Tile>
            )}
            <Tile title="Needs you">
              <p className="text-3xl font-bold leading-tight text-red-700" data-testid="tile-needs">{c.needsYou}</p>
              <p className={small}>(approvals: {c.approvals}, hand-offs: {c.handOffs})</p>
            </Tile>
            {activeTotal !== null && (
              <Tile title="Active messages">
                <p className="text-3xl font-bold leading-tight" data-testid="tile-active">{n(activeTotal)}{caps?.countsAreApproximate && <span className="ml-1.5 text-sm font-normal text-[var(--color-text-muted)]">(approx)</span>}</p>
                <p className={small}>Waiting to be processed</p>
              </Tile>
            )}
            {withStore > 0 && (
              <Tile title="With a dead-letter place">
                <p className="text-3xl font-bold leading-tight" data-testid="tile-store">{withStore} <span className="text-base font-semibold">of {c.entities.length}</span></p>
                <p className={small}>The rest have nowhere for failed messages to go</p>
              </Tile>
            )}
            {notLooked > 0 && (
              <Tile title="Not looked yet"><p className="text-3xl font-bold leading-tight" data-testid="tile-not-looked">{notLooked}</p><p className={small}>Use Look now to record them</p></Tile>
            )}
          </div>

          {!c.watched && ns.data && <NotWatchedNotice cloudName={cloudName} provider={ns.data.provider} namespaceIds={[namespaceId]} me={me.data} onLook={refresh} />}

          <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_24rem]">
            <div className="min-w-0 space-y-4">
              <EntityTable component={c} active={activeOf} detail={detail} />
              <MessagesPreview namespaceId={namespaceId} watched={c.watched} me={me.data} tabHref={`${here}?tab=dead-letters`} />
            </div>
            <div className="space-y-4">
              <section aria-labelledby="info-title" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
                <h2 id="info-title" className="mb-2 text-base font-semibold">Component information</h2>
                <dl>
                  <Info2 label="Name">{c.name}</Info2>
                  <Info2 label="Type">{c.componentName}</Info2>
                  {place && <Info2 label={ns.data?.awsRegion ? 'Region' : 'Project'}>{place}</Info2>}
                  <Info2 label="Environment"><span className="capitalize">{c.environment}</span></Info2>
                  <Info2 label="Connection">{c.reachable ? <span className="text-green-700">Connected · {c.canSend ? 'Read + Send' : 'Read only'}</span> : <span className="text-amber-800">Can’t reach{c.lastError ? ` — ${c.lastError}` : ''}</span>}</Info2>
                  {ns.data && <Info2 label="Signs in with">{credentialWords(ns.data.authType)}</Info2>}
                  <Info2 label="Watched">{c.watched ? 'Automatically' : 'Only when you look'}</Info2>
                  <Info2 label="Counts">{countsWords(cloudName, c.deadLetters)}</Info2>
                </dl>
              </section>
              <EvidencePanel cloudName={cloudName} rows={[row]} heading="Evidence and replay outcome" />
              {ns.data && (
                <SignaturePanel provider={ns.data.provider} namespaceId={namespaceId} title="Failure signatures here" cloudName={cloudName} notWatched={!c.watched}
                  recorded={first.recorded ?? (first.label === 'Dead letters recorded' ? first.value : null)} inDeadLetter={first.label === 'In dead-letter now' ? first.value : null} />
              )}
            </div>
          </div>
        </>
      )}
    </div>
  )
}
