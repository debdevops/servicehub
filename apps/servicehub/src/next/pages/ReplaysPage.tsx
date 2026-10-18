import { useQueryClient } from '@tanstack/react-query'
import { Check, CircleHelp, Clock, RotateCcw, TriangleAlert } from 'lucide-react'
import type { ReactNode } from 'react'
import { useLocation, useParams, useSearchParams } from 'react-router-dom'
import { Pager } from '../../components/ui/Pager'
import { useComponents } from '../../hooks/useComponents'
import { useNamespaces } from '../../hooks/useNamespaces'
import { useRecoverySummary } from '../../hooks/useRecoverySummary'
import { useReplays } from '../../hooks/useReplay'
import type { ReplayQuery } from '../../lib/api/replay'
import { formatWhen } from '../../lib/format'
import { usePageSize } from '../../lib/pageSize'
import { ReplayTable } from '../components/ReplayTable'
import { ToolPageHeader } from '../components/ToolPageHeader'
import { cloudLabel, cloudOf, parseScope } from '../lib/clouds'
import { outcomeTone, recoveryWindows, type RecoveryWindowId } from '../lib/words'

const field = 'rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-2.5 py-1.5 text-sm'
const th = 'px-3 py-2 text-left text-xs font-semibold text-[var(--color-text-muted)]'
const td = 'px-3 py-2.5 align-top text-sm'

const endings: readonly { id: NonNullable<ReplayQuery['ending']> | ''; label: string }[] = [
  { id: '', label: 'all' }, { id: 'fixed', label: 'Verified' }, { id: 'unproven', label: 'Unverified' }, { id: 'returned', label: 'Came back' },
  { id: 'watching', label: 'Watching' }, { id: 'notsent', label: 'Not sent' },
]
const bys: readonly { id: NonNullable<ReplayQuery['by']> | ''; label: string }[] = [{ id: '', label: 'anyone' }, { id: 'people', label: 'people' }, { id: 'autonomous', label: 'the Agent' }]
const asWindow = (v: string | null): RecoveryWindowId => recoveryWindows.find((w) => w.id === v)?.id ?? '7d'

/** What each outcome word means, and what it does not (design `p09`). Static words; the wording of an outcome itself lives in `outcomeWords`. */
const words = [
  { word: 'Verified', tone: 'good', means: 'The message did not come back to dead-letter within the observation window.', not: 'That the consumer finished its work.' },
  { word: 'Watching', tone: 'neutral', means: 'Replayed, and still inside the window.', not: 'That it worked.' },
  { word: 'Came back', tone: 'bad', means: 'It failed again and is in dead-letter again. Nothing retries it.', not: 'That the replay was wrong. Only that this message failed again.' },
  { word: 'Unverified', tone: 'caution', means: 'Sent, but this component cannot prove it stayed fixed. Recorded as verification required.', not: 'That it failed.' },
] as const

/**
 * Replay history (build-plan unit E4; design `p09-replay-history`): every message put back, by whom, and what ServiceHub observed
 * afterwards — across every connection on one cloud or all of them. The same reads and the same outcome words as a connection's Recovery
 * tab (D4): one replays list and one recovery summary, so the two cannot disagree. Read-only. The period, outcome, who and component
 * are in the address.
 */
