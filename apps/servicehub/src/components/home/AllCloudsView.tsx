import { Fragment } from 'react'
import { useQueries, useQuery } from '@tanstack/react-query'
import { Plus } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { CapabilityLine } from './CapabilityLine'
import { RecordedLine } from './RecordedLine'
import { InfoTip } from '../ui/InfoTip'
import { widgetHelp } from '../../content/widgets'
import { environmentMeta, groupByCloudEnvironment, cloudColor } from '../provider/scopeChoice'
import { useProviderScope } from '../provider/providerScope'
import { useNamespaces, namespaceKeys } from '../../hooks/useNamespaces'
import { useDeadLetters, useLookAtDeadLetters } from '../../hooks/useDeadLetters'
import { fetchDeadLetters } from '../../lib/api/deadLetters'
import { useAudit } from '../../hooks/useIdentity'
import { fetchFleet, type FleetOverview, type FleetTopFailure } from '../../lib/api/fleet'
import { fetchNamespaceStats, type CloudProvider, type Namespace } from '../../lib/api/namespaces'
import { connectedProviders, providerLabel } from '../../lib/providers'
import { traitsOf } from '../../lib/home/traits'
import type { HomeWindow } from '../../lib/home/scope'
import { formatWhen } from '../../lib/format'
import { auditActionWords } from '../../lib/auditWords'

const allProviders: readonly CloudProvider[] = ['azure', 'aws', 'gcp']
const windowLabel: Record<HomeWindow, string> = { '24h': 'in 24 h', '7d': 'in 7 d' }

/**
 * "All clouds" — Home's other level (D48): every namespace you've connected, divided by namespace and
 * grouped by cloud. What Fleet Overview used to be, now one row of this same page instead of a second
 * one. Nothing here is added across clouds (R5) — each cloud card and each group row carries only its
 * own numbers.
 */
export function AllCloudsView({ window, onOpen }: { window: HomeWindow; onOpen: (provider: CloudProvider) => void }) {
  const namespaces = useNamespaces()
  const list = namespaces.data ?? []
  const connected = connectedProviders(list).map((p) => p.provider)
  const missing = allProviders.filter((p) => !connected.includes(p))
  const fleet = useQuery({ queryKey: ['fleet', window], queryFn: () => fetchFleet(window) })

  // Every namespace's live counts, read once here and shared by the cloud cards and the table below —
  // the same `/stats` the fleet endpoint itself has no per-namespace dead-letter figure for.
  const stats = useQueries({ queries: list.map((n) => ({ queryKey: namespaceKeys.stats(n.id), queryFn: () => fetchNamespaceStats(n.id) })) })
  const liveOf = (id: string): { active: number | null; deadLetters: number | null } => {
    const s = stats[list.findIndex((n) => n.id === id)]?.data
    return { active: s?.activeMessages ?? null, deadLetters: s?.deadLetterMessages ?? null }
  }

  return (
    <div className="space-y-3.5">
      <div className={`grid items-stretch gap-3.5 ${connected.length === 3 ? 'md:grid-cols-3' : connected.length === 2 ? 'md:grid-cols-[1fr_1fr_0.6fr]' : 'md:grid-cols-2'}`}>
        {connected.map((p) => (
          <CloudSummaryCard key={p} provider={p} namespaces={list.filter((n) => n.provider === p)} fleet={fleet.data} liveOf={liveOf} window={window} onOpen={onOpen} />
        ))}
        {missing.length > 0 && <ConnectCloudCard missing={missing} />}
      </div>

      <NamespaceByCloudTable namespaces={list} fleet={fleet.data} liveOf={liveOf} window={window} />

      <div className="grid items-start gap-3.5 xl:grid-cols-12">
        <div className="xl:col-span-7"><LatestEverywhere connected={connected} namespaces={list} /></div>
        <div className="xl:col-span-5"><TopFailuresByCloud failures={fleet.data?.topFailures ?? []} /></div>
      </div>

      <ActivityEverywhere />
    </div>
  )
}

