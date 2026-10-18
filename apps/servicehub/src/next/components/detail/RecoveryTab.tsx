import { Link, useLocation, useSearchParams } from 'react-router-dom'
import { Pager } from '../../../components/ui/Pager'
import { useRecoverySummary } from '../../../hooks/useRecoverySummary'
import { useReplays } from '../../../hooks/useReplay'
import type { ConnectedComponent } from '../../../lib/api/components'
import type { ReplayQuery } from '../../../lib/api/replay'
import { formatWhen } from '../../../lib/format'
import { usePageSize } from '../../../lib/pageSize'
import { cloudLabel } from '../../lib/clouds'
import { outcomeFootnote, recoveryWindows, type RecoveryWindowId } from '../../lib/words'
import { nextEntry, nextHref } from '../../nav/navigation'
import { ReplayTable } from '../ReplayTable'

const field = 'rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2 text-sm'

const endings: readonly { id: NonNullable<ReplayQuery['ending']> | ''; label: string }[] = [
  { id: '', label: 'Any outcome' }, { id: 'fixed', label: 'Verified' }, { id: 'unproven', label: 'Unverified' }, { id: 'returned', label: 'Came back' },
  { id: 'watching', label: 'Watching' }, { id: 'notsent', label: 'Not sent' },
]
const asWindow = (v: string | null): RecoveryWindowId => recoveryWindows.find((w) => w.id === v)?.id ?? '7d'
const asEnding = (v: string | null) => endings.find((e) => e.id !== '' && e.id === v)?.id as ReplayQuery['ending'] | undefined

function Tile({ title, children, note }: { title: string; children: React.ReactNode; note: string }) {
  return (
    <section aria-label={title} className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
      <h2 className="text-sm font-semibold">{title}</h2>
      <p className="text-3xl font-bold leading-tight">{children}</p>
      <p className="text-xs text-[var(--color-text-muted)]">{note}</p>
    </section>
  )
}

/**
 * The Recovery tab of one connection (build-plan unit D4; design `p05c-service-bus-recovery`): what was replayed from here, by whom,
 * and what was observed afterwards. Outcome words appear only for a replay that happened; with none, it says so and draws no zeros
 * that look like results. The best outcome is "Verified" only where the cloud can prove it (`canProve`, from its capabilities);
 * elsewhere the same replays end "Unverified" and a tile says so. Read-only: replaying is its own dialog (F2).
 */
