import { useQueryClient } from '@tanstack/react-query'
import { Info, RefreshCw } from 'lucide-react'
import { useParams } from 'react-router-dom'
import { useComponents } from '../../hooks/useComponents'
import { sidebarComponents, useCoverage } from '../../hooks/useCoverage'
import { useMe } from '../../hooks/useIdentity'
import { usePendingWork } from '../../hooks/usePendingWork'
import type { CloudKind } from '../../lib/api/coverage'
import { formatWhen } from '../../lib/format'
import { NotFoundPage } from '../../pages/NotFoundPage'
import { AttentionTable } from '../components/home/AttentionTable'
import { ComponentTable } from '../components/home/ComponentTable'
import { EvidencePanel } from '../components/home/EvidencePanel'
import { NotWatchedNotice } from '../components/home/NotWatchedNotice'
import { SignaturePanel } from '../components/home/SignaturePanel'
import { Tiles } from '../components/home/Tiles'
import { cloudLabel, cloudOf, parseScope } from '../lib/clouds'
import { homeTotals, lookingAt } from '../lib/words'
import { Tip } from '../components/Tip'

/**
 * Home for one cloud (build-plan unit C1; designs `azure-home`, `aws-home`, `gcp-home`). Every number comes from the one component
 * read (B1) and the one pending-work read, so the tiles, the table and the menu badges cannot disagree. Nothing here tests which
 * cloud it is: what differs between the three is what their numbers and capabilities say.
 */
export default function HomePage() {
  // An address that names no cloud is not Home, and must not read anything on the way to saying so.
  const cloud = cloudOf(parseScope(useParams().cloud))
  return cloud === null ? <NotFoundPage /> : <CloudHome cloud={cloud} />
}

function CloudHome({ cloud }: { cloud: CloudKind }) {
  const client = useQueryClient()
  const components = useComponents(cloud)
  const pending = usePendingWork({ provider: cloud })
  const coverage = useCoverage()
  const me = useMe()

  const cloudName = cloudLabel[cloud]
  const rows = components.data?.components ?? []
  const covered = sidebarComponents(coverage.data?.rows ?? [], cloud)
  const totals = homeTotals(rows)
  const unwatched = rows.filter((c) => !c.watched)
  const lookIds = [...new Set(unwatched.map((c) => c.namespaceId))]
  const refresh = () => {
    void client.invalidateQueries({ queryKey: ['components'] })
    void client.invalidateQueries({ queryKey: ['pending-work'] })
    void client.invalidateQueries({ queryKey: ['next', 'signatures'] })
  }

  return (
    <div className="space-y-4 px-6 py-5">
      <div className="grid gap-4 xl:grid-cols-[1fr_auto]">
        <div>
          <h1 className="text-2xl font-bold">{cloudName} Dashboard<Tip label="page:Home" /></h1>
          <p className="text-sm text-[var(--color-text-muted)]">Monitor and manage dead-letter messages across supported {cloudName} PaaS components.</p>
        </div>
        <div className="flex gap-3">
          <aside aria-label="What you’re looking at" className="flex max-w-xl gap-2.5 rounded-xl border border-[var(--color-primary-200)] bg-[var(--color-primary-50)] p-3 text-sm">
            <Info className="mt-0.5 h-4 w-4 shrink-0 text-[var(--color-primary-700)]" aria-hidden="true" />
            <div><p className="font-semibold">What you’re looking at</p><p className="text-[var(--color-text)]">{lookingAt(cloudName, rows)}</p></div>
          </aside>
          <div className="flex items-start gap-3 rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-3 text-sm">
            <div>
              <p className="text-[var(--color-text-muted)]">Last updated</p>
              <p className="font-semibold">{components.data ? formatWhen(components.data.generatedAtUtc, new Date()) : '—'}</p>
            </div>
            <button type="button" onClick={refresh} aria-label="Refresh" className="rounded-md p-1.5 hover:bg-[var(--color-surface-muted)]"><RefreshCw className="h-4 w-4" aria-hidden="true" /></button>
          </div>
        </div>
      </div>

      {components.isError ? (
        <p role="alert" className="rounded-xl border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800">The {cloudName} components could not be read. Nothing is shown rather than numbers that may be wrong.</p>
      ) : components.isPending ? (
        <p role="status" className="text-sm text-[var(--color-text-muted)]">Loading…</p>
      ) : (
        <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_26rem]">
          <div className="min-w-0 space-y-4">
            <Tiles totals={totals} observedAtUtc={components.data.generatedAtUtc} cloudName={cloudName} />
            {unwatched.length > 0 && <NotWatchedNotice cloudName={cloudName} provider={cloud} namespaceIds={lookIds} me={me.data} onLook={refresh} />}
            <ComponentTable components={rows} cloudName={cloudName} />
          </div>
          <div className="space-y-4">
            <SignaturePanel provider={cloud} cloudName={cloudName} notWatched={unwatched.length > 0} recorded={totals.first.recorded ?? (totals.first.label === 'Dead letters recorded' ? totals.first.value : null)} inDeadLetter={totals.first.label === 'In dead-letter now' ? totals.first.value : null} />
            <EvidencePanel cloudName={cloudName} rows={covered} />
          </div>
        </div>
      )}

      {pending.isError
        ? <p role="alert" className="rounded-xl border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800">What needs your attention could not be read.</p>
        : <AttentionTable items={pending.data?.items ?? []} coverage={coverage.data?.rows ?? []} me={me.data} />}
    </div>
  )
}