function CloudSummaryCard({
  provider, namespaces, fleet, liveOf, window, onOpen,
}: {
  provider: CloudProvider
  namespaces: readonly Namespace[]
  fleet: FleetOverview | undefined
  liveOf: (id: string) => { active: number | null; deadLetters: number | null }
  window: HomeWindow
  onOpen: (provider: CloudProvider) => void
}) {
  const fleetCloud = fleet?.clouds.find((c) => c.provider === provider)
  const traits = traitsOf(namespaces, fleetCloud)
  const counts = namespaces.map((n) => liveOf(n.id).deadLetters)
  const total = counts.length > 0 && counts.every((v) => typeof v === 'number') ? (counts as number[]).reduce((a, b) => a + b, 0) : null
  const recorded = useDeadLetters({ provider, status: 'active', page: 1, pageSize: 1 })

  return (
    <article className="flex flex-col gap-3 rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-4 shadow-[var(--shadow-card)]" aria-label={providerLabel[provider]}>
      <div className="flex items-center gap-3">
        <span aria-hidden="true" className="flex h-9 w-9 shrink-0 items-center justify-center rounded-[9px] text-sm font-extrabold" style={{ background: `${cloudColor[provider]}22`, color: cloudColor[provider] }}>
          {providerLabel[provider].charAt(0)}
        </span>
        <div className="min-w-0">
          <h2 className="text-[15px] font-bold leading-tight">{providerLabel[provider]}</h2>
          <p className="text-[11.5px] text-[var(--color-text-muted)]">{namespaces.length} {namespaces.length === 1 ? 'namespace' : 'namespaces'}</p>
        </div>
        <span className="ml-auto inline-flex items-center gap-1.5 rounded-full bg-[var(--color-success-light)] px-2.5 py-0.5 text-[10.5px] font-bold text-[#047857]">Connected</span>
      </div>

      <div className="flex items-end gap-5">
        <Fig value={total} label="dead letters now" cannot="can’t count here" />
        {traits.watched ? (
          <>
            <Fig value={fleetCloud?.newInWindow ?? null} tone="red" prefix="+" label={`new ${windowLabel[window]}`} cannot="not watched" />
            <Fig value={fleetCloud?.resolvedInWindow ?? null} tone="green" prefix="−" label={`resolved ${windowLabel[window]}`} cannot="not watched" />
          </>
        ) : (
          <div className="text-[12px] font-semibold text-[var(--color-text-muted)]">
            <RecordedLine total={recorded.data?.paging.total ?? 0} newestIso={recorded.data?.items[0]?.detectedAtUtc ?? null} />
          </div>
        )}
      </div>

      <CapabilityLine traits={traits} />

      <div className="mt-auto flex items-center justify-between border-t border-[var(--color-border)] pt-2.5 text-[12px]">
        {traits.confirms ? (
          <span className="text-[var(--color-text-muted)]">every feature available</span>
        ) : (
          <a href={`?panel=help&topic=verification-required`} className="font-semibold text-[var(--color-primary-600)] hover:underline">Why the differences? ›</a>
        )}
        <button type="button" onClick={() => onOpen(provider)} className="font-semibold text-[var(--color-primary-600)] hover:underline">Open {providerLabel[provider]} →</button>
      </div>
    </article>
  )
}

function Fig({ value, label, tone, prefix = '', cannot }: { value: number | null; label: string; tone?: 'red' | 'green'; prefix?: string; cannot: string }) {
  const color = tone === 'red' ? 'text-[#b91c1c]' : tone === 'green' ? 'text-[#047857]' : 'text-[var(--color-text)]'
  return (
    <div>
      {value === null ? (
        <div className="text-[12px] font-semibold text-[var(--color-text-muted)]">{cannot}</div>
      ) : (
        <div className={`tabular text-[24px] font-extrabold leading-none tracking-tight ${color}`}>{prefix}{value.toLocaleString()}</div>
      )}
      <div className="mt-1 text-[10.5px] text-[var(--color-text-muted)]">{label}</div>
    </div>
  )
}

