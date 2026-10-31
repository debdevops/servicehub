import { Bot, Download, Hash, KeyRound, ShieldCheck, UserRound, X } from 'lucide-react'
import { useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { Attribution } from '../Attribution'
import { sectionHelp } from '../../content/sections'
import { useDeadLetter } from '../../hooks/useDeadLetter'
import { useLedgerEntry } from '../../hooks/useRecoverySummary'
import { exportEvidence, verifyChain, type ChainVerification, type EntryState, type LedgerEntry, type RecoveryWindow } from '../../lib/api/recovery'
import type { CloudProvider } from '../../lib/api/namespaces'
import { describeEvent, meaningOf, shortHash, stateChip, stateTone } from '../../lib/ledgerWords'
import { formatWhen } from '../../lib/format'
import { providerLabel } from '../../lib/providers'
import { cloudColor } from '../provider/scopeChoice'
import { InfoTip } from '../ui/InfoTip'

/** A cloud, drawn the same for all three: its colour, its initial and its name. */
export function CloudBadge({ provider }: { provider: CloudProvider | null }) {
  if (!provider) return <span className="text-[var(--color-text-muted)]">—</span>
  return (
    <span className="inline-flex items-center gap-2">
      <span aria-hidden="true" className="flex h-6 w-6 items-center justify-center rounded-md text-[11px] font-extrabold text-white" style={{ background: cloudColor[provider] }}>{providerLabel[provider][0]}</span>
      {providerLabel[provider]}
    </span>
  )
}

/** Who made it, in two lines: the name, then what kind of actor it was. Never a made-up person. */
export function ByCell({ actor, at }: { actor: LedgerEntry['actor']; at: string }) {
  const autonomous = actor.kind === 'system' || actor.kind === 'automation'
  const sub = autonomous
    ? (/AutoReplay/.test(actor.identity) ? 'Auto Replay agent' : 'ServiceHub agent')
    : actor.kind === 'apiKey' ? 'API key' : 'User initiated'
  const Icon = autonomous ? Bot : actor.kind === 'apiKey' ? KeyRound : UserRound
  const name = autonomous
    ? `ServiceHub · ${actor.identity.replace(/^System:/, '')}`
    : actor.kind === 'apiKey' ? actor.identity.replace(/^ApiKey:/, '') : actor.isSession ? 'This browser session' : actor.label
  return (
    <span className="flex items-start gap-2" data-actor-kind={actor.kind} title={`${name} · ${formatWhen(at, new Date())}`}>
      <Icon aria-hidden="true" className="mt-0.5 h-4 w-4 shrink-0 text-[var(--color-text-muted)]" />
      <span><span className="block text-[13px] font-medium">{name}</span><span className="block text-xs text-[var(--color-text-muted)]">{sub}</span></span>
    </span>
  )
}

const levelMeta = {
  approve: { chip: 'L3', label: 'L3 Approve — a person approves each replay', cls: 'bg-[#dbeafe] text-[#1d4ed8]' },
  standing: { chip: 'L4', label: 'L4 Standing — rules may replay without asking', cls: 'bg-[#ede9fe] text-[#6d28d9]' },
  unattended: { chip: 'L5', label: 'L5 Unattended — earned the most trust', cls: 'bg-[#f3e8ff] text-[#7e22ce]' },
} as const

/** The level the failure's signature held when the action began. A dash means no signature was known — never a guess. */
export function LevelChip({ level }: { level?: LedgerEntry['level'] }) {
  if (!level) return <span className="text-[var(--color-text-muted)]" aria-label="No level recorded">—</span>
  const m = levelMeta[level]
  return <span title={m.label} aria-label={m.label} className={`inline-block rounded-md px-2 py-0.5 text-xs font-bold ${m.cls}`}>{m.chip}</span>
}

export function StateChip({ state }: { state: EntryState }) {
  const tone = stateTone(state)
  const bg = tone === 'good' ? 'bg-[var(--color-success-light)]' : tone === 'bad' ? 'bg-[var(--color-error-light)]' : tone === 'warn' ? 'bg-[var(--color-warning-light)]' : 'bg-[var(--color-surface-muted)]'
  return <span className={`inline-block whitespace-nowrap rounded-full px-2.5 py-0.5 text-xs font-medium text-[var(--color-text)] ${bg}`}>{stateChip[state]}</span>
}

/** One entry opened: who, what happened, the evidence and the chain check. Read-only, and its links open the Simple flow. */
/**
 * Where an entry's own links go. The 4.2.0 Advanced page passes nothing and keeps its own addresses (Home, and the Replay modal over
 * the page); the 5.0.0 shell passes its own. `replay: null` hides Replay… there until the Replay dialog is built in that shell (F2).
 */
export interface EntryLinks {
  readonly message: (dlqMessageId: number) => string
  readonly replay: ((dlqMessageId: number) => string) | null
}

/** `windowed`: inside a details window (5.0.0), which supplies the frame and the ✕. */
export function EntryPanel({ id, onClose, links, windowed = false }: { id: string | null; onClose: () => void; links?: EntryLinks; windowed?: boolean }) {
  const { data, isPending, isError } = useLedgerEntry(id)
  const { search } = useLocation()
  // Whether the message is still stuck decides if "replay again" means anything: a replayed message has left the queue.
  const source = useDeadLetter(data?.entry.dlqMessageId ?? null).data?.item
  const [chain, setChain] = useState<{ busy: boolean; result?: ChainVerification; failed?: boolean }>({ busy: false })

  if (id === null) {
    return null
  }
  if (isPending) return <aside aria-label="Entry" className="rounded-xl border border-[var(--color-border)] p-6 text-sm" role="status">Reading the entry…</aside>
  if (isError || !data) return <aside aria-label="Entry" role="alert" className="rounded-xl border border-[var(--color-border)] p-6 text-sm">ServiceHub couldn’t read this entry. It may not be one you can see.</aside>

  const e = data.entry
  const cloud = e.provider ? providerLabel[e.provider] : 'the cloud'
  const last = data.events.at(-1)
  const homeParams = (tab: string) => {
    const next = new URLSearchParams()
    next.set('tab', tab)
    if (e.dlqMessageId !== null) next.set('message', String(e.dlqMessageId))
    return `/?${next.toString()}`
  }

  const check = async () => {
    setChain({ busy: true })
    try {
      setChain({ busy: false, result: await verifyChain() })
    } catch {
      setChain({ busy: false, failed: true })
    }
  }

  return (
    <aside aria-label="Entry" className={windowed ? '-mx-5 -my-4' : 'self-start overflow-y-auto rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] lg:sticky lg:top-[calc(var(--header-height)+8px)] lg:max-h-[calc(100vh-var(--header-height)-24px)]'}>
      <div className="flex items-start gap-2 border-b border-[var(--color-border)] p-4">
        <div className="min-w-0 flex-1">
          <p className="flex flex-wrap items-center gap-2"><StateChip state={e.state} /> <span className="text-sm font-medium">{meaningOf(e.state, e.kind)}</span></p>
          <p className="mt-1 text-sm"><span className="font-mono text-[13px]">{e.entityName}</span> → <span className="font-mono text-[13px]">{e.targetEntity}</span> · {cloud}{e.namespaceName ? ` · ${e.namespaceName}` : ''}</p>
          {e.state === 'Returned' && e.confidence && (
            <p className="mt-1 text-xs text-[var(--color-text-muted)]">Match: {e.confidence === 'Exact' ? 'Exact — by the recovery ID' : 'Heuristic — by contents, which may be shared'}</p>
          )}
        </div>
        {!windowed && <button type="button" onClick={onClose} aria-label="Close entry" className="rounded-lg p-1 text-[var(--color-text-muted)] hover:bg-[var(--color-surface-muted)]"><X className="h-4 w-4" /></button>}
      </div>

      <div className="space-y-4 p-4 text-sm">
        {(e.messageId || source) && (
          <section aria-label="Message">
            <h3 className="mb-1 flex items-center text-xs font-semibold uppercase tracking-wide text-[var(--color-text-muted)]">Message<InfoTip help={sectionHelp.ledger.message} /></h3>
            <dl className="grid grid-cols-[110px_1fr] gap-y-1">
              <dt className="text-[var(--color-text-muted)]">Message ID</dt><dd className="break-all font-mono text-xs">{e.messageId ?? source?.messageId ?? '—'}</dd>
              {source?.deadLetterReason && (<><dt className="text-[var(--color-text-muted)]">Failed because</dt><dd>{source.deadLetterReason}{source.deadLetterErrorDescription ? <span className="block text-xs text-[var(--color-text-muted)]">{source.deadLetterErrorDescription}</span> : null}</dd></>)}
            </dl>
          </section>
        )}

        <section aria-label="Who"><h3 className="mb-1 flex items-center text-xs font-semibold uppercase tracking-wide text-[var(--color-text-muted)]">Who<InfoTip help={sectionHelp.ledger.who} /></h3><Attribution actor={e.actor} at={e.begunAt} verb={e.kind === 'Purge' ? 'Purged' : 'Replayed'} /></section>

        <section aria-label="What happened">
          <h3 className="mb-1 flex items-center text-xs font-semibold uppercase tracking-wide text-[var(--color-text-muted)]">What happened<InfoTip help={sectionHelp.ledger.happened} /></h3>
          <ol className="space-y-2">
            {data.events.map((ev) => {
              const w = describeEvent(ev, cloud, e.kind)
              return (
                <li key={ev.seq} className="flex items-start justify-between gap-3">
                  <span><span className="font-medium">{w.title}</span>{w.note && <span className="block text-xs text-[var(--color-text-muted)]">{w.note}</span>}</span>
                  <time dateTime={ev.occurredAt} className="shrink-0 font-mono text-xs text-[var(--color-text-muted)]">{new Date(ev.occurredAt).toLocaleTimeString('en-GB')}</time>
                </li>
              )
            })}
          </ol>
        </section>

        <section aria-label="Evidence">
          <h3 className="mb-1 flex items-center text-xs font-semibold uppercase tracking-wide text-[var(--color-text-muted)]">Evidence<InfoTip help={sectionHelp.ledger.evidence} /></h3>
          <dl className="grid grid-cols-[110px_1fr] gap-y-1">
            <dt className="text-[var(--color-text-muted)]">Entry</dt><dd className="break-all font-mono text-xs">{e.id}</dd>
            <dt className="text-[var(--color-text-muted)]">Recovery ID</dt><dd className="break-all font-mono text-xs">{data.recoveryMarker ?? (data.markerApplied ? '—' : 'not applied')}</dd>
            {last && (<><dt className="text-[var(--color-text-muted)]">Hash · previous</dt><dd className="font-mono text-xs" title={`${last.entryHash} · ${last.prevHash}`}>{shortHash(last.entryHash)} · {shortHash(last.prevHash)}</dd></>)}
          </dl>
          <div className="mt-3 rounded-xl border border-[var(--color-border)] p-3">
            <button type="button" onClick={() => void check()} disabled={chain.busy} className="flex items-center gap-2 font-medium text-[var(--color-primary-700)] hover:underline disabled:opacity-60">
              <Hash className="h-4 w-4" aria-hidden="true" /> {chain.busy ? 'Checking…' : 'Verify the chain'}
            </button>
            {chain.result?.isValid && (
              <p role="status" className="mt-2 flex items-start gap-2 text-sm"><ShieldCheck className="mt-0.5 h-4 w-4 text-[var(--color-success)]" aria-hidden="true" />
                <span><b>Chain verified</b> — {chain.result.eventsChecked.toLocaleString()} events, unbroken. Verifiable offline with <code>verify-recovery-chain.py</code>.</span></p>
            )}
            {chain.result && !chain.result.isValid && (
              <p role="alert" className="mt-2 text-sm text-[var(--color-error)]"><b>The chain does not verify</b>{chain.result.firstDivergentSeq !== null ? ` — first divergence at event ${chain.result.firstDivergentSeq}` : ''}. {chain.result.reason}</p>
            )}
            {chain.failed && <p role="alert" className="mt-2 text-sm text-[var(--color-error)]">ServiceHub couldn’t run the check.</p>}
          </div>
        </section>
      </div>

      <div className="border-t border-[var(--color-border)] p-4 text-sm">
        <div className="flex flex-wrap gap-4 font-medium">
          {e.dlqMessageId !== null && <Link to={links ? links.message(e.dlqMessageId) : homeParams('replayed')} className="text-[var(--color-primary-700)] hover:underline">{links ? 'Open the message →' : 'Open in Home →'}</Link>}
          {e.dlqMessageId !== null && source?.status === 'active' && links?.replay !== null && <Link to={links ? (links.replay as (id: number) => string)(e.dlqMessageId) : `?${new URLSearchParams({ ...Object.fromEntries(new URLSearchParams(search)), modal: 'replay', replay: String(e.dlqMessageId) })}`} className="text-[var(--color-primary-700)] hover:underline">Replay…</Link>}
        </div>
        {e.dlqMessageId !== null && source && source.status !== 'active' && (
          <p className="mt-2 text-xs text-[var(--color-text-muted)]">
            This message is no longer in the dead-letter queue, so there is nothing to replay. If it failed again it is a new dead letter with its own entry.
          </p>
        )}
        <p className="mt-2 text-xs text-[var(--color-text-muted)]">{links ? 'Nothing here acts on its own: the dialogs it opens run the same safety checks as anywhere else.' : 'Replay opens over this page and runs the same safety checks as anywhere else; “Open in Home” leaves for Simple.'}</p>
      </div>
    </aside>
  )
}

/**
 * Export evidence (unit 6.12): a read, so Advanced may offer it. The whole chain inside the window — the cloud and namespace
 * pickers do not narrow it, because leaving events out would break the chain — and it says so.
 */
export function ExportEvidence({ window }: { window: RecoveryWindow }) {
  const [state, setState] = useState<{ busy: boolean; saved?: string; failed?: string }>({ busy: false })
  const run = async () => {
    setState({ busy: true })
    try {
      setState({ busy: false, saved: await exportEvidence(window) })
    } catch (e) {
      const status = (e as { response?: { status?: number } })?.response?.status
      setState({ busy: false, failed: status === 403 ? 'This key can’t export the ledger: it is limited to some namespaces, and the ledger is one chain across all of them.' : 'ServiceHub couldn’t export the ledger. Nothing was changed — try again.' })
    }
  }
  return (
    <div className="flex flex-col items-end">
      <button
        type="button"
        onClick={() => void run()}
        disabled={state.busy}
        title="Every event in this window, from every cloud and namespace, in chain order — check it anywhere with verify-recovery-chain.py."
        className="inline-flex items-center gap-1.5 rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-1.5 font-medium hover:bg-[var(--color-surface-muted)] disabled:opacity-60"
      >
        <Download className="h-4 w-4" aria-hidden="true" /> {state.busy ? 'Exporting…' : 'Export evidence'}
      </button>
      <p aria-live="polite" className="mt-1 max-w-[18rem] text-right text-[11px] text-[var(--color-text-muted)]">
        {state.saved && <>Saved <code>{state.saved}</code> — every cloud and namespace, in chain order.</>}
        {state.failed && <span role="alert" className="text-[var(--color-error)]">{state.failed}</span>}
      </p>
    </div>
  )
}
