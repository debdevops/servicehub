import { Fragment, useState } from 'react'
import { keepPreviousData, useQuery, useQueries } from '@tanstack/react-query'
import { BarChart3, Check, Plus, TriangleAlert } from 'lucide-react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { ExplainerCard, ExplainerToggle } from '../components/explainer/Explainer'
import { useExplainer } from '../components/explainer/useExplainer'
import { Pager } from '../components/ui/Pager'
import { usePageSize } from '../lib/pageSize'
import { HelpLabel, InfoTip } from '../components/ui/InfoTip'
import { columnHelp } from '../content/columns'
import { NamespaceScope } from '../components/provider/NamespaceScope'
import { asCloud, environmentMeta, groupByCloudEnvironment, resolveScope } from '../components/provider/scopeChoice'
import { useProviderScope } from '../components/provider/providerScope'
import { useNamespaces, namespaceKeys } from '../hooks/useNamespaces'
import { fetchFleet, type FleetTopFailure, type FleetHealth, type FleetNamespace, type FleetWindow } from '../lib/api/fleet'
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
  // `?ns=` / `?env=` narrow the whole page — every cloud at once — the same way they do on Home.
  const cloudChoice = asCloud(params.get('cloud'))
  const choice = resolveScope(cloudChoice ? list.filter((n) => n.provider === cloudChoice) : list, params)
  const scoped = choice.ns !== null || choice.env !== null || cloudChoice !== null
  const inScope = new Set(choice.namespaces.map((n) => n.id))
  const rows = fleet.data ? (scoped ? fleet.data.namespaces.filter((r) => inScope.has(r.id)) : fleet.data.namespaces) : []
  const clouds = fleet.data ? (scoped ? fleet.data.clouds.filter((c) => rows.some((r) => r.provider === c.provider)) : fleet.data.clouds) : []
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
          {list.length > 1 && <NamespaceScope namespaces={list} cloud="All clouds" cloudParam="cloud" />}
        </div>
        <label className="ml-auto rounded-[10px] border border-[var(--color-border)] bg-[var(--color-surface)] px-[13px] py-1.5 shadow-[var(--shadow-card)]">
          <span className="block text-[9.5px] font-bold uppercase tracking-[0.6px] text-[var(--color-text-muted)]">Window</span>
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
            {clouds.map((c) => (
              <CloudCard key={c.provider} cloud={c} liveOf={liveOf} rows={rows} window={window} scoped={scoped} />
            ))}
            {!scoped && missing.map((p) => (
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
            <NamespaceTable rows={rows} liveOf={liveOf} />
            <TopFailures failures={scoped ? fleet.data.topFailures.filter((f) => clouds.some((c) => c.provider === f.provider) && (!choice.env && !choice.ns ? true : f.environment === (choice.env ?? choice.ns?.environment))) : fleet.data.topFailures} narrowedToNamespace={choice.ns !== null} />
          </div>
        </div>
      )}
    </section>
  )
}