function ConnectCloudCard({ missing }: { missing: readonly CloudProvider[] }) {
  return (
    <div className="flex flex-col items-center justify-center gap-2 rounded-xl border border-dashed border-[#d1d5db] p-4 text-center">
      <span className="flex h-8 w-8 items-center justify-center rounded-full bg-[var(--color-primary-100)] text-[var(--color-primary-600)]"><Plus className="h-4 w-4" aria-hidden="true" /></span>
      <p className="text-[13px] font-bold">Connect another cloud</p>
      <p className="text-[11.5px] text-[var(--color-text-muted)]">See it here beside the others.</p>
      <div className="flex flex-wrap justify-center gap-2">
        {missing.map((p) => (
          <a key={p} href={`?modal=add-cloud&cloud=${p}`} className="rounded-lg border border-[var(--color-border)] px-2.5 py-1 text-[12px] font-semibold text-[var(--color-primary-700)] hover:bg-[var(--color-surface-muted)]">
            Connect {providerLabel[p]}
          </a>
        ))}
      </div>
    </div>
  )
}

function NamespaceByCloudTable({
  namespaces, fleet, liveOf, window,
}: {
  namespaces: readonly Namespace[]
  fleet: FleetOverview | undefined
  liveOf: (id: string) => { active: number | null; deadLetters: number | null }
  window: HomeWindow
}) {
  const navigate = useNavigate()
  const { select } = useProviderScope()
  const look = useLookAtDeadLetters()
  const byId = new Map(namespaces.map((n) => [n.id, n]))
  const fleetRows = fleet?.namespaces ?? []
  const tree = groupByCloudEnvironment(fleetRows)

  const openNamespace = (provider: CloudProvider, id: string) => {
    select(provider)
    navigate(`/?provider=${provider}&ns=${encodeURIComponent(id)}`)
  }

  return (
    <div className="overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] shadow-[var(--shadow-card)]">
      <div className="flex items-center justify-between px-4 py-3">
        <h2 className="flex items-center text-[13.5px] font-bold text-[#1f2937]">Every namespace, by cloud<InfoTip help={widgetHelp.fleetNs} /></h2>
        <span className="text-[11px] text-[var(--color-text-muted)]">worst first inside each cloud</span>
      </div>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[860px] border-collapse text-left text-[12.5px]">
          <caption className="sr-only">Every namespace, worst first inside its cloud</caption>
          <thead>
            <tr className="border-y border-[var(--color-border)] bg-[var(--color-surface-muted)] text-[10px] uppercase tracking-[0.6px] text-[var(--color-text-muted)]">
              <th scope="col" className="px-4 py-2 font-bold">Namespace</th>
              <th scope="col" className="px-4 py-2 font-bold">Environment</th>
              <th scope="col" className="px-4 py-2 font-bold">Dead letters</th>
              <th scope="col" className="px-4 py-2 font-bold">Active</th>
              <th scope="col" className="px-4 py-2 font-bold">New / resolved · {window}</th>
              <th scope="col" className="px-4 py-2 font-bold">Top failure</th>
              <th scope="col" className="px-4 py-2 font-bold">How ServiceHub sees it</th>
              <th scope="col" className="px-4 py-2 font-bold">Health</th>
              <th scope="col" className="px-4 py-2 font-bold"><span className="sr-only">Open</span></th>
            </tr>
          </thead>
          {tree.map(({ provider, environments }) => {
            const count = environments.reduce((n, e) => n + e.items.length, 0)
            const fleetCloud = fleet?.clouds.find((c) => c.provider === provider)
            const cloudTraits = traitsOf(namespaces.filter((n) => n.provider === provider), fleetCloud)
            return (
              <tbody key={provider} aria-label={providerLabel[provider]}>
                <tr>
                  <th scope="rowgroup" colSpan={9} className="border-b border-[var(--color-border)] bg-[var(--color-primary-50)] px-4 py-2 text-left">
                    <span className="flex items-center gap-2.5">
                      <span aria-hidden="true" className="flex h-6 w-6 items-center justify-center rounded-md text-[11px] font-extrabold" style={{ background: `${cloudColor[provider]}22`, color: cloudColor[provider] }}>
                        {providerLabel[provider].charAt(0)}
                      </span>
                      <span className="text-[13.5px] font-bold text-[var(--color-text)]">{providerLabel[provider]}</span>
                      <span className="text-[11.5px] font-normal text-[var(--color-text-muted)]">{count} {count === 1 ? 'namespace' : 'namespaces'}</span>
                      <span className="ml-auto text-[11px] font-normal normal-case text-[var(--color-text-muted)]">
                        {cloudTraits.watched ? 'watched automatically' : 'recorded when you look'} · {cloudTraits.confirms ? 'confirms a fix held' : 'can’t confirm fixes yet'}
                      </span>
                    </span>
                  </th>
                </tr>
                {environments.map(({ env, items }) => {
                  const meta = environmentMeta[env]
                  return (
                    <Fragment key={env}>
                      <tr>
                        <th scope="rowgroup" colSpan={9} className="border-b border-[#f3f4f6] bg-[var(--color-surface-muted)] py-1.5 pl-9 pr-4 text-left">
                          <span className="flex items-center gap-2 text-[10.5px] font-bold uppercase tracking-wider text-[var(--color-text-muted)]">
                            <span aria-hidden="true" className="inline-block h-2 w-2 rounded-full" style={{ background: meta.dot }} />
                            {meta.label}
                            <span className={`rounded-full px-2 py-0.5 text-[10px] font-semibold normal-case tracking-normal ${meta.chip}`}>{items.length}</span>
                          </span>
                        </th>
                      </tr>
                      {items.map((r) => {
                        const ns = byId.get(r.id)
                        const live = liveOf(r.id)
                        const h = healthWords[r.health]
                        return (
                          <tr key={r.id} className="border-b border-[#f3f4f6]">
                            <td className="whitespace-nowrap py-2.5 pl-9 pr-4">
                              <span className="font-mono font-medium">{r.displayName ?? r.name}</span>
                              <div className="text-[11px] text-[var(--color-text-muted)]">{ns?.awsRegion ?? ns?.gcpProjectId ?? ''}</div>
                            </td>
                            <td className="px-4 py-2.5"><span className={`rounded-full px-2 py-0.5 text-[10.5px] font-semibold ${meta.chip}`}>{meta.label}</span></td>
                            <td className="tabular px-4 py-2.5 text-[13px] font-bold">{typeof live.deadLetters === 'number' ? live.deadLetters.toLocaleString() : <span className="text-[11px] font-normal text-[var(--color-text-muted)]">can’t count</span>}</td>
                            <td className="tabular px-4 py-2.5">{typeof live.active === 'number' ? live.active.toLocaleString() : <span className="text-[11px] font-normal text-[var(--color-text-muted)]">can’t count</span>}</td>
                            <td className="tabular px-4 py-2.5">{r.watched ? <><span className="font-bold text-[#b91c1c]">+{r.newInWindow}</span> <span className="text-[var(--color-text-muted)]">/</span> <span className="font-bold text-[#047857]">−{r.resolvedInWindow}</span></> : <span className="text-[11px] font-normal text-[var(--color-text-muted)]">not watched</span>}</td>
                            <td className="px-4 py-2.5">{r.topFailure ? <span className="rounded-full bg-[var(--color-error-light)] px-2.5 py-0.5 text-[11px] font-bold text-[#b91c1c]">{r.topFailure.reason}</span> : <span className="text-[var(--color-text-muted)]">—</span>}</td>
                            <td className="px-4 py-2.5 text-[11.5px] text-[var(--color-text-muted)]">{r.watched ? 'Watched automatically · every 10 s' : 'Recorded when you look'}</td>
                            <td className="px-4 py-2.5"><span className={`rounded-full px-2.5 py-0.5 text-[11px] font-bold ${h.cls}`}>{h.text}</span></td>
                            <td className="whitespace-nowrap px-4 py-2.5 text-right">
                              {!r.watched && (
                                <button type="button" disabled={look.isPending} onClick={() => look.mutate([r.id])} className="mr-3 font-semibold text-[var(--color-primary-600)] hover:underline disabled:opacity-60">
                                  {look.isPending ? 'Looking…' : 'Look now'}
                                </button>
                              )}
                              <button type="button" onClick={() => openNamespace(r.provider, r.id)} className="font-semibold text-[var(--color-primary-600)] hover:underline">Open →</button>
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
    </div>
  )
}

const healthWords: Record<'healthy' | 'needsALook' | 'cannotTell', { text: string; cls: string }> = {
  healthy: { text: 'Healthy', cls: 'bg-[var(--color-success-light)] text-[#047857]' },
  needsALook: { text: 'Needs a look', cls: 'bg-[var(--color-warning-light)] text-[#92400e]' },
  cannotTell: { text: "Can't tell", cls: 'bg-[var(--color-surface-muted)] text-[var(--color-text-muted)]' },
}

function LatestEverywhere({ connected, namespaces }: { connected: readonly CloudProvider[]; namespaces: readonly Namespace[] }) {
  // `/dead-letters` requires a provider or a namespaceId (it 400s with neither) — there is no
  // cross-cloud query to make. One request per connected cloud instead, merged and re-sorted here.
  const perCloud = useQueries({ queries: connected.map((p) => ({ queryKey: ['dead-letters', 'list', { provider: p, status: 'active', page: 1, pageSize: 5 }], queryFn: () => fetchDeadLetters({ provider: p, status: 'active', page: 1, pageSize: 5 }) })) })
  const loaded = perCloud.filter((q) => q.isSuccess)
  const items = loaded.flatMap((q) => q.data!.items).sort((a, b) => b.detectedAtUtc.localeCompare(a.detectedAtUtc)).slice(0, 5)
  const nameOf = new Map(namespaces.map((n) => [n.id, n]))
  const anyError = perCloud.some((q) => q.isError)
  if (anyError && loaded.length === 0) return <p role="alert" className="text-sm">ServiceHub couldn’t read the latest dead letters. <button type="button" onClick={() => perCloud.forEach((q) => void q.refetch())} className="font-medium text-[var(--color-primary-700)] hover:underline">Try again</button></p>
  if (loaded.length < connected.length) return <p role="status" className="text-sm text-[var(--color-text-muted)]">Reading every cloud…</p>
  if (items.length === 0) return null
  return (
    <section aria-label="Latest dead letters, every cloud" className="h-full rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
      <div className="flex items-center justify-between px-4 py-3">
        <h2 className="flex items-center text-sm font-semibold text-[var(--color-text)]">Latest dead letters, every cloud<InfoTip help={widgetHelp.latest} /></h2>
        <a href="?tab=dlq" className="text-sm font-medium text-[var(--color-primary-700)] hover:underline">See dead letters →</a>
      </div>
      <table className="w-full border-collapse text-left text-[12.5px]">
        <caption className="sr-only">The five most recent dead letters across every cloud</caption>
        <thead>
          <tr className="border-y border-[var(--color-border)] bg-[var(--color-surface-muted)] text-[10px] uppercase tracking-[0.6px] text-[var(--color-text-muted)]">
            <th scope="col" className="px-4 py-2 font-bold">When</th>
            <th scope="col" className="px-4 py-2 font-bold">Queue or topic</th>
            <th scope="col" className="px-4 py-2 font-bold">Failed because</th>
            <th scope="col" className="px-4 py-2 font-bold"><span className="sr-only">Details</span></th>
          </tr>
        </thead>
        <tbody>
          {items.map((m) => {
            const ns = nameOf.get(m.namespaceId)
            return (
              <tr key={m.id} className="border-b border-[#f3f4f6] last:border-b-0">
                <td className="px-4 py-2.5 text-[var(--color-text-muted)]">{formatWhen(m.detectedAtUtc, new Date())}</td>
                <td className="px-4 py-2.5">
                  <span className="inline-flex items-center gap-1.5 font-mono font-medium">
                    {ns && <span aria-hidden="true" className="inline-block h-4 w-4 rounded text-center text-[9px] font-extrabold leading-4" style={{ background: `${cloudColor[ns.provider]}22`, color: cloudColor[ns.provider] }}>{providerLabel[ns.provider].charAt(0)}</span>}
                    {m.entityName}
                  </span>
                  {ns && <div className="text-[11px] text-[var(--color-text-muted)]">{providerLabel[ns.provider]} · {ns.displayName ?? ns.name}</div>}
                </td>
                <td className="px-4 py-2.5">{m.deadLetterReason ? <span className="rounded-full bg-[var(--color-error-light)] px-2.5 py-0.5 text-[11px] font-bold text-[#b91c1c]">{m.deadLetterReason}</span> : <span className="text-[var(--color-text-muted)]">Reason not recorded</span>}</td>
                <td className="px-4 py-2.5 text-right"><a href={`?tab=dlq&message=${m.id}`} className="font-semibold text-[var(--color-primary-600)] hover:underline">Details →</a></td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </section>
  )
}

function TopFailuresByCloud({ failures }: { failures: readonly FleetTopFailure[] }) {
  const tree = groupByCloudEnvironment(failures)
  return (
    <section aria-label="Top failures, per cloud" className="h-full overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] shadow-[var(--shadow-card)]">
      <h2 className="border-b border-[#f3f4f6] px-5 py-4 text-[15px] font-bold"><span className="inline-flex items-center">Top failures, per cloud<InfoTip help={widgetHelp.fleetFail} /></span></h2>
      {failures.length === 0 ? (
        <p className="px-5 py-4 text-[13px] text-[var(--color-text-muted)]">Nothing recorded in a dead-letter queue right now.</p>
      ) : (
        <div className="space-y-4 px-5 py-4">
          {tree.map(({ provider, environments }) => (
            <div key={provider}>
              <h3 className="flex items-center gap-2 text-[13px] font-bold"><span aria-hidden="true" className="inline-block h-2 w-2 rounded-full" style={{ background: cloudColor[provider] }} />{providerLabel[provider]}</h3>
              {environments.map(({ env, items }) => (
                <div key={env} className="mt-1.5">
                  <h4 className="text-[10.5px] font-bold uppercase tracking-wider text-[var(--color-text-muted)]">{environmentMeta[env].label}</h4>
                  <ul className="mt-1 space-y-1.5">
                    {items.slice(0, 2).map((f) => (
                      <li key={f.reason} className="flex items-center gap-2 text-[12px]">
                        <span className="rounded-full bg-[var(--color-error-light)] px-2 py-0.5 text-[10.5px] font-bold text-[#b91c1c]">{f.reason}</span>
                        <span className="tabular ml-auto font-bold">{f.count.toLocaleString()}</span>
                      </li>
                    ))}
                  </ul>
                </div>
              ))}
            </div>
          ))}
        </div>
      )}
      <p className="border-t border-[#f3f4f6] px-5 py-3 text-[11.5px] text-[var(--color-text-muted)]">Never added across clouds — each group is one cloud's own reasons.</p>
    </section>
  )
}

function ActivityEverywhere() {
  const { data, isError } = useAudit({ pageSize: 5 })
  if (isError || !data || data.items.length === 0) return null
  return (
    <section aria-label="Recent activity, every cloud" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] px-4 py-3">
      <h2 className="mb-1 text-[13.5px] font-bold text-[#1f2937]">Recent activity, every cloud</h2>
      <ul className="divide-y divide-[var(--color-border)]">
        {data.items.map((a) => (
          <li key={a.id} className="flex items-center gap-3 py-1.5 text-[12.5px]">
            {a.cloudProvider && (
              <span aria-hidden="true" className="flex h-5 w-5 shrink-0 items-center justify-center rounded text-[10px] font-extrabold" style={{ background: `${cloudColor[a.cloudProvider.toLowerCase() as CloudProvider] ?? '#9ca3af'}22`, color: cloudColor[a.cloudProvider.toLowerCase() as CloudProvider] ?? '#6b7280' }}>
                {a.cloudProvider.charAt(0)}
              </span>
            )}
            <span className="min-w-0 flex-1 truncate">{auditActionWords[a.action] ?? a.action}{a.resourceName ? ` · ${a.resourceName}` : ''}{a.namespaceName ? ` · ${a.namespaceName}` : ''}</span>
            <time dateTime={a.timestamp} className="shrink-0 text-xs text-[var(--color-text-muted)]">{formatWhen(a.timestamp, new Date())}</time>
          </li>
        ))}
      </ul>
    </section>
  )
}
