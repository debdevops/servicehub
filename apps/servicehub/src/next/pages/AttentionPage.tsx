import { useQueryClient } from '@tanstack/react-query'
import { Hand, Hourglass, ShieldCheck, TriangleAlert } from 'lucide-react'
import type { ReactNode } from 'react'
import { useParams, useSearchParams } from 'react-router-dom'
import { useCoverage } from '../../hooks/useCoverage'
import { useMe } from '../../hooks/useIdentity'
import { usePendingWork } from '../../hooks/usePendingWork'
import type { PendingWorkItem } from '../../lib/api/pendingWork'
import { formatAge } from '../../lib/format'
import { ToolPageHeader } from '../components/ToolPageHeader'
import { AttentionTable, type AttentionTab } from '../components/home/AttentionTable'
import { cloudLabel, cloudOf, parseScope } from '../lib/clouds'
import { familyWords } from '../lib/words'

const typeOf = (i: PendingWorkItem) => i.type ?? (i.kind === 'unresolved' ? 'handOff' : 'approval')
const needsAPerson = (i: PendingWorkItem) => i.kind === 'approval' || i.kind === 'unresolved'

/** Age bands, in hours. The filter keeps items AT LEAST this old. */
const ages = [{ value: 'any', text: 'any', hours: 0 }, { value: '1h', text: 'over 1 h', hours: 1 }, { value: '24h', text: 'over 1 day', hours: 24 }] as const

const tabOf = (v: string | null): AttentionTab => (v === 'approvals' || v === 'handOffs' ? v : 'all')

/**
 * Needs your attention (build-plan unit E1; design `p06-needs-your-attention`): everything on one cloud, or all of them, that is
 * waiting for a person — the replays the Agent is holding and the attempts ServiceHub handed off. One read (`/pending-work`), the
 * same one that feeds Home and the menu badges, so the numbers cannot disagree. The tab and every filter live in the address.
 */
