import { keepPreviousData, useQuery, useQueries } from '@tanstack/react-query'
import { BarChart3, Check, Plus, TriangleAlert } from 'lucide-react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { ExplainerCard, ExplainerToggle } from '../components/explainer/Explainer'
import { useExplainer } from '../components/explainer/useExplainer'
import { HelpLabel, InfoTip } from '../components/ui/InfoTip'
import { columnHelp } from '../content/columns'
import { useProviderScope } from '../components/provider/providerScope'
import { useNamespaces, namespaceKeys } from '../hooks/useNamespaces'
import { fetchFleet, type FleetHealth, type FleetNamespace, type FleetWindow } from '../lib/api/fleet'
import { fetchNamespaceStats, type CloudProvider } from '../lib/api/namespaces'
import { connectedProviders, providerLabel } from '../lib/providers'

const windows: readonly { id: FleetWindow; label: string }[] = [
  { id: 'today', label: 'Today' },
  { id: '24h', label: 'Last 24 hours' },
  { id: '7d', label: 'Last 7 days' },
]
const asWindow = (v: string | null): FleetWindow => (v === '24h' || v === '7d' ? v : 'today')
const allProviders: readonly CloudProvider[] = ['azure', 'aws', 'gcp']
const glyph: Record<CloudProvider, string> = { azure: '#0284c7', aws: '#f97316', gcp: '#22c55e' }

const healthWords: Record<FleetHealth, { text: string; cls: string }> = {
  healthy: { text: 'Healthy', cls: 'bg-[var(--color-success-light)] text-[#047857]' },
  needsALook: { text: 'Needs a look', cls: 'bg-[var(--color-warning-light)] text-[#92400e]' },
  cannotTell: { text: "Can't tell", cls: 'bg-[var(--color-surface-muted)] text-[var(--color-text-muted)]' },
}

/**
 * Fleet Overview: every connected cloud side by side, the only screen that shows more than one. Each cloud keeps its own
 * numbers — nothing is added across clouds. What each cloud can prove is read from capability (server-computed), and a
 * cloud ServiceHub does not watch shows "not watched", never a zero that looks like a measurement (R4, R5).
 */
