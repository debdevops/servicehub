import { Bot, Check, CircleAlert, Clock, FileText, TriangleAlert, X } from 'lucide-react'
import { useQueries } from '@tanstack/react-query'
import { useMe } from '../../hooks/useIdentity'
import { permission } from '../../lib/permissions'
import { UnlockHint } from '../UnlockHint'
import { NotAllowed } from '../ui/NotAllowed'
import { useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useApprovePending, useDeclinePending, usePendingWork } from '../../hooks/usePendingWork'
import { useReplayProposal } from '../../hooks/useReplay'
import { useNamespaces } from '../../hooks/useNamespaces'
import type { PendingWorkItem } from '../../lib/api/pendingWork'
import { formatAge } from '../../lib/format'
import { fetchDeadLetter, type DeadLetter } from '../../lib/api/deadLetters'
import { Collapsible } from '../ui/Collapsible'
import { InfoTip } from '../ui/InfoTip'
import { sectionHelp } from '../../content/sections'
import { providerLabel } from '../../lib/providers'
import { environmentMeta } from '../provider/scopeChoice'
import type { OverlayBodyProps } from '../overlays/registry'
import { RetryLink } from '../ui/RetryLink'
import { Skeleton } from '../ui/Skeleton'

/**
 * Approve and Decline (5.10) — `?modal=approve&group=<cloud>:<namespace>` or `&entry=<id>`. One modal, five doors: the bell,
 * the Needs-you strip, the toast, the Agents timeline and the Ledger's Waiting tab.
 *
 * - Why it asked instead of acting, in one plain sentence (the server's words for the gate's reason code).
 * - The waiting items, all ticked. The safety checks — the same ones the Replay modal shows.
 * - **Approve N** replays each through the one gated route, as you, and records your name. **Decline…** needs a reason and
 *   deletes nothing. **Not now** changes nothing: the items stay in the bell (R7).
 */
