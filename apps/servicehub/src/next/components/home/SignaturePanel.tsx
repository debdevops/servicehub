import { useQueries, useQuery } from '@tanstack/react-query'
import { ArrowRight } from 'lucide-react'
import { Link } from 'react-router-dom'
import { fetchSignatures, fetchSignatureTrust } from '../../../lib/api/signatures'
import type { CloudProvider } from '../../../lib/api/namespaces'
import { trustWords } from '../../../lib/trustWords'
import { nextEntry, nextHref } from '../../nav/navigation'
import { Th } from '../Tip'
import { Tip } from '../Tip'

const ROWS = 4
const tone = { yes: 'bg-green-50 text-green-800', 'not yet': 'bg-amber-50 text-amber-800', no: 'bg-red-50 text-red-700' } as const
const label = { yes: 'Yes', 'not yet': 'Not yet', no: 'No' } as const

/** A header that may wrap: this panel is a 26rem column, narrower than its four labels side by side. */
const thc = 'px-1 py-2 text-left align-bottom text-xs font-semibold text-[var(--color-text-muted)]'

/**
 * The top failure signatures for one cloud (build-plan unit C1). "Agent may act" is read from each signature's trust level —
 * never assumed. When the cloud is not watched the counts cover only what was recorded at the last look, and the panel says so.
 */
export function SignaturePanel({ provider, cloudName, notWatched, recorded, inDeadLetter, namespaceId, title = 'Failure signatures' }: {
  provider: CloudProvider
  /** Narrows the list to one connection (a component's own page). */
  namespaceId?: string
  title?: string
  cloudName: string
  notWatched: boolean
  recorded: number | null
  inDeadLetter: number | null
}) {
  const list = useQuery({
    queryKey: ['next', 'signatures', provider, namespaceId ?? null],
    queryFn: () => fetchSignatures({ provider, namespaceId, days: 7, tab: 'all', sort: 'messages', page: 1, pageSize: ROWS }),
  })
  const rows = list.data?.items ?? []
  const trust = useQueries({
    queries: rows.map((s) => ({ queryKey: ['signatures', 'trust', s.signatureHash, s.provider], queryFn: () => fetchSignatureTrust(s.signatureHash, s.provider) })),
  })
  const cannotConfirm = trust.some((t) => t.data && !t.data.cloudCanConfirm)

  return (
    <section aria-labelledby="signatures-title" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
      <div className="mb-2 flex items-center justify-between">
        <h2 id="signatures-title" className="text-base font-semibold">{title}<Tip label="section:Failure signatures" /></h2>
        <Link to={nextHref(nextEntry('signatures'), { cloud: provider })} className="inline-flex items-center gap-1 text-xs font-semibold text-[var(--color-primary-700)]">
          View all <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
        </Link>
      </div>
      {list.isError ? (
        <p role="alert" className="text-sm text-red-700">Failure signatures could not be read.</p>
      ) : list.isPending ? (
        <p role="status" className="text-sm text-[var(--color-text-muted)]">Loading…</p>
      ) : rows.length === 0 ? (
        <p className="text-sm text-[var(--color-text-muted)]">No failure signature in the last 7 days{notWatched ? ' — press Look now to record what is in the dead-letter queues' : ''}.</p>
      ) : (
        <table className="w-full table-fixed text-sm">
          <colgroup><col className="w-[34%]" /><col className="w-[16%]" /><col className="w-[28%]" /><col className="w-[22%]" /></colgroup>
          <thead>
            <tr className="text-left text-xs font-semibold text-[var(--color-text-muted)]">
              <Th className={thc}>Signature</Th><Th className={thc}>Count</Th><Th className={thc}>Components</Th><Th className={thc}>Agent may act</Th>
            </tr>
          </thead>
          <tbody>
            {rows.map((s, i) => {
              const t = trust[i]?.data
              const answer = t ? trustWords(t, cloudName).answer : null
              return (
                <tr key={s.signatureHash} className="border-t border-[var(--color-border)]">
                  <td className="break-words py-2 pr-2 font-medium">{s.reason}</td>
                  <td className="px-2">{s.messages.toLocaleString('en-US')}</td>
                  <td className="break-words px-2 text-[var(--color-text-muted)]">{s.namespaces.map((n) => n.displayName ?? n.name).join(', ')}</td>
                  <td className="pl-2">
                    {answer ? <span className={`rounded-md px-2 py-0.5 text-xs font-semibold ${tone[answer]}`}>{label[answer]}</span> : <span className="text-xs text-[var(--color-text-muted)]">…</span>}
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      )}
      {notWatched && recorded !== null && rows.length > 0 && (
        <p className="mt-3 text-xs text-[var(--color-text-muted)]">
          Covers the {recorded.toLocaleString('en-US')} messages recorded at the last look{inDeadLetter !== null ? `, not all ${inDeadLetter.toLocaleString('en-US')}` : ''}.
          {cannotConfirm && ` The Agent asks every time on ${cloudName}, because this cloud cannot prove a fix held.`}
        </p>
      )}
    </section>
  )
}