export function FleetPage() {
  const [params, setParams] = useSearchParams()
  const window = asWindow(params.get('window'))
  const explainer = useExplainer('fleet')
  const namespaces = useNamespaces()
  const fleet = useQuery({ queryKey: ['fleet', window], queryFn: () => fetchFleet(window), placeholderData: keepPreviousData })

  // Live counts come from the cloud itself, so they exist even where ServiceHub cannot browse.
  const list = namespaces.data ?? []
  const stats = useQueries({ queries: list.map((n) => ({ queryKey: namespaceKeys.stats(n.id), queryFn: () => fetchNamespaceStats(n.id) })) })
  const liveOf = (id: string): number | null | undefined => stats[list.findIndex((n) => n.id === id)]?.data?.deadLetterMessages

  const connected = connectedProviders(list)
  const missing = allProviders.filter((p) => !connected.some((c) => c.provider === p))

  return (
    <section className="px-[22px] pb-6 pt-5">
      <header className="mb-[18px] flex items-start gap-5">
        <div>
          <h1 className="flex items-center gap-2.5 text-2xl font-extrabold tracking-tight text-[var(--color-text)]">
            <BarChart3 className="h-6 w-6 text-[var(--color-primary-600)]" aria-hidden="true" />
            Fleet Overview <ExplainerToggle visible={!explainer.shown} onShow={explainer.show} />
          </h1>
          <p className="mt-[3px] text-[13px] text-[var(--color-text-muted)]">
            Every cloud you’ve connected, side by side. Each cloud keeps its own numbers — nothing here is added across clouds.
          </p>
        </div>
        <label className="ml-auto rounded-[10px] border border-[var(--color-border)] bg-[var(--color-surface)] px-[13px] py-1.5 shadow-[var(--shadow-card)]">
          <span className="block text-[9.5px] font-bold uppercase tracking-[0.6px] text-[#9ca3af]">Window</span>
          <select
            value={window}
            onChange={(e) => setParams((c) => { const n = new URLSearchParams(c); n.set('window', e.target.value); return n })}
            className="bg-transparent text-[12.5px] font-semibold text-[#1f2937]"
          >
            {windows.map((w) => <option key={w.id} value={w.id}>{w.label}</option>)}
          </select>
        </label>
      </header>

      {explainer.shown && <ExplainerCard id="fleet" onDismiss={explainer.dismiss} />}

      {fleet.isPending && <p role="status" className="text-sm text-[var(--color-text-muted)]">Reading your clouds…</p>}
      {fleet.isError && (
        <div role="alert" className="rounded-xl border border-[var(--color-warning)] bg-[var(--color-warning-light)] p-4 text-sm">
          <p className="flex items-center gap-2 font-semibold"><TriangleAlert className="h-4 w-4" aria-hidden="true" /> ServiceHub couldn’t read the fleet just now.</p>
          <button type="button" onClick={() => void fleet.refetch()} className="mt-2 font-medium text-[var(--color-primary-700)] hover:underline">Try again</button>
        </div>
      )}

      {fleet.data && (
        <div className="space-y-3.5">
          <div className="grid gap-3.5 md:grid-cols-2 xl:grid-cols-3">
            {fleet.data.clouds.map((c) => (
              <CloudCard key={c.provider} cloud={c} liveOf={liveOf} rows={fleet.data.namespaces} window={window} />
            ))}
            {missing.map((p) => (
              <Link
                key={p}
                to="?modal=add-cloud"
                className="flex flex-col items-center justify-center gap-1 rounded-xl border border-dashed border-[#d1d5db] p-6 text-center hover:bg-[var(--color-surface-muted)]"
              >
                <Plus className="h-5 w-5 text-[var(--color-text-muted)]" aria-hidden="true" />
                <span className="text-[15px] font-bold">Add a cloud</span>
                <span className="text-[13px] text-[var(--color-text-muted)]">{providerLabel[p]} isn’t connected.</span>
              </Link>
            ))}
          </div>

          <div className="grid items-start gap-3.5 xl:grid-cols-[1.9fr_1fr]">
            <NamespaceTable rows={fleet.data.namespaces} liveOf={liveOf} />
            <TopFailures failures={fleet.data.topFailures} />
          </div>
        </div>
      )}
    </section>
  )
}

