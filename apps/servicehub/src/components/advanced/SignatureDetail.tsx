import { useQuery } from '@tanstack/react-query'
import { TriangleAlert } from 'lucide-react'
import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { sectionHelp } from '../../content/sections'
import { type ColumnHelp } from '../../content/columns'
import { fetchSignatureTrust, type Signature } from '../../lib/api/signatures'
import { formatAgo } from '../../lib/format'
import { providerLabel } from '../../lib/providers'
import { trustWords } from '../../lib/trustWords'
import { reasonParam } from '../../lib/urlState'
import { useProviderScope } from '../provider/providerScope'
import { environmentMeta } from '../provider/scopeChoice'
import { InfoTip } from '../ui/InfoTip'
import { RetryLink } from '../ui/RetryLink'
import { TabBar } from '../ui/TabBar'
import { IncidentTimeline } from './SignatureStory'

/**
 * Where a signature's detail sends you. The 4.2.0 Advanced page passes nothing and keeps its own addresses; the 5.0.0 shell passes its
 * own. `incident: false` hides the Incident tab there until the Evidence ledger exists in that shell (unit E7), because its links
 * lead to the 4.2.0 ledger.
 */
export interface SignatureLinks {
  readonly messages: (s: Signature) => string | null
  readonly ledger: (s: Signature) => string
  readonly rule: (s: Signature) => string
  readonly incident: boolean
}

export function Detail({ s, days, now, onClose, links }: { s: Signature; days: number; now: Date; onClose: () => void; links?: SignatureLinks }) {
  const { select } = useProviderScope()
  const navigate = useNavigate()
  const max = Math.max(1, ...s.daily)
  const r = s.replays
  const verified = r.stayedFixed + r.returned
  const [view, setView] = useState<'summary' | 'incident'>('summary')
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
        <span className="text-[11.5px] text-[var(--color-text-muted)]">first {formatAgo(s.firstSeenAt, now)} · last {formatAgo(s.lastSeenAt, now)}</span>
        <button type="button" onClick={onClose} aria-label="Close" className="text-sm text-[var(--color-text-muted)]">✕</button>
      </div>
      {links?.incident !== false && <TabBar label="Signature detail" active={view} onSelect={(id) => setView(id as 'summary' | 'incident')}
        tabs={[
          { id: 'summary', label: 'Summary', count: 'none', help: sectionHelp.tabs.summary },
          { id: 'incident', label: 'Incident', count: 'none', help: sectionHelp.tabs.incident },
        ]} />}
      {view === 'incident' ? <div className="px-4 py-4 text-[13px]"><IncidentTimeline hash={s.signatureHash} provider={s.provider} /></div> : (
      <div className="space-y-4 px-4 py-4 text-[13px]">
        <div>
          <p className="text-[15px] font-bold">{s.exampleError ?? 'No error text was recorded'}</p>
          <p className="mt-1 text-[var(--color-text-muted)]"><b className="text-[var(--color-text)]">{s.messages.toLocaleString()} messages</b> · <span className="font-mono text-[12px]">{s.entities.join(' · ')}</span> · {providerLabel[s.provider]} · {s.activeNow} still in the queue</p>
        </div>
        <Section title="Is it getting worse?" help={sectionHelp.signatures.worse}>
          <div className="flex h-16 items-end gap-1" role="img" aria-label={`Messages per day over ${days} days: ${s.daily.join(', ')}`}>
            {s.daily.map((n, i) => <span key={i} className="flex-1 rounded-t bg-[#fca5a5]" style={{ height: `${Math.max(4, (n / max) * 100)}%`, opacity: n === 0 ? 0.3 : 1 }} />)}
          </div>
          {s.growing && <p className="mt-2 flex items-start gap-2 rounded-lg border border-[#fecaca] bg-[#fef2f2] px-3 py-2 text-[12.5px] text-[#7f1d1d]"><TriangleAlert className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" /><span><b>Growing.</b> The recent days hold at least twice what the earlier ones did.</span></p>}
        </Section>
        <Section title="Does replaying help?" help={sectionHelp.signatures.helps}><p>{help}</p></Section>
        <Section title="Can ServiceHub replay it on its own?" help={sectionHelp.signatures.onItsOwn}><Earned hash={s.signatureHash} provider={s.provider} /></Section>
        <Section title="Seen in" help={sectionHelp.signatures.seenIn}>
          {s.namespaces.length === 0 ? (
            <p className="text-[var(--color-text-muted)]">No connected namespace holds it now.</p>
          ) : (
            <ul className="space-y-1">
              {s.namespaces.map((n) => (
                <li key={n.id} className="flex items-center gap-2">
                  <span className={`rounded-full px-2 py-0.5 text-[10px] font-semibold ${environmentMeta[n.environment].chip}`}>{environmentMeta[n.environment].label}</span>
                  <span className="min-w-0 flex-1 truncate font-mono text-[12px]">{n.displayName ?? n.name}</span>
                  <span className="tabular font-bold">{n.messages.toLocaleString()}</span>
                </li>
              ))}
            </ul>
          )}
        </Section>
        <Section title="Where" help={sectionHelp.signatures.where}>
          <p className="flex flex-wrap gap-x-4 gap-y-1 font-semibold text-[var(--color-primary-600)]">
            {links ? (
              <>
                {links.messages(s) && <Link className="hover:underline" to={links.messages(s) as string}>See the {s.messages} messages ›</Link>}
                <Link className="hover:underline" to={links.ledger(s)}>Ledger entries ›</Link>
                <Link className="hover:underline" to={links.rule(s)}>Create an auto-replay rule from this ›</Link>
              </>
            ) : (
              <>
                <button type="button" className="hover:underline" onClick={() => { select(s.provider); navigate(`/?tab=dlq&reason=${encodeURIComponent(reasonParam(s.reason))}`) }}>See the {s.messages} messages in Home ›</button>
                <Link className="hover:underline" to={`/advanced/ledger?provider=${s.provider}`}>Ledger entries ›</Link>
                <button type="button" className="hover:underline" onClick={() => { select(s.provider); navigate(`?${new URLSearchParams({ ...Object.fromEntries(new URLSearchParams(window.location.search)), panel: 'rules', rule: s.signatureHash })}`) }}>Create an auto-replay rule from this ›</button>
              </>
            )}
          </p>
          <p className="mt-2 text-[12px] text-[var(--color-text-muted)]">The rule is created on Auto Replay, where it can be tested against the last 7 days. Nothing is created here.</p>
        </Section>
      </div>
      )}
    </aside>
  )
}

