import { useQueries, useQuery, useQueryClient } from '@tanstack/react-query'
import { Fingerprint, Search, TrendingUp, ThumbsDown, ThumbsUp } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, useLocation, useParams, useSearchParams } from 'react-router-dom'
import { Detail, type SignatureLinks } from '../../components/advanced/SignatureDetail'
import { TraceView, type StoryLinks } from '../../components/advanced/SignatureStory'
import { Pager } from '../../components/ui/Pager'
import { useComponents } from '../../hooks/useComponents'
import { fetchSignatures, fetchSignatureTrust, type Signature, type SignatureTab, type SignatureTrust } from '../../lib/api/signatures'
import { usePageSize } from '../../lib/pageSize'
import { replayWords } from '../../lib/signatureWords'
import { trustWords } from '../../lib/trustWords'
import { reasonParam } from '../../lib/urlState'
import { ToolPageHeader } from '../components/ToolPageHeader'
import { cloudLabel, cloudOf, parseScope } from '../lib/clouds'
import { nextEntry, nextHref } from '../nav/navigation'
import { Th } from '../components/Tip'
import { StatTile as Tile } from '../components/StatTile'
import { Tip } from '../components/Tip'

const td = 'px-3 py-2.5 align-top text-sm'
const field = 'rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-2.5 py-1.5 text-sm'
const keyOf = (s: Signature) => `${s.provider}:${s.signatureHash}`
const asTab = (v: string | null): SignatureTab => (v === 'growing' || v === 'helps' || v === 'doesnt' ? v : 'all')
const asDays = (v: string | null) => (v === '14' ? 14 : v === '30' ? 30 : 7)

/** The trust ladder as the server reports it. The server returns these three levels only; none is invented here. */
const levelOf = (t: SignatureTrust) => (t.level === 'unattended' ? { text: 'L5 · Unattended', chip: 'bg-green-50 text-green-800' } : t.level === 'standing' ? { text: 'L4 · Standing', chip: 'bg-green-50 text-green-800' } : { text: 'L3 · Asks first', chip: 'bg-slate-100 text-slate-700' })
const answerChip = { yes: 'bg-green-50 text-green-800', 'not yet': 'bg-amber-50 text-amber-800', no: 'bg-red-50 text-red-700' } as const
const answerLabel = { yes: 'Yes', 'not yet': 'Not yet', no: 'No' } as const

/**
 * Failure signatures (build-plan unit E6; design `p11-failure-signatures`): each kind of failure on one cloud or all of them — is it
 * still happening, does replaying it help, and may the Agent fix it alone? Read-only: it calls no mutating endpoint (a rule is made on
 * Auto Replay). The page and its detail are the 4.2.0 Advanced ones, moved (`SignatureDetail`), with this shell's addresses. Every
 * number comes from the signatures read and each row's trust report (counted from recorded outcomes); nothing is guessed.
 */
