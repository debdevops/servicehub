import { useQueryClient } from '@tanstack/react-query'
import { Info } from 'lucide-react'
import { useState } from 'react'
import { Link, useLocation, useParams } from 'react-router-dom'
import { Pager } from '../../components/ui/Pager'
import { useCoverage } from '../../hooks/useCoverage'
import { useMe } from '../../hooks/useIdentity'
import { useManualApproval } from '../../hooks/useManualApproval'
import { usePagedList } from '../../hooks/usePagedList'
import { usePendingWork } from '../../hooks/usePendingWork'
import type { PendingWorkItem } from '../../lib/api/pendingWork'
import { formatAge } from '../../lib/format'
import { permission } from '../../lib/permissions'
import { ToolPageHeader } from '../components/ToolPageHeader'
import { cloudLabel, cloudOf, parseScope } from '../lib/clouds'
import { roleWords } from '../lib/words'
import { Th, ThHidden } from '../components/Tip'
import { Tip } from '../components/Tip'

const td = 'px-3 py-2.5 align-top text-sm'
/** An address carries the ticked ids; this many keeps it well inside what every browser and proxy accepts. */
const MAX_REVIEW = 100

const isApproval = (i: PendingWorkItem) => i.kind === 'approval'

/**
 * Pending approvals (build-plan unit E2; design `p07-pending-approvals`): the replays the Agent prepared and is holding until a person
 * says yes or no. The same one pending-work read as Home, Needs your attention and the menu badges. Nothing is sent from here: Review
 * opens the dialog (`?dialog=review`), which is the one place an approval or a decline happens, so there is one door, not two.
 */