function CloudCard({
  cloud, rows, liveOf, window,
}: {
  cloud: import('../lib/api/fleet').FleetCloud
  rows: readonly FleetNamespace[]
  liveOf: (id: string) => number | null | undefined
  window: FleetWindow
}) {
  const { select } = useProviderScope()
  const navigate = useNavigate()
  const mine = rows.filter((r) => r.provider === cloud.provider)
  const counts = mine.map((r) => liveOf(r.id))
  const known = counts.length > 0 && counts.every((v) => typeof v === 'number')
  const total = known ? (counts as number[]).reduce((a, b) => a + b, 0) : null
  const label = window === 'today' ? 'today' : window === '24h' ? 'in 24 h' : 'in 7 d'

  return (
    <article className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-5 shadow-[var(--shadow-card)]" aria-label={providerLabel[cloud.provider]}>
      <div className="flex items-center gap-3">
        <span aria-hidden="true" className="flex h-9 w-9 items-center justify-center rounded-[9px] text-sm font-extrabold" style={{ background: `${glyph[cloud.provider]}22`, color: glyph[cloud.provider] }}>
          {providerLabel[cloud.provider].charAt(0)}
        </span>
        <div>
          <h2 className="text-[17px] font-bold leading-tight">{providerLabel[cloud.provider]}</h2>
          <p className="text-[12.5px] text-[var(--color-text-muted)]">{cloud.namespaceCount} {cloud.namespaceCount === 1 ? 'namespace' : 'namespaces'}</p>
        </div>
        <button
          type="button"
          onClick={() => { select(cloud.provider); navigate('/') }}
          className="ml-auto text-[13px] font-semibold text-[var(--color-primary-600)] hover:underline"
        >
          Open Home →
        </button>
      </div>

      <div className="mt-4 grid grid-cols-3 gap-3">
        <Figure value={total} caption="dead-lettered" cannot="can’t count here" help={columnHelp.fleet.deadLettered} />
        <Figure value={cloud.watched ? cloud.newInWindow : null} caption={`new ${label}`} tone="red" prefix="+" cannot="not watched" help={columnHelp.fleet.new} />
        <Figure value={cloud.watched ? cloud.resolvedInWindow : null} caption={`resolved ${label}`} tone="green" prefix="−" cannot="not watched" help={columnHelp.fleet.resolved} />
      </div>

      <div className="mt-4 border-t border-[#f3f4f6] pt-3">
        {cloud.capability === 'canConfirm' ? (
          <span className="inline-flex items-center gap-1.5 rounded-full bg-[var(--color-success-light)] px-3 py-1 text-[12px] font-semibold text-[#047857]">
            <Check className="h-3.5 w-3.5" aria-hidden="true" /> Can confirm a fix held
          </span>
        ) : (
          <span className="inline-flex items-center gap-1.5 rounded-full bg-[var(--color-warning-light)] px-3 py-1 text-[12px] font-semibold text-[#92400e]">
            <TriangleAlert className="h-3.5 w-3.5" aria-hidden="true" /> Can’t confirm fixes yet
          </span>
        )}
      </div>
    </article>
  )
}

function Figure({ value, caption, tone, prefix = '', cannot, help }: { value: number | null; caption: string; tone?: 'red' | 'green'; prefix?: string; cannot: string; help?: import('../content/columns').ColumnHelp }) {
  const color = tone === 'red' ? 'text-[#b91c1c]' : tone === 'green' ? 'text-[#047857]' : 'text-[var(--color-text)]'
  return (
    <div>
      {value === null ? (
        <div className="text-[12.5px] font-semibold text-[var(--color-text-muted)]">{cannot}</div>
      ) : (
        <div className={`tabular text-[30px] font-extrabold leading-none tracking-tight ${color}`}>{prefix}{value.toLocaleString()}</div>
      )}
      <div className="mt-1 flex items-center text-[12.5px] text-[var(--color-text-muted)]">{caption}{help && <InfoTip help={help} />}</div>
    </div>
  )
}