/** What this failure has earned — read-only; rules and the gate are what act on it. */
function Earned({ hash, provider }: { hash: string; provider: Signature['provider'] }) {
  const trust = useQuery({ queryKey: ['signatures', 'trust', hash, provider], queryFn: () => fetchSignatureTrust(hash, provider) })
  if (trust.isPending) return <p role="status" className="text-[var(--color-text-muted)]">Counting its verified replays…</p>
  if (trust.isError) return <p className="text-[var(--color-text-muted)]">ServiceHub couldn’t read what it has earned. <RetryLink onRetry={() => void trust.refetch()} /></p>
  const w = trustWords(trust.data, providerLabel[provider])
  const tone = w.answer === 'yes' ? 'bg-[var(--color-success-light)] text-[#047857]' : w.answer === 'no' ? 'bg-[var(--color-surface-muted)] text-[var(--color-text)]' : 'bg-[var(--color-warning-light)] text-[#92400e]'
  return (
    <p>
      <span className={`mr-2 rounded-full px-2 py-0.5 text-[11px] font-bold uppercase ${tone}`}>{w.answer}</span>
      {w.text}
    </p>
  )
}

function Section({ title, help, children }: { title: string; help: ColumnHelp; children: React.ReactNode }) {
  return (
    <section>
      <h3 className="mb-1.5 flex items-center text-[10.5px] font-bold uppercase tracking-[0.6px] text-[var(--color-text-muted)]">{title}<InfoTip help={help} /></h3>
      {children}
    </section>
  )
}