export default function ApproveModal({ close }: OverlayBodyProps) {
  const [params] = useSearchParams()
  const group = params.get('group')
  const entry = params.get('entry')
  const pending = usePendingWork()
  const waiting = (pending.data?.items ?? []).filter((i) =>
    i.kind === 'approval' && (entry ? i.entryId === entry : group ? `${i.provider ?? '?'}:${i.namespaceId ?? '?'}` === group : true))

  const [unticked, setUnticked] = useState<ReadonlySet<string>>(new Set())
  const [shown, setShown] = useState(PAGE)
  const [progress, setProgress] = useState(0)
  const [declining, setDeclining] = useState(false)
  const [why, setWhy] = useState('')
  const approve = useApprovePending()
  const decline = useDeclinePending()
  const proposal = useReplayProposal(waiting[0]?.dlqMessageId ?? null)
  const me = useMe()
  const namespaces = useNamespaces()

  if (approve.isPending) return <Sending done={progress} total={approve.variables?.entryIds.length ?? 0} />
  if (approve.data) return <Approved results={approve.data} close={close} />
  if (decline.isSuccess) {
    return (
      <div className="space-y-4 text-sm" role="status">
        <p className="flex items-center gap-2 font-semibold"><Check className="h-4 w-4 text-[var(--color-success)]" aria-hidden="true" /> Declined, with your reason recorded in the Ledger.</p>
        <p className="text-[var(--color-text-muted)]">Nothing was deleted. The Agent won’t ask about these messages again unless they fail again.</p>
        <button type="button" onClick={close} className="rounded-lg border border-[var(--color-border)] px-3.5 py-2 font-semibold">Close</button>
      </div>
    )
  }

  if (pending.isPending) return <Skeleton label="Reading what is waiting…" rows={3} />
  if (pending.isError) return <p role="alert" className="text-sm">ServiceHub couldn’t read what is waiting. <RetryLink onRetry={() => void pending.refetch()} /></p>
  if (waiting.length === 0) {
    return (
      <div className="space-y-3 text-sm">
        <p className="font-semibold">Nothing here is waiting any more.</p>
        <p className="text-[var(--color-text-muted)]">Someone may already have answered it, or the messages left the dead-letter queue.</p>
        <button type="button" onClick={close} className="rounded-lg border border-[var(--color-border)] px-3.5 py-2 font-semibold">Close</button>
      </div>
    )
  }

  const first = waiting[0]
  const chosen = waiting.filter((i) => !unticked.has(i.id))
  const cloud = first.provider ? providerLabel[first.provider] : 'This cloud'
  const toggle = (id: string) => setUnticked((u) => { const n = new Set(u); if (n.has(id)) n.delete(id); else n.add(id); return n })
  const busy = approve.isPending || decline.isPending
  const allTicked = chosen.length === waiting.length
  const toggleAll = () => setUnticked(allTicked ? new Set(waiting.map((i) => i.id)) : new Set())
  const visible = waiting.slice(0, shown)
  // Said from the namespace's capability, never the cloud's name.
  const canProve = namespaces.data?.find((n) => n.id === first.namespaceId)?.capabilities?.canProveDlqAbsence
  const may = permission(me.data, 'Approver', 'answer what the Agent asked', { recover: true, namespaceId: first.namespaceId ?? undefined })

  return (
    <div className="space-y-5 text-sm">
      <div className="flex items-start gap-3">
        <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-[#fffbeb] text-[#d97706]"><Clock className="h-5 w-5" aria-hidden="true" /></span>
        <div>
          <p className="text-lg font-bold">The Agent stopped and asked you</p>
          <p className="text-[var(--color-text-muted)]">
            {waiting.length} {waiting.length === 1 ? 'replay' : 'replays'} waiting · {cloud}{first.namespaceName ? ` · ${first.namespaceName}` : ''}
            {first.environment && <span className={`ml-2 rounded-full px-2 py-0.5 text-[11px] font-semibold ${environmentMeta[first.environment].chip}`}>{environmentMeta[first.environment].label}</span>}
          </p>
        </div>
      </div>

      <section className="rounded-xl border border-[#bae6fd] bg-[#f0f9ff] p-4">
        <Collapsible title="Why it asked instead of acting" help={sectionHelp.approve.why}>
          <p className="flex items-start gap-2"><Bot className="mt-0.5 h-4 w-4 shrink-0 text-[var(--color-primary-700)]" aria-hidden="true" /> <span>{first.reason}</span></p>
          {first.ruleName && <p className="mt-1 text-xs text-[var(--color-text-muted)]">Asked on behalf of the rule “{first.ruleName}”. <span className="font-mono">{first.reasonCode}</span></p>}
        </Collapsible>
      </section>

      <section aria-label="Waiting for you">
        <div className="mb-1.5 flex items-center gap-3">
          <h3 className="flex items-center text-[10.5px] font-bold uppercase tracking-[0.6px] text-[var(--color-text-muted)]">Waiting for you<InfoTip help={sectionHelp.approve.waiting} /></h3>
          <span className="ml-auto text-xs text-[var(--color-text-muted)]">{chosen.length} of {waiting.length} ticked</span>
        </div>
        <div className="overflow-x-auto rounded-xl border border-[var(--color-border)]">
          <table className="w-full text-left text-[12.5px]">
            <caption className="sr-only">Replays waiting for your answer</caption>
            <thead className="bg-[var(--color-surface-muted)] text-[11px] uppercase tracking-wide text-[var(--color-text-muted)]">
              <tr>
                <th scope="col" className="w-8 px-3 py-1.5">
                  <input type="checkbox" checked={allTicked} onChange={toggleAll} className="h-4 w-4" aria-label={`Select all ${waiting.length} waiting replays`} />
                </th>
                <th scope="col" className="px-3 py-1.5">Message</th>
                <th scope="col" className="px-3 py-1.5">What went wrong</th>
                <th scope="col" className="px-3 py-1.5 text-right">Waiting</th>
              </tr>
            </thead>
            <Items items={visible} unticked={unticked} onToggle={toggle} />
          </table>
        </div>
        {waiting.length > shown && (
          <p className="mt-2 text-xs text-[var(--color-text-muted)]">
            Showing {shown} of {waiting.length}.{' '}
            <button type="button" onClick={() => setShown((n) => n + PAGE)} className="font-semibold text-[var(--color-primary-700)] hover:underline">Show {Math.min(PAGE, waiting.length - shown)} more</button>
            {' · '}
            <button type="button" onClick={() => setShown(waiting.length)} className="font-semibold text-[var(--color-primary-700)] hover:underline">Show all</button>
            {' — ticking “select all” covers every one, shown or not.'}
          </p>
        )}
      </section>

      {proposal.isPending && proposal.fetchStatus !== 'idle' && <Skeleton label="Working out the safety checks…" rows={3} />}
      {proposal.isError && (
        <p role="alert" className="rounded-xl border border-[var(--color-warning)] bg-[var(--color-warning-light)] px-4 py-3">
          ServiceHub couldn’t show the safety checks just now. Approving still runs them on every message. <RetryLink onRetry={() => void proposal.refetch()} />
        </p>
      )}
      {proposal.data && (
        <section>
          <Collapsible title="Safety checks" help={sectionHelp.approve.checks} summary={`${proposal.data.checks.filter((c) => c.state === 'passed').length} of ${proposal.data.checks.length} passed`} defaultOpen={proposal.data.checks.some((c) => c.state !== 'passed')}>
          <ul className="divide-y divide-[var(--color-border)] rounded-xl border border-[var(--color-border)]">
            {proposal.data.checks.map((c) => (
              <li key={c.id} className="flex items-start gap-2 px-4 py-2.5">
                {c.state === 'passed'
                  ? <Check className="mt-0.5 h-4 w-4 shrink-0 text-[var(--color-success)]" aria-label="Passed" />
                  : c.state === 'warning'
                    ? <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0 text-[#d97706]" aria-label="Warning" />
                    : <CircleAlert className="mt-0.5 h-4 w-4 shrink-0 text-[#dc2626]" aria-label="Blocked" />}
                <span>{c.label}{c.detail ? ` — ${c.detail}` : ''}</span>
              </li>
            ))}
          </ul>
          </Collapsible>
        </section>
      )}

      {/* A cloud that can't prove a fix is the real blocker whatever code the escalation carries: AWS escalations arrive as
          AUTONOMY_GRANT_INSUFFICIENT, and "after 10 at 95%" would be a promise that cloud can never keep. */}
      <UnlockHint code={canProve === false ? 'PROVIDER_CANNOT_VERIFY_ABSENCE' : first.reasonCode} />


      {declining && (
        <section className="rounded-xl border border-[#fecaca] bg-[#fef2f2] p-4">
          <label className="block font-semibold text-[#7f1d1d]" htmlFor="decline-why">Why not? <span className="font-normal">(recorded with your name)</span></label>
          <textarea id="decline-why" value={why} onChange={(e) => setWhy(e.target.value)} maxLength={500} rows={2}
            className="mt-2 w-full rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2" placeholder="e.g. the downstream service is still down" />
          <div className="mt-2 flex gap-2">
            <button type="button" disabled={!why.trim() || busy || chosen.length === 0}
              onClick={() => decline.mutate({ entryIds: chosen.map((i) => i.entryId!), reason: why.trim() })}
              className="rounded-lg bg-[#dc2626] px-3.5 py-2 font-semibold text-white disabled:opacity-50">
              Decline {chosen.length}
            </button>
            <button type="button" onClick={() => setDeclining(false)} className="rounded-lg border border-[var(--color-border)] px-3.5 py-2 font-semibold">Cancel</button>
          </div>
        </section>
      )}
      {(decline.isError || approve.isError) && <p role="alert" className="text-[#b91c1c]">That didn’t go through. Nothing changed — try again.</p>}

      <footer className="sticky bottom-0 z-10 -mx-5 -mb-4 flex flex-wrap items-center justify-end gap-2 border-t border-[var(--color-border)] bg-[var(--color-surface)] px-5 py-3">
        <span className="flex w-full items-center gap-1.5 text-xs text-[var(--color-text-muted)]"><FileText className="h-3.5 w-3.5" aria-hidden="true" /> Recorded with your name</span>
        <button type="button" onClick={close} className="rounded-lg border border-[var(--color-border)] px-3.5 py-2 font-semibold">Not now</button>
        {!declining && <button type="button" onClick={() => setDeclining(true)} disabled={busy || !may.allowed} className="rounded-lg border border-[#fecaca] px-3.5 py-2 font-semibold text-[#b91c1c]">Decline…</button>}
        <button type="button" disabled={busy || !may.allowed || chosen.length === 0}
          onClick={() => { setProgress(0); approve.mutate({ entryIds: chosen.map((i) => i.entryId!), onProgress: setProgress }) }}
          className="inline-flex items-center gap-1.5 rounded-lg bg-[#b45309] px-4 py-2 font-bold text-white hover:bg-[#92400e] disabled:opacity-50">
          <Check className="h-4 w-4" aria-hidden="true" /> {approve.isPending ? 'Replaying…' : `Approve ${chosen.length} ${chosen.length === 1 ? 'replay' : 'replays'}`}
        </button>
      </footer>
      <NotAllowed reason={may.reason} />
    </div>
  )
}

/** How many waiting replays show at first; more on request. Every one can still be ticked at once. */
const PAGE = 25

/** One row per waiting replay, named by what a person recognises: the message's own id, its queue and what failed. */
function Items({ items, unticked, onToggle }: { items: readonly PendingWorkItem[]; unticked: ReadonlySet<string>; onToggle: (id: string) => void }) {
  const results = useQueries({
    queries: items.map((i) => ({
      queryKey: ['dead-letter', i.dlqMessageId],
      queryFn: () => fetchDeadLetter(i.dlqMessageId as number),
      enabled: i.dlqMessageId !== null,
      staleTime: 30_000,
      retry: false,
    })),
  })
  const now = new Date()
  return (
    <tbody>
      {items.map((i, n) => {
        const m: DeadLetter | undefined = results[n]?.data?.item
        const reason = m?.deadLetterReason ?? i.deadLetterReason
        return (
          <tr key={i.id} className="border-t border-[var(--color-border)] align-top">
            <td className="px-3 py-2"><input type="checkbox" checked={!unticked.has(i.id)} onChange={() => onToggle(i.id)} className="h-4 w-4" aria-label={`Include ${i.entity ?? 'this message'}`} /></td>
            <td className="px-3 py-2">
              <p className="font-mono text-[11.5px] font-semibold [overflow-wrap:anywhere]">{m?.messageId ?? (i.dlqMessageId !== null ? `Message #${i.dlqMessageId}` : 'Message')}</p>
              <p className="text-xs text-[var(--color-text-muted)]">
                in <span className="font-mono">{m?.topicName ? `${m.topicName} › ` : ''}{m?.entityName ?? i.entity ?? 'a queue'}</span>
                {m ? ` · tried ${m.deliveryCount}×` : ''}
              </p>
            </td>
            <td className="px-3 py-2">
              {reason && <span className="rounded-full bg-[#fef3c7] px-2.5 py-0.5 text-[11px] font-semibold text-[#92400e]">{reason}</span>}
              {m?.deadLetterErrorDescription && <p className="mt-1 line-clamp-2 text-xs text-[var(--color-text-muted)]" title={m.deadLetterErrorDescription}>{m.deadLetterErrorDescription}</p>}
            </td>
            <td className="whitespace-nowrap px-3 py-2 text-right text-xs text-[var(--color-text-muted)]">{formatAge(i.since, now)}</td>
          </tr>
        )
      })}
    </tbody>
  )
}

/** While the replays go out one by one: how far along, so a long batch never looks frozen. */
function Sending({ done, total }: { done: number; total: number }) {
  const pct = total === 0 ? 0 : Math.min(100, Math.round((done / total) * 100))
  return (
    <div className="space-y-4 text-sm" role="status">
      <p className="text-base font-semibold">Sending {total.toLocaleString()} {total === 1 ? 'replay' : 'replays'}…</p>
      <div>
        <div className="flex items-baseline justify-between"><b>{done.toLocaleString()} of {total.toLocaleString()} sent</b><span className="text-[var(--color-text-muted)]">{(total - done).toLocaleString()} to go</span></div>
        <div className="mt-2 h-2.5 overflow-hidden rounded-full bg-[var(--color-surface-muted)]" role="progressbar" aria-valuenow={pct} aria-valuemin={0} aria-valuemax={100} aria-label="Approval progress">
          <div className="h-full rounded-full bg-[var(--color-primary-600)] transition-all" style={{ width: `${pct}%` }} />
        </div>
      </div>
      <p className="text-[var(--color-text-muted)]">Each goes through the safety checks on its own. You can leave this open; nothing is skipped.</p>
    </div>
  )
}

function Approved({ results, close }: { results: readonly { entryId: string; ok: boolean; error?: string }[]; close: () => void }) {
  const ok = results.filter((r) => r.ok).length
  const failed = results.filter((r) => !r.ok)
  return (
    <div className="space-y-4 text-sm" role="status">
      <p className="flex items-center gap-2 text-base font-semibold"><Check className="h-5 w-5 text-[var(--color-success)]" aria-hidden="true" /> {ok} {ok === 1 ? 'replay' : 'replays'} sent, each recorded with your name.</p>
      <p className="text-[var(--color-text-muted)]">Each is now being watched to see whether it stays fixed — follow it on the Replayed tab.</p>
      {failed.length > 0 && (
        <ul className="space-y-1 rounded-xl border border-[#fecaca] bg-[#fef2f2] p-3">
          {failed.map((f) => <li key={f.entryId} className="flex items-start gap-2"><X className="mt-0.5 h-4 w-4 shrink-0 text-[#dc2626]" aria-hidden="true" />{f.error ?? 'It could not be replayed.'} It is still waiting.</li>)}
        </ul>
      )}
      <div className="flex gap-2">
        <Link to="/?tab=replayed" onClick={close} className="rounded-lg bg-[var(--color-primary-600)] px-3.5 py-2 font-semibold text-white">See them in Replayed</Link>
        <button type="button" onClick={close} className="rounded-lg border border-[var(--color-border)] px-3.5 py-2 font-semibold">Close</button>
      </div>
    </div>
  )
}
