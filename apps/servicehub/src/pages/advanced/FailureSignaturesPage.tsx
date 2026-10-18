import { Fragment } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Fingerprint } from 'lucide-react'
import { usePageSize } from '../../lib/pageSize'
import { useSearchParams } from 'react-router-dom'
import { ExplainerCard, ExplainerToggle } from '../../components/explainer/Explainer'
import { useExplainer } from '../../components/explainer/useExplainer'
import { HelpLabel } from '../../components/ui/InfoTip'
import { columnHelp } from '../../content/columns'
import { Pager } from '../../components/ui/Pager'
import { TabBar } from '../../components/ui/TabBar'
import { fetchSignatures, type SignatureTab } from '../../lib/api/signatures'
import { AdvancedFilters, asBy } from '../../components/advanced/AdvancedFilters'
import { environmentMeta, resolveScope } from '../../components/provider/scopeChoice'
import { useNamespaces } from '../../hooks/useNamespaces'
import type { CloudProvider } from '../../lib/api/namespaces'
import { providerLabel } from '../../lib/providers'
import { TraceView } from '../../components/advanced/SignatureStory'
import { Detail } from '../../components/advanced/SignatureDetail'
import { replayWords } from '../../lib/signatureWords'
import { RetryLink } from '../../components/ui/RetryLink'
import { Skeleton } from '../../components/ui/Skeleton'
import { Select as UiSelect } from '../../components/ui/Select'
import PageHelpLink from '../../components/help/PageHelpLink'

const asTab = (v: string | null): SignatureTab => (v === 'growing' || v === 'helps' || v === 'doesnt' ? v : 'all')
const asProvider = (v: string | null): CloudProvider | undefined => (v === 'azure' || v === 'aws' || v === 'gcp' ? v : undefined)
const asDays = (v: string | null) => (v === '14' ? 14 : v === '30' ? 30 : 7)

const signatureTone: Record<SignatureTab, 'bad' | 'good' | 'neutral' | undefined> = { all: undefined, growing: 'bad', helps: 'good', doesnt: 'neutral' }

/** One line under the tabs saying what the chosen tab is showing, so a person never has to guess what "Growing" means. */
const tabSummary: Record<SignatureTab, string> = {
  all: 'Every way of failing ServiceHub has recorded in this window.',
  growing: 'Getting worse: at least 3 new messages lately, and twice as many as earlier in the window.',
  helps: 'Replaying these has worked — at least half of the checked replays stayed fixed.',
  doesnt: 'Replaying these mostly fails again — fix the cause before replaying more.',
}

function Spark({ daily, tone }: { daily: readonly number[]; tone: string }) {
  const max = Math.max(1, ...daily)
  return (
    <span className="flex h-6 items-end gap-0.5" role="img" aria-label={`Messages per day: ${daily.join(', ')}`}>
      {daily.map((n, i) => <span key={i} className={`w-1.5 rounded-sm ${tone}`} style={{ height: `${Math.max(8, (n / max) * 100)}%`, opacity: n === 0 ? 0.25 : 1 }} />)}
    </span>
  )
}

/**
 * Failure Signatures (Advanced): which failures are the same failure. Read-only — a rule is created on Auto Replay, and this page
 * only links there. It says what replaying has done for each signature from the ledger, and — from the trust model (unit 4.1) —
 * whether ServiceHub may replay it on its own yet, and what it still needs.
 */