function CloudCard({
  cloud, rows, liveOf, window, scoped,
}: {
  cloud: import('../lib/api/fleet').FleetCloud
  rows: readonly FleetNamespace[]
  liveOf: (id: string) => number | null | undefined
  window: FleetWindow
  scoped: boolean
}) {
  const { select } = useProviderScope()
  const navigate = useNavigate()
  const mine = rows.filter((r) => r.provider === cloud.provider)
  const counts = mine.map((r) => liveOf(r.id))
  const known = counts.length > 0 && counts.every((v) => typeof v === 'number')
  const total = known ? (counts as number[]).reduce((a, b) => a + b, 0) : null
  // The server counts new/resolved per cloud; under a scope they are re-summed from the namespaces in it.
  const sum = (pick: (r: FleetNamespace) => number) => mine.reduce((a, r) => a + pick(r), 0)
  const newCount = scoped ? sum((r) => r.newInWindow) : cloud.newInWindow
  const resolvedCount = scoped ? sum((r) => r.resolvedInWindow) : cloud.resolvedInWindow
  const label = window === 'today' ? 'today' : window === '24h' ? 'in 24 h' : 'in 7 d'

  return (
    <article className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-5 shadow-[var(--shadow-card)]" aria-label={providerLabel[cloud.provider]}>
      <div className="flex items-center gap-3">
        <span aria-hidden="true" className="flex h-9 w-9 items-center justify-center rounded-[9px] text-sm font-extrabold" style={{ background: `${glyph[cloud.provider]}22`, color: glyph[cloud.provider] }}>
          {providerLabel[cloud.provider].charAt(0)}
        </span>
        <div>
          <h2 className="text-[17px] font-bold leading-tight">{providerLabel[cloud.provider]}</h2>
          <p className="text-[12.5px] text-[var(--color-text-muted)]">{scoped ? mine.length : cloud.namespaceCount} {(scoped ? mine.length : cloud.namespaceCount) === 1 ? 'namespace' : 'namespaces'}</p>
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
        <Figure value={cloud.watched ? newCount : null} caption={`new ${label}`} tone="red" prefix="+" cannot="not watched" help={columnHelp.fleet.new} />
        <Figure value={cloud.watched ? resolvedCount : null} caption={`resolved ${label}`} tone="green" prefix="−" cannot="not watched" help={columnHelp.fleet.resolved} />
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
  const [pageSize, setPageSize] = usePageSize()
  const [requested, setPage] = useState(1)
  // Paged in the order the table is drawn (cloud, environment, worst first), so a page is a run of the table, and a
  // group heading is repeated where a group carries over onto the next page.
  const ordered = groupByCloudEnvironment(rows).flatMap((g) => g.environments.flatMap((e) => e.items))
  const page = Math.min(requested, Math.max(1, Math.ceil(ordered.length / pageSize)))
  const tree = groupByCloudEnvironment(ordered.slice((page - 1) * pageSize, page * pageSize))
  return (
    <section aria-label="Namespaces" className="overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] shadow-[var(--shadow-card)]">
      <div className="flex items-center justify-between px-5 py-4">
        <h2 className="text-[15px] font-bold">Namespaces</h2>
        <span className="text-[12.5px] text-[var(--color-text-muted)]">{rows.length} {rows.length === 1 ? 'namespace' : 'namespaces'} · by cloud, then environment, worst first</span>
      </div>
      <div className="relative overflow-x-auto">
      <table className="w-full min-w-[720px] border-collapse text-left text-[12.5px]">
        <caption className="sr-only">Namespaces, worst first</caption>
        <thead>
          <tr className="border-y border-[var(--color-border)] bg-[var(--color-surface-muted)] text-[10px] uppercase tracking-[0.6px] text-[var(--color-text-muted)]">
            {([['Namespace', columnHelp.fleet.namespace], ['Dead-lettered', columnHelp.fleet.deadLettered], ['New', columnHelp.fleet.new], ['Resolved', columnHelp.fleet.resolved], ['Top failure', columnHelp.fleet.topFailure], ['Health', columnHelp.fleet.health]] as const).map(([h, help]) => (
              <th key={h} scope="col" className="px-4 py-2 font-bold"><HelpLabel help={help}>{h}</HelpLabel></th>
            ))}
            <th scope="col" className="px-4 py-2 font-bold"><span className="sr-only">Open</span></th>
          </tr>
        </thead>
        {tree.map(({ provider, environments }) => {
          const count = environments.reduce((n, e) => n + e.items.length, 0)
          return (
            <tbody key={provider} aria-label={providerLabel[provider]}>
              <tr>
                <th scope="rowgroup" colSpan={7} className="border-b border-[var(--color-border)] bg-[var(--color-primary-50)] px-4 py-2 text-left">
                  <span className="flex items-center gap-2.5">
                    <span aria-hidden="true" className="flex h-6 w-6 items-center justify-center rounded-md text-[11px] font-extrabold" style={{ background: `${glyph[provider]}22`, color: glyph[provider] }}>
                      {providerLabel[provider].charAt(0)}
                    </span>
                    <span className="text-[13.5px] font-bold text-[var(--color-text)]">{providerLabel[provider]}</span>
                    <span className="text-[11.5px] font-normal text-[var(--color-text-muted)]">{count} {count === 1 ? 'namespace' : 'namespaces'}</span>
                  </span>
                </th>
              </tr>
              {environments.map(({ env, items }) => {
                const meta = environmentMeta[env]
                return (
                  <Fragment key={env}>
                    <tr>
                      <th scope="rowgroup" colSpan={7} className="border-b border-[#f3f4f6] bg-[var(--color-surface-muted)] py-1.5 pl-9 pr-4 text-left">
                        <span className="flex items-center gap-2 text-[10.5px] font-bold uppercase tracking-wider text-[var(--color-text-muted)]">
                          <span aria-hidden="true" className="inline-block h-2 w-2 rounded-full" style={{ background: meta.dot }} />
                          {meta.label}
                          <span className={`rounded-full px-2 py-0.5 text-[10px] font-semibold normal-case tracking-normal ${meta.chip}`}>{items.length}</span>
                        </span>
                      </th>
                    </tr>
                    {items.map((r) => {
                      const live = liveOf(r.id)
                      const h = healthWords[r.health]
                      return (
                        <tr key={r.id} className="border-b border-[#f3f4f6]">
                          <td className="py-2.5 pl-9 pr-4 font-mono font-medium">{r.displayName ?? r.name}</td>
                          <td className="tabular px-4 py-2.5 text-[15px] font-bold">{typeof live === 'number' ? live.toLocaleString() : <span className="text-[12px] font-normal text-[var(--color-text-muted)]">can’t count</span>}</td>
                          <td className="tabular px-4 py-2.5 font-bold text-[#b91c1c]">{r.watched ? `+${r.newInWindow}` : <Dash />}</td>
                          <td className="tabular px-4 py-2.5 font-bold text-[#047857]">{r.watched ? `−${r.resolvedInWindow}` : <Dash />}</td>
                          <td className="px-4 py-2.5">{r.topFailure ? <span className="rounded-full bg-[var(--color-error-light)] px-2.5 py-0.5 text-[11px] font-bold text-[#b91c1c]">{r.topFailure.reason}</span> : <Dash />}</td>
                          <td className="px-4 py-2.5"><span className={`rounded-full px-2.5 py-0.5 text-[11px] font-bold ${h.cls}`}>{h.text}</span></td>
                          <td className="px-4 py-2.5">
                            <button type="button" onClick={() => { select(r.provider); navigate(`/?ns=${encodeURIComponent(r.id)}`) }} className="font-semibold text-[var(--color-primary-600)] hover:underline">Open →</button>
                          </td>
                        </tr>
                      )
                    })}
                  </Fragment>
                )
              })}
            </tbody>
          )
        })}
      </table>
      </div>
      <Pager page={page} pageSize={pageSize} total={ordered.length} onPage={setPage} onPageSize={(s) => { setPageSize(s); setPage(1) }} />
    </section>
  )
}

const Dash = () => <span className="font-normal text-[var(--color-text-muted)]" aria-label="not recorded">—</span>

function TopFailures({ failures, narrowedToNamespace }: { failures: readonly FleetTopFailure[]; narrowedToNamespace: boolean }) {
  const max = Math.max(1, ...failures.map((f) => f.count))
  // The same hierarchy as everywhere: cloud, then environment, then the failures in it, deepest first.
  const tree = groupByCloudEnvironment(failures)
  return (
    <section aria-label="Top failures" className="overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] shadow-[var(--shadow-card)]">
      <h2 className="border-b border-[#f3f4f6] px-5 py-4 text-[15px] font-bold">Top failures in dead-letter queues</h2>
      {failures.length === 0 ? (
        <p className="px-5 py-4 text-[13px] text-[var(--color-text-muted)]">Nothing recorded in a dead-letter queue right now.</p>
      ) : (
        <div className="space-y-4 px-5 py-4">
          {tree.map(({ provider, environments }) => (
            <div key={provider}>
              <h3 className="text-[13px] font-bold">{providerLabel[provider]}</h3>
              {environments.map(({ env, items }) => (
                <div key={env} className="mt-2">
                  <h4 className="flex items-center gap-2 text-[10.5px] font-bold uppercase tracking-wider text-[var(--color-text-muted)]">
                    <span aria-hidden="true" className="inline-block h-2 w-2 rounded-full" style={{ background: environmentMeta[env].dot }} />
                    {environmentMeta[env].label}
                  </h4>
                  <ul className="mt-1.5 space-y-2.5">
                    {items.map((f) => (
                      <li key={f.reason}>
                        <div className="flex items-center gap-2 text-[12.5px]">
                          <span className="rounded-full bg-[var(--color-error-light)] px-2.5 py-0.5 text-[11px] font-bold text-[#b91c1c]">{f.reason}</span>
                          <span className="tabular ml-auto font-bold">{f.count.toLocaleString()}</span>
                        </div>
                        <div className="mt-1 h-1.5 rounded-full bg-[#f3f4f6]"><div className="h-1.5 rounded-full bg-[#f87171]" style={{ width: `${(f.count / max) * 100}%` }} /></div>
                      </li>
                    ))}
                  </ul>
                </div>
              ))}
            </div>
          ))}
        </div>
      )}
      <p className="px-5 pb-4 text-[12.5px] text-[var(--color-text-muted)]">What is sitting in dead-letter queues now — one group per cloud and environment, never added across clouds.{narrowedToNamespace && ' Reasons are counted per environment, so they cover the whole environment of the chosen namespace.'}</p>
    </section>
  )
}
