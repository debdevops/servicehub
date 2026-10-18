import { useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import type { Me } from '../../../lib/api/identity'
import type { PendingWorkItem } from '../../../lib/api/pendingWork'
import type { CoverageRow } from '../../../lib/api/coverage'
import { formatAge } from '../../../lib/format'
import { permission } from '../../../lib/permissions'
import { usePagedList } from '../../../hooks/usePagedList'
import { Pager } from '../../../components/ui/Pager'
import { cloudLabel } from '../../lib/clouds'
import { familyWords } from '../../lib/words'

type Tab = 'all' | 'approvals' | 'handOffs'
const th = 'px-3 py-2 text-left text-xs font-semibold text-[var(--color-text-muted)]'
const td = 'px-3 py-2.5 align-top text-sm'

const isMine = (i: PendingWorkItem) => i.kind === 'approval' || i.kind === 'unresolved'
const typeOf = (i: PendingWorkItem) => i.type ?? (i.kind === 'unresolved' ? 'handOff' : 'approval')

/**
 * Items that need a person (build-plan unit C1), from the one pending-work read. Review opens the approval dialog and View the
 * hand-off panel (both are carried in the URL; F-units build them). The owner column says "All channels": a notification channel has
 * no component field in 5.0.0 (decision F2, option A), so it is not drawn as if each item had its own.
 */
export function AttentionTable({ items, coverage, me, showCloud = false }: { items: readonly PendingWorkItem[]; coverage: readonly CoverageRow[]; me: Me | undefined; showCloud?: boolean }) {
  const { pathname } = useLocation()
  const [tab, setTab] = useState<Tab>('all')
  const mine = items.filter(isMine)
  const approvals = mine.filter((i) => typeOf(i) === 'approval')
  const handOffs = mine.filter((i) => typeOf(i) === 'handOff')
  const { items: shown, pager } = usePagedList(tab === 'approvals' ? approvals : tab === 'handOffs' ? handOffs : mine, tab)
  const now = new Date()
  const nameOf = (key: string | null | undefined) => coverage.find((r) => r.componentKey === key)?.name ?? '—'
  const tabs: { id: Tab; text: string }[] = [
    { id: 'all', text: `All (${mine.length})` },
    { id: 'approvals', text: `Pending approvals (${approvals.length})` },
    { id: 'handOffs', text: `Hand-offs (${handOffs.length})` },
  ]

  return (
    <section aria-labelledby="attention-title" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
      <h2 id="attention-title" className="px-4 pt-4 text-base font-semibold">Items that need your attention ({mine.length})</h2>
      <div role="tablist" aria-label="Items that need your attention" className="flex gap-5 border-b border-[var(--color-border)] px-4">
        {tabs.map((t) => (
          <button key={t.id} role="tab" type="button" aria-selected={tab === t.id} onClick={() => setTab(t.id)}
            className={`-mb-px border-b-2 px-1 py-2.5 text-sm font-semibold ${tab === t.id ? 'border-[var(--color-primary-600)] text-[var(--color-primary-700)]' : 'border-transparent text-[var(--color-text-muted)]'}`}>
            {t.text}
          </button>
        ))}
      </div>
      {pager.total === 0 ? (
        <p className="px-4 py-6 text-sm text-[var(--color-text-muted)]">Nothing here needs a person right now.</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead>
              <tr>
                {showCloud && <th className={th}>Cloud</th>}
                <th className={th}>Message</th><th className={th}>Component</th><th className={th}>Entity</th><th className={th}>Reason</th>
                <th className={th}>Family (if hand-off)</th><th className={th}>Age</th><th className={th}>Type</th><th className={th}>What’s needed</th>
                <th className={th}>Owner (notification channel)</th><th className={th}>Actions</th>
              </tr>
            </thead>
            <tbody>
              {shown.map((i) => {
                const hand = typeOf(i) === 'handOff'
                const may = permission(me, 'Approver', 'approve or decline a replay', { recover: true, namespaceId: i.namespaceId ?? undefined })
                const open = `${pathname}?${hand ? 'panel=hand-off' : 'dialog=review'}&entry=${encodeURIComponent(i.entryId ?? i.id)}`
                return (
                  <tr key={i.id} className="border-t border-[var(--color-border)]">
                    {showCloud && <td className={td}>{i.provider ? cloudLabel[i.provider] : '—'}</td>}
                    <td className={`${td} font-mono text-xs`}>{i.dlqMessageId !== null ? `#${i.dlqMessageId}` : '—'}</td>
                    <td className={td}>{nameOf(i.componentKey)}{i.namespaceName && <span className="block text-xs text-[var(--color-text-muted)]">{i.namespaceName}</span>}</td>
                    <td className={td}>{i.entity ?? '—'}</td>
                    <td className={td}>{i.deadLetterReason ?? i.reason}</td>
                    <td className={td}>{hand && familyWords(i.family) ? <span className="rounded-md bg-purple-50 px-2 py-0.5 text-xs font-semibold text-purple-800">{familyWords(i.family)}</span> : '—'}</td>
                    <td className={td}>{formatAge(i.since, now)}</td>
                    <td className={`${td} font-semibold ${hand ? 'text-amber-700' : 'text-red-700'}`}>{hand ? 'Hand-off' : 'Approval'}</td>
                    <td className={td}>{hand ? 'Investigate and decide' : 'Approve or reject'}</td>
                    <td className={td}>All channels</td>
                    <td className={td}>
                      {hand || may.allowed
                        ? <Link to={open} className="rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold text-[var(--color-primary-700)] hover:bg-[var(--color-primary-50)]">{hand ? 'View' : 'Review'}</Link>
                        : <span title={may.reason ?? undefined} className="text-xs text-[var(--color-text-muted)]">Needs an Approver</span>}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
      <Pager {...pager} label="items that need your attention" />
    </section>
  )
}