export default function ReplaysPage() {
  const scope = parseScope(useParams().cloud)
  const cloud = cloudOf(scope)
  const { pathname } = useLocation()
  const client = useQueryClient()
  const [params, setParams] = useSearchParams()
  const [pageSize, setPageSize] = usePageSize()
  const components = useComponents(cloud)
  const namespaces = useNamespaces()

  const page = Math.max(1, Number(params.get('page')) || 1)
  const window = asWindow(params.get('window'))
  const ending = endings.find((e) => e.id !== '' && e.id === params.get('ending'))?.id as ReplayQuery['ending'] | undefined
  const by = bys.find((b) => b.id !== '' && b.id === params.get('by'))?.id as ReplayQuery['by'] | undefined
  const namespaceId = params.get('connection') ?? undefined
  // `?rule=<id>`: only what this Auto Replay rule sent (linked from the Auto Replay rules page).
  const ruleId = Number(params.get('rule')) || undefined

  const replays = useReplays({ provider: cloud ?? undefined, namespaceId, ruleId, window, ending, by, page, pageSize })
  const summary = useRecoverySummary({ provider: cloud ?? undefined, namespaceId, window })
  const count = (state: string) => summary.data?.states.find((s) => s.state === state)?.count ?? 0
  const replayStates = new Set(['Executing', 'Observing', 'ExecutionFailed', 'ExecutionUnknown', 'Recovered', 'Returned', 'Unverified', 'Expired'])
  const total = (summary.data?.states ?? []).filter((s) => replayStates.has(s.state)).reduce((sum, s) => sum + s.count, 0)

  const connections = [...new Map((components.data?.components ?? []).map((c) => [c.namespaceId, c.name])).entries()]
  const nameOf = new Map((namespaces.data ?? []).map((ns) => [ns.id, ns.displayName ?? ns.name]))
  const windowLabel = recoveryWindows.find((w) => w.id === window)?.label ?? ''
  const items = replays.data?.items ?? []
  const filtering = !!ending || !!by || ruleId !== undefined
  const now = new Date()
  const fmt = (iso: string) => formatWhen(iso, now)

  const change = (patch: Record<string, string | null>) => setParams((current) => {
    const next = new URLSearchParams(current)
    for (const [k, v] of Object.entries(patch)) { if (v === null || v === '') next.delete(k); else next.set(k, v) }
    if (!('page' in patch)) next.delete('page')
    return next
  }, { replace: true })

  return (
    <div className="space-y-4 px-6 py-5">
      <ToolPageHeader scope={scope} title="Replay history" updatedAtMs={Math.max(replays.dataUpdatedAt, summary.dataUpdatedAt)}
        onRefresh={() => { void client.invalidateQueries({ queryKey: ['replays'] }); void client.invalidateQueries({ queryKey: ['recovery'] }) }}
        lookingAt={`Every message put back ${scope === 'all' ? 'on every connected cloud' : `on ${cloudLabel[scope]}`}, by whom, and what ServiceHub observed afterwards.`} />

      {replays.isError || summary.isError ? (
        <p role="alert" className="rounded-xl border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800">What was replayed could not be read. Nothing is shown rather than numbers that may be wrong.</p>
      ) : (
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
          <Tile icon={<RotateCcw className="h-5 w-5 text-[var(--color-primary-700)]" aria-hidden="true" />} title="Replays" note={windowLabel}>{summary.isPending ? '…' : total.toLocaleString('en-US')}</Tile>
          {total > 0 && (
            <>
              <Tile icon={<Check className="h-5 w-5 text-green-700" aria-hidden="true" />} title="Verified" note="Did not return in the window">{count('Recovered').toLocaleString('en-US')}</Tile>
              <Tile icon={<TriangleAlert className="h-5 w-5 text-red-600" aria-hidden="true" />} title="Came back" note="Failed again after replay">{count('Returned').toLocaleString('en-US')}</Tile>
              <Tile icon={<CircleHelp className="h-5 w-5 text-amber-700" aria-hidden="true" />} title="Unverified" note="Sent; cannot show whether it returned">{count('Unverified').toLocaleString('en-US')}</Tile>
              <Tile icon={<Clock className="h-5 w-5 text-[var(--color-text-muted)]" aria-hidden="true" />} title="Watching" note="Still inside the window">{count('Observing').toLocaleString('en-US')}</Tile>
            </>
          )}
        </div>
      )}

      <div role="group" aria-label="Filters" className="flex flex-wrap items-center gap-3">
        <label className="flex items-center gap-2 text-sm"><span className="text-[var(--color-text-muted)]">Connection:</span>
          <select className={field} value={namespaceId ?? ''} onChange={(e) => change({ connection: e.target.value || null })}>
            <option value="">all</option>{connections.map(([id, name]) => <option key={id} value={id}>{name}</option>)}
          </select>
        </label>
        <label className="flex items-center gap-2 text-sm"><span className="text-[var(--color-text-muted)]">Outcome:</span>
          <select className={field} value={ending ?? ''} onChange={(e) => change({ ending: e.target.value || null })}>
            {endings.map((e) => <option key={e.id} value={e.id}>{e.label}</option>)}
          </select>
        </label>
        <label className="flex items-center gap-2 text-sm"><span className="text-[var(--color-text-muted)]">Replayed by:</span>
          <select className={field} value={by ?? ''} onChange={(e) => change({ by: e.target.value || null })}>
            {bys.map((b) => <option key={b.id} value={b.id}>{b.label}</option>)}
          </select>
        </label>
        <label className="flex items-center gap-2 text-sm"><span className="text-[var(--color-text-muted)]">When:</span>
          <select className={field} value={window} onChange={(e) => change({ window: e.target.value === '7d' ? null : e.target.value })}>
            {recoveryWindows.map((w) => <option key={w.id} value={w.id}>{w.label}</option>)}
          </select>
        </label>
        {filtering && <button type="button" onClick={() => change({ ending: null, by: null, rule: null })} className="text-sm font-semibold text-[var(--color-primary-700)]">Clear filters</button>}
        {ruleId !== undefined && <span className="text-xs text-[var(--color-text-muted)]">Only what one Auto Replay rule sent.</span>}
        {by && <span className="text-xs text-[var(--color-text-muted)]">The tiles count everyone’s replays; the list is narrowed.</span>}
      </div>

      <section aria-labelledby="put-back" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
        <h2 id="put-back" className="px-4 py-3 text-base font-semibold">What was put back</h2>
        {replays.isError ? null : replays.isPending ? (
          <p role="status" className="border-t border-[var(--color-border)] px-4 py-6 text-sm text-[var(--color-text-muted)]">Loading…</p>
        ) : items.length === 0 ? (
          <p className="border-t border-[var(--color-border)] px-4 py-6 text-sm text-[var(--color-text-muted)]">
            {filtering || namespaceId ? 'No replay matches those filters.' : `Nothing has been replayed ${scope === 'all' ? '' : `on ${cloudLabel[scope]} `}in this period (${windowLabel.toLowerCase()}). An outcome appears here once a replay has happened.`}
          </p>
        ) : (
          <ReplayTable items={items} caption="Messages put back" fmt={fmt} connection={(r) => nameOf.get(r.namespaceId) ?? '—'}
            openHref={(r) => `${pathname}?dialog=message&id=${r.dlqMessageId}`} />
        )}
        <Pager page={page} pageSize={pageSize} total={replays.data?.total ?? 0} filtered={filtering} onPage={(p) => change({ page: String(p) })} onPageSize={(n) => { setPageSize(n); change({}) }} label="replays" />
      </section>

      <section aria-labelledby="four-words" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
        <h2 id="four-words" className="px-4 py-3 text-base font-semibold">The four outcome words</h2>
        <div className="overflow-x-auto">
          <table className="w-full border-t border-[var(--color-border)]">
            <caption className="sr-only">What each outcome word means</caption>
            <thead><tr><th className={th}>Word</th><th className={th}>Means</th><th className={th}>Does not mean</th></tr></thead>
            <tbody>
              {words.map((w) => (
                <tr key={w.word} className="border-t border-[var(--color-border)]">
                  <td className={td}><span className={`inline-block rounded-md px-2 py-0.5 text-xs font-semibold ${outcomeTone[w.tone]}`}>{w.word}</span></td>
                  <td className={td}>{w.means}</td><td className={td}>{w.not}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}

function Tile({ icon, title, note, children }: { icon: ReactNode; title: string; note: string; children: ReactNode }) {
  return (
    <div className="flex items-center gap-3 rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
      <span className="rounded-full bg-[var(--color-surface-muted)] p-2.5">{icon}</span>
      <div><p className="text-sm font-semibold">{title}</p><p className="text-2xl font-bold">{children}</p><p className="text-xs text-[var(--color-text-muted)]">{note}</p></div>
    </div>
  )
}