export default function AttentionPage() {
  const scope = parseScope(useParams().cloud)
  const cloud = cloudOf(scope)
  const client = useQueryClient()
  const [params, setParams] = useSearchParams()
  const pending = usePendingWork(cloud ? { provider: cloud } : {})
  const coverage = useCoverage()
  const me = useMe()

  const scopeName = cloudLabel[scope]
  const rows = coverage.data?.rows ?? []
  const nameOf = (key: string | null | undefined) => rows.find((r) => r.componentKey === key)?.name ?? key ?? 'Unknown'
  const mine = (pending.data?.items ?? []).filter(needsAPerson)

  const component = params.get('component') ?? ''
  const family = params.get('family') ?? ''
  const age = ages.find((a) => a.value === params.get('age')) ?? ages[0]
  const tab = tabOf(params.get('type'))

  const set = (key: string, value: string) => setParams((p) => {
    const next = new URLSearchParams(p)
    if (value === '' || value === 'any' || (key === 'type' && value === 'all')) next.delete(key); else next.set(key, value)
    return next
  }, { replace: true })

  const now = new Date()
  // Options come from what is actually waiting, never from a fixed list: an option with nothing behind it would be a dead end.
  const components = [...new Set(mine.map((i) => i.componentKey).filter((k): k is string => !!k))]
  const families = [...new Set(mine.filter((i) => typeOf(i) === 'handOff').map((i) => i.family).filter((f): f is string => !!f))]
  const shown = mine.filter((i) =>
    (component === '' || i.componentKey === component) &&
    (family === '' || i.family === family) &&
    (now.getTime() - new Date(i.since).getTime()) / 3_600_000 >= age.hours)
  const filtered = component !== '' || family !== '' || age.hours > 0

  const approvals = shown.filter((i) => typeOf(i) === 'approval').length
  const handOffs = shown.filter((i) => typeOf(i) === 'handOff').length
  const oldest = shown.reduce<PendingWorkItem | null>((o, i) => (o === null || i.since < o.since ? i : o), null)

  return (
    <div className="space-y-4 px-6 py-5">
      <ToolPageHeader scope={scope} title="Needs your attention" count={pending.data ? mine.length : null} updatedAtMs={pending.dataUpdatedAt}
        onRefresh={() => void client.invalidateQueries({ queryKey: ['pending-work'] })}
        lookingAt={`Everything ${scope === 'all' ? 'on every connected cloud' : `on ${scopeName}`} that is waiting for a person: replays the Agent is holding, and items ServiceHub handed off.`} />

      {pending.isError ? (
        <p role="alert" className="rounded-xl border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800">What needs your attention could not be read. Nothing is shown rather than a list that may be incomplete.</p>
      ) : pending.isPending ? (
        <p role="status" className="text-sm text-[var(--color-text-muted)]">Loading…</p>
      ) : (
        <>
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <Tile icon={<TriangleAlert className="h-5 w-5 text-red-600" aria-hidden="true" />} title="Needs your attention" value={String(shown.length)} note="Approvals + hand-offs to a person" />
            <Tile icon={<ShieldCheck className="h-5 w-5 text-[var(--color-primary-700)]" aria-hidden="true" />} title="Approvals" value={String(approvals)} note="The Agent stopped and asked" />
            <Tile icon={<Hand className="h-5 w-5 text-amber-700" aria-hidden="true" />} title="Hand-offs" value={String(handOffs)} note="ServiceHub stopped; a person decides" />
            <Tile icon={<Hourglass className="h-5 w-5 text-[var(--color-text-muted)]" aria-hidden="true" />} title="Oldest" value={oldest ? formatAge(oldest.since, now) : '—'}
              note={oldest ? `${oldest.dlqMessageId !== null ? `#${oldest.dlqMessageId}` : 'Item'} on ${oldest.entity ?? 'unknown entity'}` : 'Nothing is waiting'} />
          </div>

          <div className="flex flex-wrap items-center gap-3" role="group" aria-label="Filters">
            <Filter label="Component" value={component} onChange={(v) => set('component', v)} all="all">
              {components.map((k) => <option key={k} value={k}>{nameOf(k)}</option>)}
            </Filter>
            <Filter label="Family" value={family} onChange={(v) => set('family', v)} all="all">
              {families.map((f) => <option key={f} value={f}>{familyWords(f)}</option>)}
            </Filter>
            <Filter label="Age" value={age.value === 'any' ? '' : age.value} onChange={(v) => set('age', v)} all="any">
              {ages.filter((a) => a.hours > 0).map((a) => <option key={a.value} value={a.value}>{a.text}</option>)}
            </Filter>
            {filtered && (
              <button type="button" onClick={() => setParams((p) => { const n = new URLSearchParams(p); ['component', 'family', 'age'].forEach((k) => n.delete(k)); return n }, { replace: true })}
                className="text-sm font-semibold text-[var(--color-primary-700)] hover:underline">Clear filters</button>
            )}
          </div>

          <AttentionTable items={shown} coverage={rows} me={me.data} showCloud={scope === 'all'} tab={tab} onTab={(t) => set('type', t === 'all' ? 'all' : t)} />
          <p className="text-xs text-[var(--color-text-muted)]">An approval is a replay the Agent is holding. A hand-off is something ServiceHub will not act on by itself; it always says why and who was told.</p>
        </>
      )}
    </div>
  )
}

function Tile({ icon, title, value, note }: { icon: ReactNode; title: string; value: string; note: string }) {
  return (
    <div className="flex items-center gap-3 rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
      <span className="rounded-full bg-[var(--color-surface-muted)] p-2.5">{icon}</span>
      <div><p className="text-sm font-semibold">{title}</p><p className="text-2xl font-bold">{value}</p><p className="text-xs text-[var(--color-text-muted)]">{note}</p></div>
    </div>
  )
}

function Filter({ label, value, onChange, all, children }: { label: string; value: string; onChange: (v: string) => void; all: string; children: ReactNode }) {
  return (
    <label className="flex items-center gap-2 text-sm">
      <span className="text-[var(--color-text-muted)]">{label}:</span>
      <select value={value} onChange={(e) => onChange(e.target.value)} className="rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-2.5 py-1.5 text-sm">
        <option value="">{all}</option>
        {children}
      </select>
    </label>
  )
}
