import { Bot, Check, CircleAlert, Clock, FileText, Search, TriangleAlert, X } from 'lucide-react'
import { useQueries, useQuery } from '@tanstack/react-query'
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
import type { CloudProvider } from '../../lib/api/namespaces'
import { formatAge, formatAgo, formatBytes, formatWhen } from '../../lib/format'
import { fetchDeadLetter, type DeadLetter } from '../../lib/api/deadLetters'
import { fetchSignatures, type Signature } from '../../lib/api/signatures'
import { useDeadLetter } from '../../hooks/useDeadLetter'
import { Collapsible } from '../ui/Collapsible'
import { InfoTip } from '../ui/InfoTip'
import { sectionHelp } from '../../content/sections'
import { providerLabel } from '../../lib/providers'
import { environmentMeta } from '../provider/scopeChoice'
import type { OverlayBodyProps } from '../overlays/registry'
import { RetryLink } from '../ui/RetryLink'
import { Skeleton } from '../ui/Skeleton'
import { Select } from '../ui/Select'

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
  const [groupProvider, groupNamespace] = group ? group.split(':') : []
  const pending = usePendingWork(group && groupProvider !== '?' && groupNamespace && groupNamespace !== '?' ? { provider: groupProvider as CloudProvider, namespaceId: groupNamespace } : {})
  const waiting = (pending.data?.items ?? []).filter((i) =>
    i.kind === 'approval' && (entry ? i.entryId === entry : group ? `${i.provider ?? '?'}:${i.namespaceId ?? '?'}` === group : true))

  const [unticked, setUnticked] = useState<ReadonlySet<string>>(new Set())
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState<number>(DEFAULT_PAGE_SIZE)
  const [progress, setProgress] = useState(0)
  const [declining, setDeclining] = useState(false)
  const [why, setWhy] = useState('')
  const [tab, setTab] = useState<'messages' | 'checks'>('messages')
  const [search, setSearch] = useState('')
  const [picked, setPicked] = useState<string | null>(null)
  const approve = useApprovePending()
  const decline = useDeclinePending()
  const head = waiting[0]
  const proposal = useReplayProposal(head?.dlqMessageId ?? null)
  const me = useMe()
  const namespaces = useNamespaces()
  const record = useTrackRecord(head)

  if (approve.isPending) return <Sending done={progress} total={approve.variables?.entryIds.length ?? 0} />
  if (approve.data) return <Approved results={approve.data} close={close} stillWaiting={pending.isFetching ? null : (entry ? waiting.length : (pending.data?.total ?? null))} />
  if (decline.isSuccess) {
    const n = decline.variables?.entryIds.length ?? 0
    const left = pending.isFetching ? null : (entry ? waiting.length : (pending.data?.total ?? null))
    return (
      <div className="space-y-4 text-sm" role="status">
        <p className="flex items-center gap-2 text-base font-semibold"><Check className="h-5 w-5 text-[var(--color-success)]" aria-hidden="true" /> Declined {n.toLocaleString()} {n === 1 ? 'replay' : 'replays'}.</p>
        <ul className="list-disc space-y-1 pl-5 text-[var(--color-text-muted)]">
          <li>Nothing was replayed and nothing was deleted; the {n === 1 ? 'message is' : 'messages are'} still in the dead-letter queue.</li>
          <li>Your reason is recorded in the Ledger with your name.</li>
          <li>The Agent won’t ask about {n === 1 ? 'it' : 'them'} again unless {n === 1 ? 'it fails' : 'they fail'} again.</li>
        </ul>
        {left !== null && left > 0 && <p><b>{left.toLocaleString()}</b> {left === 1 ? 'replay is' : 'replays are'} still waiting for you. Open Approve again for the next ones.</p>}
        {left === 0 && <p>Nothing else is waiting for you.</p>}
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
  const needle = search.trim().toLowerCase()
  const matching = needle
    ? waiting.filter((i) => [i.dlqMessageId?.toString(), i.entity, i.deadLetterReason].some((v) => v?.toLowerCase().includes(needle)))
    : waiting
  const pages = Math.max(1, Math.ceil(matching.length / pageSize))
  const at = Math.min(page, pages)
  const visible = matching.slice((at - 1) * pageSize, at * pageSize)
  const selected = waiting.find((i) => i.id === picked) ?? visible[0] ?? first
  // Said from the namespace's capability, never the cloud's name.
  const canProve = namespaces.data?.find((n) => n.id === first.namespaceId)?.capabilities?.canProveDlqAbsence
  const may = permission(me.data, 'Approver', 'answer what the Agent asked', { recover: true, namespaceId: first.namespaceId ?? undefined })
  // The API may hold more than this window carries; say so rather than let "all" mean "some".
  const total = entry ? waiting.length : Math.max(pending.data?.total ?? 0, waiting.length)
  const restStay = total - chosen.length
  const now = new Date()
  const checksSummary = proposal.data ? `${proposal.data.checks.filter((c) => c.state === 'passed').length} of ${proposal.data.checks.length} passed` : undefined

  return (
    <div className="space-y-4 text-sm">
      <div className="flex items-start gap-3">
        <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-[#fffbeb] text-[#d97706]"><Clock className="h-5 w-5" aria-hidden="true" /></span>
        <div>
          <p className="text-lg font-bold">The Agent stopped and asked you</p>
          <p className="text-[var(--color-text-muted)]">
            {total.toLocaleString()} {total === 1 ? 'replay' : 'replays'} waiting{total > waiting.length ? ` (showing the first ${waiting.length.toLocaleString()})` : ''} · {cloud}{first.namespaceName ? ` · ${first.namespaceName}` : ''}
            {first.environment && <span className={`ml-2 rounded-full px-2 py-0.5 text-[11px] font-semibold ${environmentMeta[first.environment].chip}`}>{environmentMeta[first.environment].label}</span>}
          </p>
        </div>
      </div>

      <BeforeYouApprove why={first.reason} rule={first.ruleName} code={first.reasonCode} record={record} count={chosen.length} canProve={canProve} partial={total > waiting.length ? { shown: waiting.length, total } : null} />

      <div className="grid grid-cols-2 gap-2 lg:grid-cols-4" aria-label="What is being asked">
        <Fact label="Messages" value={String(chosen.length)} sub={`of ${total.toLocaleString()} waiting`} />
        <Fact label="What failed" value={first.deadLetterReason ?? 'No reason recorded'} sub={`${waiting.filter((i) => i.deadLetterReason === first.deadLetterReason).length} of ${waiting.length} messages`} />
        <Fact label="Queue or topic" value={first.entity ?? 'a queue'} sub={`${cloud}${first.namespaceName ? ` · ${first.namespaceName}` : ''}`} mono />
        <Fact label="Asked by" value={first.ruleName ? 'The Agent' : 'ServiceHub'} sub={first.ruleName ? `rule “${first.ruleName}” · ${formatAgo(first.since, now)}` : formatAgo(first.since, now)} />
      </div>

      <div role="tablist" aria-label="Approve details" className="flex gap-1 border-b border-[var(--color-border)]">
        {([['messages', `Messages (${waiting.length})`], ['checks', 'Safety checks']] as const).map(([id, label]) => (
          <button key={id} type="button" role="tab" id={`approve-tab-${id}`} aria-selected={tab === id} aria-controls={`approve-panel-${id}`} onClick={() => setTab(id)}
            className={`-mb-px border-b-2 px-3 py-2 font-semibold ${tab === id ? 'border-[var(--color-primary-600)] text-[var(--color-primary-700)]' : 'border-transparent text-[var(--color-text-muted)] hover:text-[var(--color-text)]'}`}>
            {label}
          </button>
        ))}
      </div>

      {tab === 'messages' && (
        <section id="approve-panel-messages" role="tabpanel" aria-labelledby="approve-tab-messages" className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_320px]">
          <div>
            <div className="mb-1.5 flex flex-wrap items-center gap-3">
              <h3 className="flex items-center text-[10.5px] font-bold uppercase tracking-[0.6px] text-[var(--color-text-muted)]">Waiting for you<InfoTip help={sectionHelp.approve.waiting} /></h3>
              <label className="relative ml-auto block">
                <span className="sr-only">Search the waiting messages</span>
                <Search className="pointer-events-none absolute left-2.5 top-2 h-4 w-4 text-[var(--color-text-muted)]" aria-hidden="true" />
                <input type="search" value={search} onChange={(e) => { setSearch(e.target.value); setPage(1) }} placeholder="Search message ID, queue, reason…"
                  className="w-64 rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] py-1.5 pl-8 pr-2 text-xs" />
              </label>
              <span className="text-xs text-[var(--color-text-muted)]">{chosen.length} of {waiting.length} ticked</span>
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
                <Items items={visible} unticked={unticked} onToggle={toggle} selectedId={selected.id} onPick={setPicked} sameEntity={first.entity} />
              </table>
            </div>
            {matching.length === 0 && <p className="mt-2 text-xs text-[var(--color-text-muted)]">Nothing matches “{search.trim()}”. Ticked messages stay ticked.</p>}
            {matching.length > 0 && (
              <nav aria-label="Pages of waiting messages" className="mt-2 flex flex-wrap items-center gap-3 text-xs text-[var(--color-text-muted)]">
                <div className="flex items-center gap-1.5">Rows per page
                  <Select variant="inline" ariaLabel="Rows per page" value={String(pageSize)} onChange={(v) => { setPageSize(Number(v)); setPage(1) }}>
                    {PAGE_SIZES.map((n) => <option key={n} value={n}>{n}</option>)}
                  </Select>
                </div>
                <span aria-live="polite">{(at - 1) * pageSize + 1}–{Math.min(at * pageSize, matching.length)} of {matching.length}</span>
                <span className="ml-auto flex items-center gap-1">
                  <button type="button" onClick={() => setPage(at - 1)} disabled={at <= 1} aria-label="Previous page" className="rounded-md border border-[var(--color-border)] px-2 py-1 font-semibold disabled:opacity-40">‹</button>
                  <span className="px-1">Page {at} of {pages}</span>
                  <button type="button" onClick={() => setPage(at + 1)} disabled={at >= pages} aria-label="Next page" className="rounded-md border border-[var(--color-border)] px-2 py-1 font-semibold disabled:opacity-40">›</button>
                </span>
                <span className="w-full">Ticking “select all” covers every message, on every page.</span>
              </nav>
            )}
          </div>
          <MessagePanel dlqMessageId={selected.dlqMessageId} />
        </section>
      )}

      {tab === 'checks' && (
        <section id="approve-panel-checks" role="tabpanel" aria-labelledby="approve-tab-checks" className="space-y-3">
          {proposal.isPending && proposal.fetchStatus !== 'idle' && <Skeleton label="Working out the safety checks…" rows={3} />}
          {proposal.isError && (
            <p role="alert" className="rounded-xl border border-[var(--color-warning)] bg-[var(--color-warning-light)] px-4 py-3">
              ServiceHub couldn’t show the safety checks just now. Approving still runs them on every message. <RetryLink onRetry={() => void proposal.refetch()} />
            </p>
          )}
          {proposal.data && (
            <Collapsible title="Safety checks" help={sectionHelp.approve.checks} summary={checksSummary} defaultOpen>
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
              <p className="mt-2 text-xs text-[var(--color-text-muted)]">These are shown for the first message. Approving runs the same checks on every message on its own.</p>
            </Collapsible>
          )}
          {/* A cloud that can't prove a fix is the real blocker whatever code the escalation carries: AWS escalations arrive as
              AUTONOMY_GRANT_INSUFFICIENT, and "after 10 at 95%" would be a promise that cloud can never keep. */}
          <UnlockHint code={canProve === false ? 'PROVIDER_CANNOT_VERIFY_ABSENCE' : first.reasonCode} />
        </section>
      )}

      {approve.isError && <p role="alert" className="text-[#b91c1c]">That didn’t go through. Nothing changed — try again.</p>}

      <footer className="sticky bottom-0 z-10 -mx-5 -mb-4 border-t border-[var(--color-border)] bg-[var(--color-surface)] px-5 py-3">
        {declining ? (
          <section aria-label="Decline these replays" className="space-y-2.5">
            <div>
              <p className="text-base font-bold text-[#7f1d1d]">Decline {chosen.length.toLocaleString()} {chosen.length === 1 ? 'replay' : 'replays'}?</p>
              <ul className="mt-1 list-disc space-y-0.5 pl-5 text-[12.5px] text-[var(--color-text-muted)]">
                <li>Nothing is replayed and nothing is deleted. The {chosen.length === 1 ? 'message stays' : 'messages stay'} in the dead-letter queue.</li>
                <li>Your reason and your name are recorded in the Ledger.</li>
                <li>The Agent won’t ask about {chosen.length === 1 ? 'it' : 'them'} again unless {chosen.length === 1 ? 'it fails' : 'they fail'} again.</li>
                {restStay > 0 && <li>{restStay.toLocaleString()} other {restStay === 1 ? 'message stays' : 'messages stay'} waiting for you.</li>}
              </ul>
            </div>
            <label className="block text-[13px] font-semibold" htmlFor="decline-why">Why not? <span className="font-normal text-[var(--color-text-muted)]">(required — recorded with your name)</span></label>
            <textarea id="decline-why" autoFocus value={why} onChange={(e) => setWhy(e.target.value)} maxLength={500} rows={2}
              className="w-full rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2" placeholder="e.g. the downstream service is still down" />
            {decline.isError && <p role="alert" className="text-[#b91c1c]">That didn’t go through. Nothing was declined — try again.</p>}
            <div className="flex flex-wrap items-center justify-end gap-2">
              <button type="button" onClick={() => setDeclining(false)} disabled={decline.isPending} className="rounded-lg border border-[var(--color-border)] px-3.5 py-2 font-semibold">Go back</button>
              <button type="button" disabled={!why.trim() || busy || chosen.length === 0}
                onClick={() => decline.mutate({ entryIds: chosen.map((i) => i.entryId!), reason: why.trim() })}
                className="rounded-lg bg-[#dc2626] px-4 py-2 font-bold text-white disabled:opacity-50">
                {decline.isPending ? 'Declining…' : `Decline ${chosen.length} ${chosen.length === 1 ? 'replay' : 'replays'}`}
              </button>
            </div>
          </section>
        ) : (
          <div className="flex flex-wrap items-center justify-end gap-2">
            <span className="flex w-full items-center gap-1.5 text-xs text-[var(--color-text-muted)]">
              <FileText className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
              Recorded with your name. {restStay > 0 ? `${restStay.toLocaleString()} other ${restStay === 1 ? 'message stays' : 'messages stay'} waiting.` : 'Nothing else is left waiting.'}
            </span>
            <button type="button" onClick={close} className="rounded-lg border border-[var(--color-border)] px-3.5 py-2 font-semibold">Not now</button>
            <button type="button" onClick={() => setDeclining(true)} disabled={busy || !may.allowed} className="rounded-lg border border-[#fecaca] px-3.5 py-2 font-semibold text-[#b91c1c]">Decline…</button>
            <button type="button" disabled={busy || !may.allowed || chosen.length === 0}
              onClick={() => { setProgress(0); approve.mutate({ entryIds: chosen.map((i) => i.entryId!), onProgress: setProgress }) }}
              className="inline-flex items-center gap-1.5 rounded-lg bg-[#b45309] px-4 py-2 font-bold text-white hover:bg-[#92400e] disabled:opacity-50">
              <Check className="h-4 w-4" aria-hidden="true" /> {approve.isPending ? 'Replaying…' : `Approve ${chosen.length} ${chosen.length === 1 ? 'replay' : 'replays'}`}
            </button>
          </div>
        )}
      </footer>
      <NotAllowed reason={may.reason} />
    </div>
  )
}

/** What this failure has done in earlier replays, from recorded outcomes only. Null while loading, or when it cannot be found. */
function useTrackRecord(head: PendingWorkItem | undefined): Signature | null {
  const query = useQuery({
    queryKey: ['approve-track-record', head?.provider, head?.namespaceId],
    queryFn: () => fetchSignatures({ provider: head!.provider!, namespaceId: head!.namespaceId ?? undefined, days: 30, tab: 'all', sort: 'messages', page: 1, pageSize: 50 }),
    enabled: !!head?.provider,
    staleTime: 30_000,
    retry: false,
  })
  if (!head || !query.data) return null
  const entity = head.entity
  const matches = query.data.items.filter((s) => s.reason === head.deadLetterReason)
  return matches.find((s) => !entity || s.entities.some((e) => e === entity || e.includes(entity) || entity.includes(e))) ?? null
}

/** The facts to read before pressing Approve — what it does, what it does not do, and whether replaying has ever helped this failure. */
function BeforeYouApprove({ why, rule, code, record, count, canProve, partial }: { why: string; rule: string | null; code: string; record: Signature | null; count: number; canProve: boolean | undefined; partial: { shown: number; total: number } | null }) {
  // Only replays whose outcome was actually checked count: ones still inside their watch window have not stayed fixed OR come back yet.
  const fixed = record?.replays.stayedFixed ?? 0
  const replayed = fixed + (record?.replays.returned ?? 0)
  const poor = record !== null && replayed > 0 && (record.replayVerdict === 'doesnt' || (replayed >= 3 && fixed / replayed < 0.5))
  return (
    <section aria-label="Before you approve" className={`rounded-xl border p-3 ${poor ? 'border-[#fcd34d] bg-[#fffbeb]' : 'border-[var(--color-border)] bg-[var(--color-surface-muted)]'}`}>
      <Collapsible title="Before you approve" help={sectionHelp.approve.why} summary={poor ? 'this failure has not been fixed by replay before' : undefined}>
        <ul className="space-y-1.5 text-[13px]">
          <li className="flex items-start gap-2"><Bot className="mt-0.5 h-4 w-4 shrink-0 text-[var(--color-primary-700)]" aria-hidden="true" /><span><b>Why the Agent asked:</b> {why}{rule && <span className="text-xs text-[var(--color-text-muted)]"> Rule “{rule}” · <span className="font-mono">{code}</span></span>}</span></li>
          {poor && (
            <li className="flex items-start gap-2 font-semibold text-[#92400e]">
              <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
              <span>Replaying this failure hasn’t worked before: {fixed} of {replayed} earlier {replayed === 1 ? 'replay' : 'replays'} stayed fixed. These are likely to fail again unless the cause has been fixed.</span>
            </li>
          )}
          {!poor && record && replayed > 0 && <li><b>Track record:</b> {fixed} of {replayed} earlier {replayed === 1 ? 'replay' : 'replays'} of this failure stayed fixed.</li>}
          {!poor && (!record || replayed === 0) && <li><b>Track record:</b> no earlier replay of this failure has been checked yet, so there is nothing to say whether replaying helps.</li>}
          <li><b>What Approve does:</b> puts the {count === 1 ? 'message' : `${count.toLocaleString()} ticked messages`} back on {count === 1 ? 'its' : 'their'} queue now, one at a time, each through the safety checks, recorded with your name. Nothing is deleted{count > 20 ? '; a batch this size can take a few minutes, so keep this window open' : ''}.</li>
          <li><b>What it does not do:</b> it doesn’t earn this failure any trust. A person approving is the floor, not a step towards replaying without asking.</li>
          {canProve === false && <li><b>This cloud can’t confirm the fix held,</b> so each result will read “verification required”, never “verified”.</li>}
          {partial && <li><b>Only {partial.shown.toLocaleString()} of {partial.total.toLocaleString()} waiting</b> are in this window; the rest stay waiting until you answer them.</li>}
          <li><b>Not sure?</b> “Not now” changes nothing, and “Decline…” records your reason and deletes nothing.</li>
        </ul>
      </Collapsible>
    </section>
  )
}

function Fact({ label, value, sub, mono }: { label: string; value: string; sub: string; mono?: boolean }) {
  return (
    <div className="min-w-0 rounded-xl border border-[var(--color-border)] px-3 py-2.5">
      <p className="text-[10.5px] font-bold uppercase tracking-[0.6px] text-[var(--color-text-muted)]">{label}</p>
      <p className={`truncate text-[15px] font-bold ${mono ? 'font-mono text-[13px]' : ''}`} title={value}>{value}</p>
      <p className="truncate text-xs text-[var(--color-text-muted)]" title={sub}>{sub}</p>
    </div>
  )
}

/** JSON laid out for reading; anything that is not JSON is shown exactly as stored. */
function prettyJson(text: string): string {
  try { return JSON.stringify(JSON.parse(text), null, 2) } catch { return text }
}

/** The chosen message, read from the durable list — body, properties and delivery, never a guess at what is not stored. */
function MessagePanel({ dlqMessageId }: { dlqMessageId: number | null }) {
  const [view, setView] = useState<'body' | 'properties' | 'delivery'>('body')
  const detail = useDeadLetter(dlqMessageId)
  const now = new Date()
  const d = detail.data
  return (
    <aside aria-label="Message details" className="min-w-0 self-start rounded-xl border border-[var(--color-border)] p-3 lg:sticky lg:top-0">
      <h3 className="text-[10.5px] font-bold uppercase tracking-[0.6px] text-[var(--color-text-muted)]">Message details</h3>
      {dlqMessageId === null && <p className="mt-2 text-xs text-[var(--color-text-muted)]">ServiceHub has no stored copy of this message to show.</p>}
      {dlqMessageId !== null && detail.isPending && <Skeleton label="Reading the message…" rows={3} />}
      {dlqMessageId !== null && detail.isError && <p role="alert" className="mt-2 text-xs">This message isn’t in ServiceHub’s list any more. <RetryLink onRetry={() => void detail.refetch()} /></p>}
      {d && (
        <div className="mt-2 space-y-2">
          <div role="tablist" aria-label="Message view" className="flex gap-1 text-xs">
            {(['body', 'properties', 'delivery'] as const).map((v) => (
              <button key={v} type="button" role="tab" aria-selected={view === v} onClick={() => setView(v)}
                className={`rounded-md px-2 py-1 font-semibold capitalize ${view === v ? 'bg-[var(--color-primary-600)] text-white' : 'text-[var(--color-text-muted)] hover:bg-[var(--color-surface-muted)]'}`}>{v}</button>
            ))}
          </div>
          {view === 'body' && (
            <>
              <pre className="max-h-72 overflow-auto whitespace-pre-wrap break-words rounded-lg bg-[var(--color-surface-muted)] p-2 font-mono text-[11px] leading-snug">{d.bodyPreview ? prettyJson(d.bodyPreview) : 'ServiceHub has no stored body for this message.'}</pre>
              {d.bodyIsPreview && <p className="text-xs text-[var(--color-text-muted)]">This is the start of the body; ServiceHub keeps only a preview.</p>}
            </>
          )}
          {view === 'properties' && (
            <pre className="max-h-72 overflow-auto whitespace-pre-wrap break-words rounded-lg bg-[var(--color-surface-muted)] p-2 font-mono text-[11px] leading-snug">{d.applicationPropertiesJson ? prettyJson(d.applicationPropertiesJson) : 'The sender attached no properties.'}</pre>
          )}
          {view === 'delivery' && (
            <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-xs">
              <dt className="text-[var(--color-text-muted)]">Delivered</dt><dd>{d.item.deliveryCount} {d.item.deliveryCount === 1 ? 'time' : 'times'}</dd>
              <dt className="text-[var(--color-text-muted)]">Enqueued</dt><dd>{formatWhen(d.item.enqueuedTimeUtc, now)}</dd>
              <dt className="text-[var(--color-text-muted)]">First seen dead</dt><dd>{formatWhen(d.item.detectedAtUtc, now)}</dd>
              <dt className="text-[var(--color-text-muted)]">Size</dt><dd>{formatBytes(d.item.sizeInBytes)}</dd>
              <dt className="text-[var(--color-text-muted)]">Failure</dt><dd className="[overflow-wrap:anywhere]">{d.item.deadLetterReason ?? '—'}{d.item.deadLetterErrorDescription ? ` — ${d.item.deadLetterErrorDescription}` : ''}</dd>
              <dt className="text-[var(--color-text-muted)]">Content type</dt><dd className="[overflow-wrap:anywhere]">{d.contentType ?? '—'}</dd>
            </dl>
          )}
          <dl className="space-y-1 border-t border-[var(--color-border)] pt-2 text-xs">
            <div><dt className="text-[var(--color-text-muted)]">Message ID</dt><dd className="font-mono [overflow-wrap:anywhere]">{d.item.messageId}</dd></div>
            <div><dt className="text-[var(--color-text-muted)]">Correlation ID</dt><dd className="font-mono [overflow-wrap:anywhere]">{d.correlationId ?? '—'}</dd></div>
          </dl>
        </div>
      )}
    </aside>
  )
}

/** Rows per page — 5 fits a short window without scrolling, 10 is the default. Every message can still be ticked at once, on any page. */
const PAGE_SIZES = [5, 10, 20, 50] as const
const DEFAULT_PAGE_SIZE = 10

/** One row per waiting replay, named by what a person recognises: the message's own id, its queue and what failed. */
function Items({ items, unticked, onToggle, selectedId, onPick, sameEntity }: { items: readonly PendingWorkItem[]; unticked: ReadonlySet<string>; onToggle: (id: string) => void; selectedId: string; onPick: (id: string) => void; sameEntity: string | null }) {
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
        const label = m?.messageId ?? (i.dlqMessageId !== null ? `Message #${i.dlqMessageId}` : 'Message')
        const entity = m?.topicName ? `${m.topicName} › ${m.entityName}` : (m?.entityName ?? i.entity)
        const showEntity = !!entity && i.entity !== sameEntity
        return (
          // The whole row opens the message; the tick box has its own job and stops the click.
          <tr key={i.id} aria-selected={i.id === selectedId} onClick={() => onPick(i.id)}
            className={`cursor-pointer border-t border-[var(--color-border)] align-middle hover:bg-[var(--color-surface-muted)] ${i.id === selectedId ? 'bg-[#f0f9ff]' : ''}`}>
            <td className="px-3 py-2" onClick={(e) => e.stopPropagation()}><input type="checkbox" checked={!unticked.has(i.id)} onChange={() => onToggle(i.id)} className="h-4 w-4" aria-label={`Include ${i.entity ?? 'this message'}`} /></td>
            <td className="px-3 py-2">
              <button type="button" onClick={(e) => { e.stopPropagation(); onPick(i.id) }} className="block max-w-[220px] truncate text-left font-mono text-[11.5px] font-semibold hover:underline" title={label} aria-label={`Show details of ${m?.messageId ?? i.entity ?? 'this message'}`}>{label}</button>
              {showEntity || m ? (
                <p className="truncate text-xs text-[var(--color-text-muted)]">{showEntity ? <>in <span className="font-mono">{entity}</span></> : null}{m ? `${showEntity ? ' · ' : ''}tried ${m.deliveryCount}×` : ''}</p>
              ) : null}
            </td>
            <td className="px-3 py-2">{reason && <span className="rounded-full bg-[#fef3c7] px-2.5 py-0.5 text-[11px] font-semibold text-[#92400e]" title={m?.deadLetterErrorDescription ?? undefined}>{reason}</span>}</td>
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

function Approved({ results, close, stillWaiting }: { results: readonly { entryId: string; ok: boolean; error?: string }[]; close: () => void; stillWaiting: number | null }) {
  const ok = results.filter((r) => r.ok).length
  const failed = results.filter((r) => !r.ok)
  return (
    <div className="space-y-4 text-sm" role="status">
      <p className="flex items-center gap-2 text-base font-semibold"><Check className="h-5 w-5 text-[var(--color-success)]" aria-hidden="true" /> {ok} {ok === 1 ? 'replay' : 'replays'} sent, each recorded with your name.</p>
      <p className="text-[var(--color-text-muted)]">Each is now being watched to see whether it stays fixed — follow it on the Replayed tab.</p>
      {stillWaiting !== null && stillWaiting > 0 && <p><b>{stillWaiting.toLocaleString()}</b> {stillWaiting === 1 ? 'replay is' : 'replays are'} still waiting for you. Open Approve again for the next ones.</p>}
      {stillWaiting === 0 && <p>Nothing else is waiting for you.</p>}
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
