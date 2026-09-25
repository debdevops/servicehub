import { useQuery } from '@tanstack/react-query'
import { Fingerprint, TriangleAlert } from 'lucide-react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { ExplainerCard, ExplainerToggle } from '../../components/explainer/Explainer'
import { useExplainer } from '../../components/explainer/useExplainer'
import { HelpLabel } from '../../components/ui/InfoTip'
import { columnHelp } from '../../content/columns'
import { useProviderScope } from '../../components/provider/providerScope'
import { Pager } from '../../components/ui/Pager'
import { fetchSignatures, type Signature, type SignatureTab } from '../../lib/api/signatures'
import type { CloudProvider } from '../../lib/api/namespaces'
import { formatAge } from '../../lib/format'
import { providerLabel } from '../../lib/providers'

const asTab = (v: string | null): SignatureTab => (v === 'growing' || v === 'helps' || v === 'doesnt' ? v : 'all')
const asProvider = (v: string | null): CloudProvider | undefined => (v === 'azure' || v === 'aws' || v === 'gcp' ? v : undefined)
const asDays = (v: string | null) => (v === '14' ? 14 : v === '30' ? 30 : 7)

/** The words for how replaying has gone, from the ledger — never a guess, and never "verified" where nothing was verified. */
function replayWords(s: Signature): string {
  const r = s.replays
  const verified = r.stayedFixed + r.returned
  if (r.replayed === 0) return 'not replayed'
  if (verified === 0) return `${r.replayed} replayed, none verified`
  return `${r.stayedFixed} of ${verified} stayed fixed`
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
 * only links there. It says what replaying has done for each signature from the ledger, and shows no autonomy level: those arrive
 * with the trust model (unit 4.1) and are added here only after it lands.
 */
export default function FailureSignaturesPage() {
  const [params, setParams] = useSearchParams()
  const explainer = useExplainer('signatures')
  const tab = asTab(params.get('tab'))
  const provider = asProvider(params.get('provider'))
  const days = asDays(params.get('days'))
  const sort = params.get('sort') === 'recent' ? 'recent' : 'messages'
  const page = Math.max(1, Number(params.get('page')) || 1)
  const selectedHash = params.get('signature')

  const list = useQuery({ queryKey: ['signatures', provider, days, tab, sort, page], queryFn: () => fetchSignatures({ provider, days, tab, sort, page }) })
  const change = (patch: Record<string, string | null>) =>
    setParams((c) => {
      const n = new URLSearchParams(c)
      for (const [k, v] of Object.entries(patch)) v === null || v === '' ? n.delete(k) : n.set(k, v)
      if (!('page' in patch) && !('signature' in patch)) n.delete('page')
      return n
    }, { replace: true })

  const data = list.data
  const selected = data?.items.find((s) => `${s.provider}:${s.signatureHash}` === selectedHash) ?? null
  const now = new Date()
  const tabs: { id: SignatureTab; label: string; n: number | undefined }[] = [
    { id: 'all', label: 'All', n: data?.all },
    { id: 'growing', label: 'Growing', n: data?.growing },
    { id: 'helps', label: 'Replay helps', n: data?.replayHelps },
    { id: 'doesnt', label: 'Replay doesn’t help', n: data?.replayDoesNotHelp },
  ]

  return (
    <section className="px-[22px] pb-6 pt-5">
      <header className="mb-4 flex items-start gap-5">
        <div>
          <h1 className="flex items-center gap-2.5 text-2xl font-extrabold tracking-tight">
            <Fingerprint className="h-6 w-6 text-[var(--color-primary-600)]" aria-hidden="true" /> Failure Signatures <ExplainerToggle visible={!explainer.shown} onShow={explainer.show} />
          </h1>
          <p className="mt-[3px] text-[13px] text-[var(--color-text-muted)]">Failures grouped by how they fail. The Agent and the rules reason about these groups, not single messages.</p>
        </div>
        <div className="ml-auto flex gap-3">
          <Select label="Scope" value={provider ?? ''} onChange={(v) => change({ provider: v || null })}>
            <option value="">All clouds</option>
            {(['azure', 'aws', 'gcp'] as const).map((p) => <option key={p} value={p}>{providerLabel[p]}</option>)}
          </Select>
          <Select label="Window" value={String(days)} onChange={(v) => change({ days: v === '7' ? null : v })}>
            <option value="7">Last 7 days</option><option value="14">Last 14 days</option><option value="30">Last 30 days</option>
          </Select>
        </div>
      </header>

      {explainer.shown && <ExplainerCard id="signatures" onDismiss={explainer.dismiss} />}
      {list.isPending && <p role="status" className="text-sm text-[var(--color-text-muted)]">Reading signatures…</p>}
      {list.isError && <p role="alert" className="text-sm text-[var(--color-error)]">ServiceHub couldn’t read the signatures just now.</p>}

      {data && (
        <div className="flex items-start gap-3.5">
          <div className="min-w-0 flex-1 overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] shadow-[var(--shadow-card)]">
            <div className="flex items-center gap-1 border-b border-[var(--color-border)] px-3">
              <nav aria-label="Signature views" className="flex">
                {tabs.map((t) => (
                  <button key={t.id} type="button" aria-current={t.id === tab ? 'page' : undefined} onClick={() => change({ tab: t.id === 'all' ? null : t.id })}
                    className={`-mb-px border-b-2 px-3 py-3 text-[13px] font-semibold ${t.id === tab ? 'border-[var(--color-primary-600)] text-[var(--color-primary-700)]' : 'border-transparent text-[var(--color-text-muted)]'}`}>
                    {t.label} <span className="ml-1 rounded-full bg-[var(--color-surface-muted)] px-1.5 text-[11px]">{t.n ?? 0}</span>
                  </button>
                ))}
              </nav>
              <label className="ml-auto text-[12px] text-[var(--color-text-muted)]">Sort{' '}
                <select value={sort} onChange={(e) => change({ sort: e.target.value === 'messages' ? null : e.target.value })} className="rounded-lg border border-[var(--color-border)] px-2 py-1 text-[12px]">
                  <option value="messages">most messages</option><option value="recent">most recent</option>
                </select>
              </label>
            </div>
            {data.items.length === 0 ? (
              <p className="px-5 py-8 text-center text-sm text-[var(--color-text-muted)]">
                {tab === 'all' ? 'No failures have been recorded yet. ServiceHub records them where it can look on its own; a cloud it does not watch has none to group.' : 'Nothing matches this view.'}
              </p>
            ) : (
              <table className="w-full border-collapse text-left text-[12.5px]">
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
                  {data.items.map((s) => {
                    const key = `${s.provider}:${s.signatureHash}`
                    return (
                      <tr key={key} aria-selected={key === selectedHash} className={`cursor-pointer border-b border-[#f3f4f6] ${key === selectedHash ? 'bg-[var(--color-primary-50)]' : 'hover:bg-[var(--color-surface-muted)]'}`} onClick={() => change({ signature: key })}>
                        <td className="px-4 py-3">
                          <div className="flex items-center gap-2">
                            <span className="rounded-full bg-[var(--color-error-light)] px-2.5 py-0.5 text-[11px] font-bold text-[#b91c1c]">{s.reason}</span>
                            <button type="button" onClick={(e) => { e.stopPropagation(); change({ signature: key }) }} className="text-left font-semibold">{s.exampleError ?? 'No error text was recorded'}</button>
                          </div>
                          <div className="mt-0.5 font-mono text-[11.5px] text-[var(--color-text-muted)]">{s.entities.join(' · ')} <span className="font-sans">· {providerLabel[s.provider]}</span></div>
                        </td>
                        <td className="tabular px-3 py-3 text-[15px] font-bold">{s.messages.toLocaleString()}</td>
                        <td className="px-3 py-3"><Spark daily={s.daily} tone={s.growing ? 'bg-[#f87171]' : 'bg-[#38bdf8]'} /></td>
                        <td className="px-3 py-3">{replayWords(s)}</td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            )}
            <Pager page={data.page} pageSize={data.pageSize} total={data.total} onPage={(p) => change({ page: String(p) })} />
          </div>
          {selected && <Detail s={selected} days={days} now={now} onClose={() => change({ signature: null })} />}
        </div>
      )}
    </section>
  )
}

function Select({ label, value, onChange, children }: { label: string; value: string; onChange: (v: string) => void; children: React.ReactNode }) {
  return (
    <label className="rounded-[10px] border border-[var(--color-border)] bg-[var(--color-surface)] px-[13px] py-1.5 shadow-[var(--shadow-card)]">
      <span className="block text-[9.5px] font-bold uppercase tracking-[0.6px] text-[#9ca3af]">{label}</span>
      <select value={value} onChange={(e) => onChange(e.target.value)} className="bg-transparent text-[12.5px] font-semibold">{children}</select>
    </label>
  )
}

function Detail({ s, days, now, onClose }: { s: Signature; days: number; now: Date; onClose: () => void }) {
  const { select } = useProviderScope()
  const navigate = useNavigate()
  const max = Math.max(1, ...s.daily)
  const r = s.replays
  const verified = r.stayedFixed + r.returned
  const help =
    s.replayVerdict === 'unknown'
      ? r.replayed === 0
        ? 'Nothing has been replayed yet, so there is no evidence either way.'
        : `${r.replayed} replayed, but none could be verified, so it is not counted as helping or not.`
      : s.replayVerdict === 'helps'
        ? `Yes — ${r.stayedFixed} of ${verified} replays stayed fixed.`
        : `No — ${r.stayedFixed} of ${verified} replays stayed fixed. ${r.returned === 1 ? 'The one that came' : 'The ones that came'} back failed the same way, so retrying is unlikely to help until the cause is fixed.`

  return (
    <aside aria-label="Signature details" className="w-[420px] shrink-0 overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] shadow-[var(--shadow-card)]">
      <div className="flex items-center justify-between border-b border-[#f3f4f6] px-4 py-3">
        <span className="rounded-full bg-[var(--color-error-light)] px-2.5 py-0.5 text-[11px] font-bold text-[#b91c1c]">{s.reason}</span>
        <span className="text-[11.5px] text-[var(--color-text-muted)]">first {formatAge(s.firstSeenAt, now)} ago · last {formatAge(s.lastSeenAt, now)} ago</span>
        <button type="button" onClick={onClose} aria-label="Close" className="text-sm text-[var(--color-text-muted)]">✕</button>
      </div>
      <div className="space-y-4 px-4 py-4 text-[13px]">
        <div>
          <p className="text-[15px] font-bold">{s.exampleError ?? 'No error text was recorded'}</p>
          <p className="mt-1 text-[var(--color-text-muted)]"><b className="text-[var(--color-text)]">{s.messages.toLocaleString()} messages</b> · <span className="font-mono text-[12px]">{s.entities.join(' · ')}</span> · {providerLabel[s.provider]} · {s.activeNow} still in the queue</p>
        </div>
        <Section title="Is it getting worse?">
          <div className="flex h-16 items-end gap-1" role="img" aria-label={`Messages per day over ${days} days: ${s.daily.join(', ')}`}>
            {s.daily.map((n, i) => <span key={i} className="flex-1 rounded-t bg-[#fca5a5]" style={{ height: `${Math.max(4, (n / max) * 100)}%`, opacity: n === 0 ? 0.3 : 1 }} />)}
          </div>
          {s.growing && <p className="mt-2 flex items-start gap-2 rounded-lg border border-[#fecaca] bg-[#fef2f2] px-3 py-2 text-[12.5px] text-[#7f1d1d]"><TriangleAlert className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" /><span><b>Growing.</b> The recent days hold at least twice what the earlier ones did.</span></p>}
        </Section>
        <Section title="Does replaying help?"><p>{help}</p></Section>
        <Section title="Where">
          <p className="flex flex-wrap gap-x-4 gap-y-1 font-semibold text-[var(--color-primary-600)]">
            <button type="button" className="hover:underline" onClick={() => { select(s.provider); navigate(`/?tab=dlq&reason=${encodeURIComponent(s.reason)}`) }}>See the {s.messages} messages in Home ›</button>
            <Link className="hover:underline" to={`/advanced/ledger?provider=${s.provider}`}>Ledger entries ›</Link>
            <button type="button" className="hover:underline" onClick={() => { select(s.provider); navigate(`/?panel=rules&rule=${s.signatureHash}`) }}>Create an auto-replay rule from this ›</button>
          </p>
          <p className="mt-2 text-[12px] text-[var(--color-text-muted)]">The rule is created on Auto Replay, where it can be tested against the last 7 days. Nothing is created here.</p>
        </Section>
      </div>
    </aside>
  )
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section>
      <h3 className="mb-1.5 text-[10.5px] font-bold uppercase tracking-[0.6px] text-[var(--color-text-muted)]">{title}</h3>
      {children}
    </section>
  )
}