export default function SignaturesPage() {
  const scope = parseScope(useParams().cloud)
  const cloud = cloudOf(scope)
  const client = useQueryClient()
  const { pathname } = useLocation()
  const [params, setParams] = useSearchParams()
  const [pageSize, setPageSize] = usePageSize()
  const components = useComponents(cloud)

  const tab = asTab(params.get('tab'))
  const days = asDays(params.get('days'))
  const sort = params.get('sort') === 'recent' ? 'recent' : 'messages'
  const page = Math.max(1, Number(params.get('page')) || 1)
  const namespaceId = params.get('connection') ?? undefined
  const q = params.get('q') ?? ''
  const selectedKey = params.get('signature')

  const change = (patch: Record<string, string | null>) => setParams((current) => {
    const next = new URLSearchParams(current)
    for (const [k, v] of Object.entries(patch)) { if (v === null || v === '') next.delete(k); else next.set(k, v) }
    if (!('page' in patch) && !('signature' in patch)) next.delete('page')
    return next
  }, { replace: true })

  // The search waits for a pause in typing, so each keystroke is not a request.
  const [draft, setDraft] = useState(q)
  useEffect(() => { const t = setTimeout(() => { if (draft !== q) change({ q: draft || null }) }, 350); return () => clearTimeout(t) }, [draft]) // eslint-disable-line react-hooks/exhaustive-deps

  const list = useQuery({
    queryKey: ['next', 'signatures-page', cloud, namespaceId ?? null, days, tab, sort, page, pageSize, q],
    queryFn: () => fetchSignatures({ provider: cloud ?? undefined, namespaceId, days, tab, sort, page, pageSize, q: q || undefined }),
  })
  const data = list.data
  const rows = data?.items ?? []
  const trust = useQueries({ queries: rows.map((s) => ({ queryKey: ['signatures', 'trust', s.signatureHash, s.provider], queryFn: () => fetchSignatureTrust(s.signatureHash, s.provider) })) })
  const selected = rows.find((s) => keyOf(s) === selectedKey) ?? null

  const connections = [...new Map((components.data?.components ?? []).map((c) => [c.namespaceId, c.name])).entries()]
  const nameOfNs = (id: string) => connections.find(([i]) => i === id)?.[1]
  const now = new Date()
  const filtering = tab !== 'all' || !!namespaceId || !!q

  // Where the detail sends you, in this shell: the connection that holds the messages, the ledger (E7), and the rule form.
  // The Incident tab and Trace a message link to evidence in this shell's ledger and to a message in its Message dialog.
  const story: StoryLinks = {
    evidence: (entryId) => `${nextHref(nextEntry('ledger'), { cloud: cloud ?? 'all' })}?entry=${encodeURIComponent(entryId)}&window=all`,
    message: (_namespaceId, dlqMessageId) => `${pathname}?${new URLSearchParams({ ...Object.fromEntries(params), dialog: 'message', id: String(dlqMessageId) })}`,
  }
  const tracing = params.get('view') === 'trace'
  const links: SignatureLinks = {
    incident: true,
    story,
    ledger: (s) => nextHref(nextEntry('ledger'), { cloud: s.provider }),
    rule: (s) => `${nextHref(nextEntry('rules'), { cloud: s.provider })}?rule=${encodeURIComponent(s.signatureHash)}`,
    messages: (s) => {
      const ns = s.namespaces[0]
      const all = components.data?.components ?? []
      const c = ns ? (all.find((x) => x.namespaceId === ns.id && x.entities.some((e) => s.entities.includes(e.name))) ?? all.find((x) => x.namespaceId === ns.id)) : undefined
      return c ? `${nextHref(nextEntry('component-detail'), { cloud: c.cloud, component: c.componentKey, namespaceId: c.namespaceId })}?tab=dead-letters&reason=${encodeURIComponent(reasonParam(s.reason))}` : null
    },
  }

  const tabs: { id: SignatureTab; label: string; n: number | undefined }[] = [
    { id: 'all', label: 'All', n: data?.all }, { id: 'growing', label: 'Growing', n: data?.growing },
    { id: 'helps', label: 'Replay helps', n: data?.replayHelps }, { id: 'doesnt', label: 'Replay doesn’t help', n: data?.replayDoesNotHelp },
  ]

  return (
    <div className="space-y-4 px-6 py-5">
      <ToolPageHeader scope={scope} title="Failure signatures" updatedAtMs={list.dataUpdatedAt} onRefresh={() => void client.invalidateQueries({ queryKey: ['next', 'signatures-page'] })}
        lookingAt={`Each kind of failure ServiceHub has seen ${scope === 'all' ? 'on every connected cloud' : `on ${cloudLabel[scope]}`}: is it still happening, does replaying it help, and may the Agent fix it alone?`} />

      <div role="radiogroup" aria-label="Page view" className="flex w-fit rounded-lg bg-[var(--color-surface-muted)] p-0.5 text-sm">
        {([['list', 'Signatures'], ['trace', 'Trace a message']] as const).map(([id, label]) => (
          <button key={id} type="button" role="radio" aria-checked={(id === 'trace') === tracing} onClick={() => change({ view: id === 'trace' ? 'trace' : null, signature: null })}
            className={`rounded-md px-3 py-1 font-semibold ${(id === 'trace') === tracing ? 'bg-[var(--color-surface)] shadow-sm' : 'text-[var(--color-text-muted)]'}`}>{label}</button>
        ))}
      </div>

      {tracing ? (
        <TraceView links={story} />
      ) : list.isError ? (
        <p role="alert" className="rounded-xl border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800">Failure signatures could not be read. Nothing is shown rather than a list that may be incomplete.</p>
      ) : list.isPending ? (
        <p role="status" className="text-sm text-[var(--color-text-muted)]">Loading…</p>
      ) : (
        <>
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <Tile icon={<Fingerprint className="h-5 w-5 text-[var(--color-primary-700)]" aria-hidden="true" />} title="Signatures" note={`Kinds of failure, last ${days} days`}>{(data?.all ?? 0).toLocaleString('en-US')}</Tile>
            <Tile icon={<TrendingUp className="h-5 w-5 text-red-600" aria-hidden="true" />} title="Growing" note="Getting worse">{(data?.growing ?? 0).toLocaleString('en-US')}</Tile>
            <Tile icon={<ThumbsUp className="h-5 w-5 text-green-700" aria-hidden="true" />} title="Replay helps" note="At least half of checked replays stayed fixed">{(data?.replayHelps ?? 0).toLocaleString('en-US')}</Tile>
            <Tile icon={<ThumbsDown className="h-5 w-5 text-amber-700" aria-hidden="true" />} title="Replay doesn’t help" note="Mostly fail again">{(data?.replayDoesNotHelp ?? 0).toLocaleString('en-US')}</Tile>
          </div>

          <div role="group" aria-label="Filters" className="flex flex-wrap items-center gap-3">
            <label className="flex items-center gap-2 text-sm"><span className="text-[var(--color-text-muted)]">Connection:</span>
              <select className={field} value={namespaceId ?? ''} onChange={(e) => change({ connection: e.target.value || null })}>
                <option value="">all</option>{connections.map(([id, name]) => <option key={id} value={id}>{name}</option>)}
              </select>
            </label><Tip label="Connection:" />
            <label className="flex items-center gap-2 text-sm"><span className="text-[var(--color-text-muted)]">Window:</span>
              <select className={field} value={String(days)} onChange={(e) => change({ days: e.target.value === '7' ? null : e.target.value })}>
                <option value="7">last 7 days</option><option value="14">last 14 days</option><option value="30">last 30 days</option>
              </select>
            </label><Tip label="Window:" />
            <label className="flex items-center gap-2 text-sm"><span className="text-[var(--color-text-muted)]">Sort:</span>
              <select className={field} value={sort} onChange={(e) => change({ sort: e.target.value === 'messages' ? null : e.target.value })}>
                <option value="messages">most messages</option><option value="recent">most recent</option>
              </select>
            </label><Tip label="Sort:" />
            <label className="relative block">
              <span className="sr-only">Search signatures</span>
              <Search className="pointer-events-none absolute left-2.5 top-2 h-4 w-4 text-[var(--color-text-muted)]" aria-hidden="true" />
              <input type="search" data-shortcut="filter" value={draft} onChange={(e) => setDraft(e.target.value)} placeholder="Reason, error or queue…" className={`${field} w-64 pl-8`} />
            </label>
            <Tip label="Search" />
            {filtering && <button type="button" onClick={() => { setDraft(''); change({ tab: null, connection: null, q: null }) }} className="text-sm font-semibold text-[var(--color-primary-700)]">Clear filters</button>}
          </div>

          <div className="flex items-start gap-4">
            <section aria-labelledby="sigs-title" className="min-w-0 flex-1 rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
              <h2 id="sigs-title" className="px-4 pt-4 text-base font-semibold">Signatures<Tip label="section:Signatures" /></h2>
              <div role="tablist" aria-label="Signature views" className="flex gap-5 border-b border-[var(--color-border)] px-4">
                {tabs.map((t) => (
                  <button key={t.id} role="tab" type="button" aria-selected={tab === t.id} onClick={() => change({ tab: t.id === 'all' ? null : t.id })}
                    className={`-mb-px border-b-2 px-1 py-2.5 text-sm font-semibold ${tab === t.id ? 'border-[var(--color-primary-600)] text-[var(--color-primary-700)]' : 'border-transparent text-[var(--color-text-muted)]'}`}>{t.label} ({t.n ?? 0})</button>
                ))}
              </div>
              <Tip label="Tabs" />
              {rows.length === 0 ? (
                <p className="px-4 py-6 text-sm text-[var(--color-text-muted)]">{filtering ? 'Nothing matches these filters.' : 'No failure has been recorded yet. ServiceHub records failures where it can look on its own; a cloud it does not watch has none to group until you press Look now.'}</p>
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full">
                    <caption className="sr-only">Failure signatures</caption>
                    <thead>
                      <tr>
                        {scope === 'all' && <Th>Cloud</Th>}
                        <Th>Signature</Th><Th>Connection</Th><Th>Still in queue</Th>
                        <Th>Does replay help?</Th><Th>Level</Th><Th>Agent may act</Th><Th>Why</Th><Th>Actions</Th>
                      </tr>
                    </thead>
                    <tbody>
                      {rows.map((s, i) => {
                        const t = trust[i]?.data
                        const words = t ? trustWords(t, cloudLabel[s.provider]) : null
                        const level = t ? levelOf(t) : null
                        return (
                          <tr key={keyOf(s)} aria-selected={keyOf(s) === selectedKey} className={`border-t border-[var(--color-border)] ${keyOf(s) === selectedKey ? 'bg-[var(--color-primary-50)]' : ''}`}>
                            {scope === 'all' && <td className={td}>{cloudLabel[s.provider]}</td>}
                            <td className={`${td} font-semibold`}>{s.reason}{s.growing && <span className="ml-2 rounded-md bg-red-50 px-1.5 py-0.5 text-[10px] font-bold text-red-700">Growing</span>}
                              <span className="block max-w-xs truncate text-xs font-normal text-[var(--color-text-muted)]">{s.exampleError ?? s.entities.join(' · ')}</span></td>
                            <td className={td}>{s.namespaces.map((n) => nameOfNs(n.id) ?? n.displayName ?? n.name).join(', ') || '—'}</td>
                            <td className={td}>{s.activeNow.toLocaleString('en-US')}</td>
                            <td className={td}>{replayWords(s)}</td>
                            <td className={td}>{level ? <span className={`inline-block rounded-md px-2 py-0.5 text-xs font-semibold ${level.chip}`}>{level.text}</span> : '…'}</td>
                            <td className={td}>{words ? <span className={`inline-block rounded-md px-2 py-0.5 text-xs font-semibold ${answerChip[words.answer]}`}>{answerLabel[words.answer]}</span> : '…'}</td>
                            <td className={`${td} max-w-sm`}>{trust[i]?.isError ? 'Not read' : words?.text ?? '…'}</td>
                            <td className={td}>
                              <button type="button" onClick={() => change({ signature: keyOf(s) })} aria-label={`View ${s.reason}`} className="rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold text-[var(--color-primary-700)] hover:bg-[var(--color-primary-50)]">View</button>
                            </td>
                          </tr>
                        )
                      })}
                    </tbody>
                  </table>
                </div>
              )}
              <Pager page={data?.page ?? 1} pageSize={data?.pageSize ?? pageSize} total={data?.total ?? 0} filtered={filtering} onPage={(p) => change({ page: String(p) })} onPageSize={(n) => { setPageSize(n); change({ page: null }) }} label="signatures" />
              <p className="px-4 pb-4 text-xs text-[var(--color-text-muted)]">A signature is one kind of failure on one cloud. Trust is earned per signature and never carries over to another.</p>
            </section>
            {selected && <Detail s={selected} days={days} now={now} onClose={() => change({ signature: null })} links={links} />}
          </div>

          <section aria-labelledby="levels-title" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
            <h2 id="levels-title" className="text-base font-semibold">Levels<Tip label="section:Levels" /> <Link to={nextHref(nextEntry('autonomy'), {}, pathname)} className="ml-2 text-sm font-semibold text-[var(--color-primary-700)] hover:underline">How autonomy is earned →</Link></h2>
            <table className="mt-2 w-full">
              <caption className="sr-only">What each level lets the Agent do</caption>
              <thead><tr><Th>Level</Th><Th>The Agent</Th></tr></thead>
              <tbody>
                <tr className="border-t border-[var(--color-border)]"><td className={`${td} font-semibold`}>L3 · Asks first</td><td className={td}>Prepares the replay and asks a person first. Where a cloud cannot confirm a fix held, or in Production, it stays here.</td></tr>
                <tr className="border-t border-[var(--color-border)]"><td className={`${td} font-semibold`}>L4 · Standing</td><td className={td}>Auto Replay rules may replay this kind without asking, and keep checking it still works. Earned from verified fixes at a high success rate (see each row’s Why).</td></tr>
                <tr className="border-t border-[var(--color-border)]"><td className={`${td} font-semibold`}>L5 · Unattended</td><td className={td}>The same, with the widest limits. Needs more verified fixes at a higher rate.</td></tr>
              </tbody>
            </table>
          </section>
        </>
      )}
    </div>
  )
}