export default function FailureSignaturesPage() {
  const [params, setParams] = useSearchParams()
  const explainer = useExplainer('signatures')
  const tab = asTab(params.get('tab'))
  const provider = asProvider(params.get('provider'))
  const days = asDays(params.get('days'))
  const sort = params.get('sort') === 'recent' ? 'recent' : 'messages'
  const page = Math.max(1, Number(params.get('page')) || 1)
  const [pageSize, setPageSize] = usePageSize()
  const selectedHash = params.get('signature')
  const tracing = params.get('view') === 'trace'

  const namespaces = useNamespaces()
  // The cloud picks which namespaces are on offer; `?ns=` / `?env=` narrow within them. A stale one is ignored.
  const inScope = (namespaces.data ?? []).filter((n) => !provider || n.provider === provider)
  const choice = resolveScope(inScope, params)
  const namespaceId = choice.ns?.id
  const environment = choice.env ?? undefined
  const by = asBy(params.get('by'))
  const entity = params.get('entity') ?? undefined
  const search = params.get('q') ?? undefined
  const list = useQuery({
    queryKey: ['signatures', provider, namespaceId, environment, days, tab, sort, page, pageSize, by, entity, search],
    queryFn: () => fetchSignatures({ provider, namespaceId, environment, days, tab, sort, page, pageSize, by, entity, q: search }),
  })
  const change = (patch: Record<string, string | null>) =>
    setParams((c) => {
      const n = new URLSearchParams(c)
      for (const [k, v] of Object.entries(patch)) {
        if (v === null || v === '') n.delete(k)
        else n.set(k, v)
      }
      if (!('page' in patch) && !('signature' in patch)) n.delete('page')
      return n
    }, { replace: true })

  const data = list.data
  const selected = data?.items.find((s) => `${s.provider}:${s.signatureHash}` === selectedHash) ?? null
  const now = new Date()
  const tabs: { id: SignatureTab; label: string; n: number | undefined }[] = [
    { id: 'all', label: 'All', n: data?.all },
    { id: 'growing', label: 'Growing (getting worse)', n: data?.growing },
    { id: 'helps', label: 'Replay helps', n: data?.replayHelps },
    { id: 'doesnt', label: 'Replay doesn’t help', n: data?.replayDoesNotHelp },
  ]

  return (
    <section className="px-[22px] pb-6 pt-5">
      <header className="mb-4 flex flex-wrap items-start gap-3 sm:gap-5">
        <div className="min-w-0">
          <h1 className="flex items-center gap-2.5 text-2xl font-extrabold tracking-tight">
            <Fingerprint className="h-6 w-6 text-[var(--color-primary-600)]" aria-hidden="true" /> Failure Signatures <ExplainerToggle visible={!explainer.shown} onShow={explainer.show} /> <PageHelpLink page="advanced" />
          </h1>
          <p className="mt-[3px] text-[13px] text-[var(--color-text-muted)]">Failures grouped by how they fail. The Agent and the rules reason about these groups, not single messages.</p>
        </div>
        <div className="ml-auto flex flex-wrap items-end gap-3">
          <div role="radiogroup" aria-label="Page view" className="flex rounded-lg bg-[var(--color-surface-muted)] p-0.5 text-[12.5px]">
            {([['list', 'Signatures'], ['trace', 'Trace a message']] as const).map(([id, label]) => (
              <button key={id} type="button" role="radio" aria-checked={(id === 'trace') === tracing} onClick={() => change({ view: id === 'trace' ? 'trace' : null, signature: null })}
                className={`rounded-md px-2.5 py-1 font-semibold ${(id === 'trace') === tracing ? 'bg-[var(--color-surface)] shadow-sm' : 'text-[var(--color-text-muted)]'}`}>{label}</button>
            ))}
          </div>
          <Select label="Window" value={String(days)} onChange={(v) => change({ days: v === '7' ? null : v })}>
            <option value="7">Last 7 days</option><option value="14">Last 14 days</option><option value="30">Last 30 days</option>
          </Select>
        </div>
      </header>

      {explainer.shown && <ExplainerCard id="signatures" onDismiss={explainer.dismiss} />}
      {tracing && <TraceView />}
      {!tracing && list.isPending && <Skeleton label="Reading signatures…" rows={5} />}
      {!tracing && list.isError && <p role="alert" className="text-sm text-[var(--color-error)]">ServiceHub couldn’t read the signatures just now. <RetryLink onRetry={() => void list.refetch()} /></p>}

      {!tracing && data && (
        <div className="flex items-start gap-3.5">
          <div className="min-w-0 flex-1 overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] shadow-[var(--shadow-card)]">
            <TabBar variant="pills" label="Signature views" active={tab} onSelect={(id) => change({ tab: id === 'all' ? null : id })}
              tabs={tabs.map((t) => ({ id: t.id, label: t.label, count: t.n ?? 0, help: columnHelp.signatureTabs[t.id], tone: signatureTone[t.id] }))} />
            <AdvancedFilters namespaces={namespaces.data ?? []} searchPlaceholder="Search signatures, errors or queues…" />
            <div className="flex items-center gap-2 border-b border-[var(--color-border)] px-4 py-2">
              <p className="text-[12.5px] text-[var(--color-text-muted)]">{tabSummary[tab]}</p>
              <div className="ml-auto flex items-center gap-1.5 text-[12px] text-[var(--color-text-muted)]">Sort
                <UiSelect variant="inline" ariaLabel="Sort" value={sort} onChange={(v) => change({ sort: v === 'messages' ? null : v })} className="text-[12px]">
                  <option value="messages">most messages</option><option value="recent">most recent</option>
                </UiSelect>
              </div>
            </div>
            {data.items.length === 0 ? (
              <p className="px-5 py-8 text-center text-sm text-[var(--color-text-muted)]">
                {tab === 'all' ? 'No failures have been recorded yet. ServiceHub records them where it can look on its own; a cloud it does not watch has none to group.' : 'Nothing matches this view.'}
              </p>
            ) : (
              <div className="relative overflow-x-auto">
              <table className="w-full min-w-[640px] border-collapse text-left text-[12.5px]">
                <caption className="sr-only">Failure signatures</caption>
                <thead>
                  <tr className="border-b border-[var(--color-border)] text-[10px] uppercase tracking-[0.6px] text-[var(--color-text-muted)]">
                    <th scope="col" className="px-4 py-2 font-bold"><HelpLabel help={columnHelp.signatures.signature}>Signature</HelpLabel></th>
                    <th scope="col" className="px-3 py-2 font-bold"><HelpLabel help={columnHelp.signatures.messages}>Messages</HelpLabel></th>
                    <th scope="col" className="px-3 py-2 font-bold"><HelpLabel help={columnHelp.signatures.days}>{days} days</HelpLabel></th>
                    <th scope="col" className="px-3 py-2 font-bold"><HelpLabel help={columnHelp.signatures.replays}>Replays</HelpLabel></th>
                  </tr>
                </thead>
                <tbody>
                  {[...data.items].sort((a, b) => a.provider.localeCompare(b.provider)).map((s, i, all) => {
                    const key = `${s.provider}:${s.signatureHash}`
                    return (
                      <Fragment key={key}>
                      {(i === 0 || all[i - 1]!.provider !== s.provider) && (
                        <tr className="bg-[var(--color-surface-muted)]"><th scope="colgroup" colSpan={4} className="px-4 py-1.5 text-left text-[11px] font-bold uppercase tracking-wide text-[var(--color-text-muted)]">{providerLabel[s.provider]}</th></tr>
                      )}
                      <tr aria-selected={key === selectedHash} className={`cursor-pointer border-b border-[#f3f4f6] ${key === selectedHash ? 'bg-[var(--color-primary-50)]' : 'hover:bg-[var(--color-surface-muted)]'}`} onClick={() => change({ signature: key })}>
                        <td className="px-4 py-3">
                          <div className="flex items-center gap-2">
                            <span className="rounded-full bg-[var(--color-error-light)] px-2.5 py-0.5 text-[11px] font-bold text-[#b91c1c]">{s.reason}</span>
                            <button type="button" onClick={(e) => { e.stopPropagation(); change({ signature: key }) }} className="text-left font-semibold">{s.exampleError ?? 'No error text was recorded'}</button>
                          </div>
                          <div className="mt-0.5 font-mono text-[11.5px] text-[var(--color-text-muted)]">{s.entities.join(' · ')} <span className="font-sans">· {providerLabel[s.provider]}</span></div>
                          <div className="mt-1 flex flex-wrap gap-1">
                            {[...new Set(s.namespaces.map((n) => n.environment))].map((env) => (
                              <span key={env} className={`rounded-full px-2 py-0.5 text-[10px] font-semibold ${environmentMeta[env].chip}`}>{environmentMeta[env].label}</span>
                            ))}
                          </div>
                        </td>
                        <td className="tabular px-3 py-3 text-[15px] font-bold">{s.messages.toLocaleString()}</td>
                        <td className="px-3 py-3"><Spark daily={s.daily} tone={s.growing ? 'bg-[#f87171]' : 'bg-[#38bdf8]'} /></td>
                        <td className="px-3 py-3">{replayWords(s)}</td>
                      </tr>
                      </Fragment>
                    )
                  })}
                </tbody>
              </table>
              </div>
            )}
            <Pager page={data.page} pageSize={data.pageSize} total={data.total} onPage={(p) => change({ page: String(p) })} onPageSize={(s) => { setPageSize(s); change({ page: null }) }} />
          </div>
          {selected && <Detail s={selected} days={days} now={now} onClose={() => change({ signature: null })} />}
        </div>
      )}
    </section>
  )
}

function Select({ label, value, onChange, children }: { label: string; value: string; onChange: (v: string) => void; children: React.ReactNode }) {
  return <UiSelect variant="card" label={label} ariaLabel={label} value={value} onChange={onChange}>{children}</UiSelect>
}