export default function ApprovalsPage() {
  const scope = parseScope(useParams().cloud)
  const cloud = cloudOf(scope)
  const { pathname } = useLocation()
  const client = useQueryClient()
  const pending = usePendingWork(cloud ? { provider: cloud } : {})
  const coverage = useCoverage()
  const me = useMe()
  const isManual = useManualApproval()
  const [ticked, setTicked] = useState<ReadonlySet<string>>(new Set())

  const rows = coverage.data?.rows ?? []
  const nameOf = (key: string | null | undefined) => rows.find((r) => r.componentKey === key)?.name ?? '—'
  const items = (pending.data?.items ?? []).filter(isApproval)
  const { items: shown, pager } = usePagedList(items)
  const now = new Date()

  const mayOn = (i: PendingWorkItem) => permission(me.data, 'Approver', 'approve or decline a replay', { recover: true, namespaceId: i.namespaceId ?? undefined })
  const idOf = (i: PendingWorkItem) => i.entryId ?? i.id
  // A tick for something that is no longer waiting (answered elsewhere, refreshed away) must not be sent.
  const chosen = items.filter((i) => ticked.has(idOf(i)))
  const blocked = chosen.find((i) => !mayOn(i).allowed)
  const tooMany = chosen.length > MAX_REVIEW
  const allTicked = items.length > 0 && chosen.length === items.length
  const toggle = (id: string) => setTicked((t) => { const n = new Set(t); if (n.has(id)) n.delete(id); else n.add(id); return n })
  const reviewSelected = `${pathname}?dialog=review&entries=${chosen.map((i) => encodeURIComponent(idOf(i))).join(',')}`

  const generalMay = permission(me.data, 'Approver', 'approve or decline a replay', { recover: true })
  const role = roleWords(me.data)
  const manual = items.filter(isManual).length

  return (
    <div className="space-y-4 px-6 py-5">
      <ToolPageHeader scope={scope} title="Pending approvals" count={pending.data ? items.length : null} updatedAtMs={pending.dataUpdatedAt}
        onRefresh={() => void client.invalidateQueries({ queryKey: ['pending-work'] })}
        lookingAt="Replays the Agent prepared and is holding until a person says yes or no." />

      <p className="flex items-start gap-2.5 rounded-xl border border-[var(--color-primary-200)] bg-[var(--color-primary-50)] px-4 py-3 text-sm">
        <Info className="mt-0.5 h-4 w-4 shrink-0 text-[var(--color-primary-700)]" aria-hidden="true" />
        <span>
          <b>Approving needs the Approver role.</b>{' '}
          {generalMay.allowed ? `You have it${role ? ` (${role})` : ''}.` : `You do not have it${role ? ` (you are ${role})` : ''}; each row says “Needs an Approver” in place of Review.`}
        </span>
      </p>

      {pending.isError ? (
        <p role="alert" className="rounded-xl border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800">What is waiting for approval could not be read. Nothing is shown rather than a list that may be incomplete.</p>
      ) : pending.isPending ? (
        <p role="status" className="text-sm text-[var(--color-text-muted)]">Loading…</p>
      ) : (
        <>
          <section aria-labelledby="waiting-title" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
            <div className="flex flex-wrap items-center justify-between gap-3 px-4 pt-4">
              <h2 id="waiting-title" className="text-base font-semibold">Waiting for approval ({items.length})<Tip label="section:Waiting for approval" /></h2>
              {chosen.length > 0 && (blocked || tooMany
                ? <span className="text-xs text-[var(--color-text-muted)]">{tooMany ? `Review up to ${MAX_REVIEW} at a time.` : mayOn(blocked!).reason}</span>
                : <Link to={reviewSelected} className="rounded-lg bg-[var(--color-primary-600)] px-3.5 py-2 text-sm font-semibold text-white">Review {chosen.length} selected…</Link>)}
            </div>
            {items.length === 0 ? (
              <p className="px-4 py-6 text-sm text-[var(--color-text-muted)]">Nothing is waiting for approval right now.</p>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full">
                  <caption className="sr-only">Replays waiting for approval</caption>
                  <thead>
                    <tr>
                      <ThHidden className="w-8 px-3 py-2">
                        <input type="checkbox" className="h-4 w-4" checked={allTicked} aria-label={`Select all ${items.length} waiting replays`}
                          onChange={() => setTicked(allTicked ? new Set() : new Set(items.map(idOf)))} />
                      </ThHidden>
                      {scope === 'all' && <Th context="attention">Cloud</Th>}
                      <Th context="attention">Message</Th><Th context="attention">Component</Th><Th context="attention">Entity</Th>
                      <Th context="attention">Reason</Th><Th context="attention">Why the Agent asked</Th><Th context="attention">Waiting</Th>
                      <Th context="attention">Owner (notification channel)</Th><Th context="attention">Actions</Th>
                    </tr>
                  </thead>
                  <tbody>
                    {shown.map((i) => {
                      const may = mayOn(i)
                      const id = idOf(i)
                      return (
                        <tr key={i.id} className="border-t border-[var(--color-border)]">
                          <td className="px-3 py-2.5 align-top">
                            <input type="checkbox" className="h-4 w-4" checked={ticked.has(id)} onChange={() => toggle(id)} aria-label={`Select message ${i.dlqMessageId ?? id}`} />
                          </td>
                          {scope === 'all' && <td className={td}>{i.provider ? cloudLabel[i.provider] : '—'}</td>}
                          <td className={`${td} font-mono text-xs`}>{i.dlqMessageId !== null ? `#${i.dlqMessageId}` : '—'}</td>
                          <td className={td}>{nameOf(i.componentKey)}{i.namespaceName && <span className="block text-xs text-[var(--color-text-muted)]">{i.namespaceName}</span>}</td>
                          <td className={td}>{i.entity ?? '—'}</td>
                          <td className={td}>{i.deadLetterReason ?? '—'}</td>
                          <td className={`${td} max-w-md`}>{i.reason}</td>
                          <td className={td}>{formatAge(i.since, now)}</td>
                          <td className={td}>All channels</td>
                          <td className={td}>
                            {may.allowed
                              ? <Link to={`${pathname}?dialog=review&entry=${encodeURIComponent(id)}`} className="rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold text-[var(--color-primary-700)] hover:bg-[var(--color-primary-50)]">Review</Link>
                              : <span title={may.reason ?? undefined} className="text-xs text-[var(--color-text-muted)]">Needs an Approver</span>}
                          </td>
                        </tr>
                      )
                    })}
                  </tbody>
                </table>
              </div>
            )}
            <Pager {...pager} label="replays waiting for approval" />
            <p className="px-4 pb-4 text-xs text-[var(--color-text-muted)]">Nothing is sent until a person approves. Declining deletes nothing; the message stays in dead-letter and the reason you give is recorded.</p>
          </section>

          <section aria-labelledby="less-title" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
            <h2 id="less-title" className="text-base font-semibold">How to be asked less often<Tip label="section:How to be asked less often" /></h2>
            <p className="mt-2 text-sm">
              The Agent stops asking about a kind of failure once that fix has proved itself: enough verified replays, at a high enough success rate. That can only happen on a component that can reach Verified.
              {manual > 0 && ` ${manual === items.length ? 'Every item here is' : `${manual} of these ${manual === 1 ? 'is' : 'are'}`} on a component that cannot, so a person decides every replay there.`}
              {' '}Failure signatures shows how close each kind is.
            </p>
          </section>
        </>
      )}
    </div>
  )
}