export function RecoveryTab({ component: c, canProve }: { component: ConnectedComponent; canProve: boolean }) {
  const { pathname } = useLocation()
  const [params, setParams] = useSearchParams()
  const [pageSize, setPageSize] = usePageSize()
  const page = Math.max(1, Number(params.get('page')) || 1)
  const window = asWindow(params.get('window'))
  const ending = asEnding(params.get('ending'))

  const replays = useReplays({ namespaceId: c.namespaceId, window, ending, page, pageSize })
  const summary = useRecoverySummary({ namespaceId: c.namespaceId, window })
  const count = (state: string) => summary.data?.states.find((s) => s.state === state)?.count ?? 0
  const total = replays.data?.total ?? 0
  const items = replays.data?.items ?? []
  const filtering = !!ending
  const windowLabel = recoveryWindows.find((w) => w.id === window)?.label ?? ''
  const now = new Date()
  const fmt = (iso: string) => formatWhen(iso, now)

  const change = (patch: Record<string, string | null>) =>
    setParams((current) => {
      const next = new URLSearchParams(current)
      for (const [k, v] of Object.entries(patch)) {
        if (v === null || v === '') next.delete(k)
        else next.set(k, v)
      }
      if (!('page' in patch)) next.delete('page')
      return next
    }, { replace: true })

  // A replay's own page is the Message dialog; "Replay history" is the cross-connection list (E4).
  const open = (id: number) => `${pathname}?tab=recovery&dialog=message&id=${id}`

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <label className="flex items-center gap-2 text-sm">Period
          <select aria-label="Period" className={field} value={window} onChange={(e) => change({ window: e.target.value === '7d' ? null : e.target.value })}>
            {recoveryWindows.map((w) => <option key={w.id} value={w.id}>{w.label}</option>)}
          </select>
        </label>
        <label className="flex items-center gap-2 text-sm">Outcome
          <select aria-label="Outcome" className={field} value={ending ?? ''} onChange={(e) => change({ ending: e.target.value || null })}>
            {endings.map((e) => <option key={e.id} value={e.id}>{e.label}</option>)}
          </select>
        </label>
        {filtering && <button type="button" onClick={() => change({ ending: null })} className="text-sm font-semibold text-[var(--color-primary-700)]">Clear filters</button>}
      </div>

      {replays.isError || summary.isError ? (
        <p role="alert" className="rounded-xl border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800">What was replayed here could not be read. Nothing is shown rather than numbers that may be wrong.</p>
      ) : (
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <Tile title="Replays here" note={windowLabel}><span data-testid="tile-replays">{replays.isPending ? '…' : (filtering ? summaryTotal(summary.data?.states) : total).toLocaleString('en-US')}</span></Tile>
          {(summary.data?.total ?? 0) > 0 && (
            <>
              {canProve
                ? <Tile title="Verified" note="Did not return in the window"><span data-testid="tile-verified">{count('Recovered').toLocaleString('en-US')}</span></Tile>
                : <Tile title="Unverified" note="Sent; this cloud cannot show whether it returned"><span data-testid="tile-unverified">{count('Unverified').toLocaleString('en-US')}</span></Tile>}
              <Tile title="Came back" note="Failed again after replay"><span data-testid="tile-returned">{count('Returned').toLocaleString('en-US')}</span></Tile>
              <Tile title="Watching" note="Still inside the window"><span data-testid="tile-watching">{count('Observing').toLocaleString('en-US')}</span></Tile>
            </>
          )}
        </div>
      )}

      <section aria-labelledby="rec-title" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
        <div className="flex flex-wrap items-center justify-between gap-2 px-4 py-3">
          <h2 id="rec-title" className="text-base font-semibold">Replays on this component</h2>
          <Link to={nextHref(nextEntry('replays'), { cloud: c.cloud })} className="text-xs font-semibold text-[var(--color-primary-700)]">Open Replay history →</Link>
        </div>
        {replays.isError ? null : replays.isPending ? (
          <p role="status" className="border-t border-[var(--color-border)] px-4 py-6 text-sm text-[var(--color-text-muted)]">Loading…</p>
        ) : items.length === 0 ? (
          <p className="border-t border-[var(--color-border)] px-4 py-6 text-sm text-[var(--color-text-muted)]">
            {filtering ? 'No replay matches that outcome.' : `Nothing has been replayed from ${c.name} in this period (${windowLabel.toLowerCase()}). An outcome appears here once a replay has happened.`}
          </p>
        ) : (
          <ReplayTable items={items} caption={`Replays from ${c.name} on ${cloudLabel[c.cloud]}`} openHref={(r) => open(r.dlqMessageId)} fmt={fmt} />
        )}
        {items.length > 0 && <p className="border-t border-[var(--color-border)] px-4 py-2 text-xs text-[var(--color-text-muted)]">{outcomeFootnote(canProve)}</p>}
        <Pager page={page} pageSize={pageSize} total={total} filtered={filtering} onPage={(p) => change({ page: String(p) })} onPageSize={(n) => { setPageSize(n); change({}) }} label="replays" />
      </section>
    </div>
  )
}

/** The summary's replay states added up, for when the list is filtered and its own total is no longer "all replays here". */
function summaryTotal(states: readonly { state: string; count: number }[] | undefined): number {
  const replayStates = new Set(['Executing', 'Observing', 'ExecutionFailed', 'ExecutionUnknown', 'Recovered', 'Returned', 'Unverified', 'Expired'])
  return (states ?? []).filter((s) => replayStates.has(s.state)).reduce((n, s) => n + s.count, 0)
}