function NamespaceTable({ rows, liveOf }: { rows: readonly FleetNamespace[]; liveOf: (id: string) => number | null | undefined }) {
  const { select } = useProviderScope()
  const navigate = useNavigate()
  return (
    <section aria-label="Namespaces" className="overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] shadow-[var(--shadow-card)]">
      <div className="flex items-center justify-between px-5 py-4">
        <h2 className="text-[15px] font-bold">Namespaces</h2>
        <span className="text-[12.5px] text-[var(--color-text-muted)]">{rows.length} {rows.length === 1 ? 'namespace' : 'namespaces'} · sorted by what needs attention</span>
      </div>
      <table className="w-full border-collapse text-left text-[12.5px]">
        <caption className="sr-only">Namespaces, worst first</caption>
        <thead>
          <tr className="border-y border-[var(--color-border)] bg-[var(--color-surface-muted)] text-[10px] uppercase tracking-[0.6px] text-[var(--color-text-muted)]">
            {([['Namespace', columnHelp.fleet.namespace], ['Env', columnHelp.fleet.env], ['Dead-lettered', columnHelp.fleet.deadLettered], ['New', columnHelp.fleet.new], ['Resolved', columnHelp.fleet.resolved], ['Top failure', columnHelp.fleet.topFailure], ['Health', columnHelp.fleet.health]] as const).map(([h, help]) => (
              <th key={h} scope="col" className="px-4 py-2 font-bold"><HelpLabel help={help}>{h}</HelpLabel></th>
            ))}
            <th scope="col" className="px-4 py-2 font-bold"><span className="sr-only">Open</span></th>
          </tr>
        </thead>
        <tbody>
          {rows.map((r) => {
            const live = liveOf(r.id)
            const h = healthWords[r.health]
            return (
              <tr key={r.id} className="border-b border-[#f3f4f6]">
                <td className="px-4 py-2.5 font-mono font-medium">{r.displayName ?? r.name}<span className="ml-2 text-[10.5px] text-[var(--color-text-muted)]">{providerLabel[r.provider]}</span></td>
                <td className="px-4 py-2.5"><span className="rounded-full bg-[var(--color-surface-muted)] px-2.5 py-0.5 text-[11px] font-bold capitalize">{r.environment}</span></td>
                <td className="tabular px-4 py-2.5 text-[15px] font-bold">{typeof live === 'number' ? live.toLocaleString() : <span className="text-[12px] font-normal text-[var(--color-text-muted)]">can’t count</span>}</td>
                <td className="tabular px-4 py-2.5 font-bold text-[#b91c1c]">{r.watched ? `+${r.newInWindow}` : <Dash />}</td>
                <td className="tabular px-4 py-2.5 font-bold text-[#047857]">{r.watched ? `−${r.resolvedInWindow}` : <Dash />}</td>
                <td className="px-4 py-2.5">{r.topFailure ? <span className="rounded-full bg-[var(--color-error-light)] px-2.5 py-0.5 text-[11px] font-bold text-[#b91c1c]">{r.topFailure.reason}</span> : <Dash />}</td>
                <td className="px-4 py-2.5"><span className={`rounded-full px-2.5 py-0.5 text-[11px] font-bold ${h.cls}`}>{h.text}</span></td>
                <td className="px-4 py-2.5">
                  <button type="button" onClick={() => { select(r.provider); navigate('/') }} className="font-semibold text-[var(--color-primary-600)] hover:underline">Open →</button>
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </section>
  )
}

const Dash = () => <span className="font-normal text-[var(--color-text-muted)]" aria-label="not recorded">—</span>

function TopFailures({ failures }: { failures: readonly import('../lib/api/fleet').FleetTopFailure[] }) {
  const max = Math.max(1, ...failures.map((f) => f.count))
  return (
    <section aria-label="Top failures" className="overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] shadow-[var(--shadow-card)]">
      <h2 className="border-b border-[#f3f4f6] px-5 py-4 text-[15px] font-bold">Top failures in dead-letter queues</h2>
      {failures.length === 0 ? (
        <p className="px-5 py-4 text-[13px] text-[var(--color-text-muted)]">Nothing recorded in a dead-letter queue right now.</p>
      ) : (
        <ul className="space-y-3 px-5 py-4">
          {failures.map((f) => (
            <li key={`${f.provider}:${f.reason}`}>
              <div className="flex items-center gap-2 text-[12.5px]">
                <span className="rounded-full bg-[var(--color-error-light)] px-2.5 py-0.5 text-[11px] font-bold text-[#b91c1c]">{f.reason}</span>
                <span className="text-[var(--color-text-muted)]">{providerLabel[f.provider]}</span>
                <span className="tabular ml-auto font-bold">{f.count.toLocaleString()}</span>
              </div>
              <div className="mt-1 h-1.5 rounded-full bg-[#f3f4f6]"><div className="h-1.5 rounded-full bg-[#f87171]" style={{ width: `${(f.count / max) * 100}%` }} /></div>
            </li>
          ))}
        </ul>
      )}
      <p className="px-5 pb-4 text-[12.5px] text-[var(--color-text-muted)]">What is sitting in dead-letter queues now — one row per cloud, never added across clouds.</p>
    </section>
  )
}
