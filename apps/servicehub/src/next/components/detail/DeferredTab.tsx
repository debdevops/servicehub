import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import type { ConnectedComponent } from '../../../lib/api/components'
import { peekMessages } from '../../../lib/api/messages'
import { Pager } from '../../../components/ui/Pager'
import { usePagedList } from '../../../hooks/usePagedList'
import { subscriptionParts } from '../../../lib/entities'
import { formatWhen } from '../../../lib/format'
import { entityLabel, fullName } from '../../lib/entityRows'
import { Th, Tip } from '../Tip'

const td = 'px-3 py-2.5 align-top text-sm'
const LOOK = 100

/**
 * The Deferred messages tab (build-plan unit G5; design `p05b-service-bus-deferred`), READ ONLY. A consumer set a deferred message aside to
 * fetch later by its sequence number. Service Bus keeps no count of them (they are counted as active), so the only way to find them is to
 * peek, and a peek is safe to repeat; it is done when a person asks, for one place at a time, never in the background.
 * The design's "Review" (receive it by sequence number and send it again) is NOT built: it is a mutation, and nothing from the cloud's
 * documentation is built on without a live check.
 */
export function DeferredTab({ component: c }: { component: ConnectedComponent }) {
  const places = c.entities.filter((e) => e.kind !== 'topic')
  const [chosen, setChosen] = useState<string>(places[0] ? fullName(places[0]) : '')
  const [asked, setAsked] = useState<{ name: string; at: number } | null>(null)
  const peek = useQuery({
    queryKey: ['deferred-peek', c.namespaceId, asked?.name, asked?.at],
    queryFn: () => peekMessages(c.namespaceId, { ...subscriptionParts(asked!.name), max: LOOK }),
    enabled: asked !== null,
    staleTime: Infinity,
  })
  const now = new Date()
  const deferred = (peek.data?.messages ?? []).filter((m) => m.isDeferred === true)
  const { items: shown, pager } = usePagedList(deferred)
  return (
    <div className="space-y-4">
      <aside aria-label="Being validated" className="rounded-xl border border-amber-200 bg-amber-50 p-3 text-sm text-amber-900">
        <p><span className="font-semibold">Being validated.</span> This tab lists deferred messages and nothing more. Whether one can be received by its sequence number and sent again safely, and what that repeats, has to be confirmed on a real namespace first, so there is no button to move one.</p>
      </aside>
      <section aria-labelledby="deferred-title" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
        <h2 id="deferred-title" className="px-4 pt-4 text-base font-semibold">Deferred messages<Tip label="section:Deferred messages" /></h2>
        <div className="flex flex-wrap items-center gap-3 px-4 py-3">
          <label className="flex items-center gap-2 text-sm">Place
            <select aria-label="Place" value={chosen} onChange={(e) => setChosen(e.target.value)}
              className="rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2 text-sm">
              {places.map((e) => <option key={fullName(e)} value={fullName(e)}>{entityLabel(fullName(e), null)}</option>)}
            </select>
          </label><Tip label="Place:" />
          <button type="button" disabled={chosen === ''} onClick={() => setAsked({ name: chosen, at: Date.now() })}
            className="rounded-lg border border-[var(--color-border)] px-3.5 py-2 text-xs font-bold hover:bg-[var(--color-surface-muted)] disabled:opacity-50">Look now</button>
          <Tip label="Look now" context="deferred" />
        </div>
        {asked === null ? (
          <p className="border-t border-[var(--color-border)] px-4 py-5 text-sm text-[var(--color-text-muted)]">Service Bus keeps no count of deferred messages, so they are found by looking. Choose a place and press Look now.</p>
        ) : peek.isPending ? (
          <p role="status" className="border-t border-[var(--color-border)] px-4 py-5 text-sm text-[var(--color-text-muted)]">Looking…</p>
        ) : peek.isError ? (
          <p role="alert" className="border-t border-[var(--color-border)] px-4 py-5 text-sm text-red-800">That place could not be read.</p>
        ) : deferred.length === 0 ? (
          <p className="border-t border-[var(--color-border)] px-4 py-5 text-sm text-[var(--color-text-muted)]">
            No deferred message among the first {peek.data.messages.length.toLocaleString('en-US')} looked at in {entityLabel(asked.name, null)}.
            {peek.data.messages.length >= LOOK ? ` Only the first ${LOOK} are looked at, so a deferred message further back would not show.` : ''}
          </p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full border-t border-[var(--color-border)]">
              <caption className="sr-only">Deferred messages in {asked.name}</caption>
              <thead><tr><Th>Message</Th><Th>Sequence number</Th><Th>Deliveries</Th><Th>Enqueued</Th></tr></thead>
              <tbody>
                {shown.map((m) => (
                  <tr key={m.sequenceNumber} className="border-t border-[var(--color-border)]">
                    <td className={`${td} font-mono text-xs`}>{m.messageId}</td>
                    <td className={`${td} font-mono text-xs`}>{m.sequenceNumber}</td>
                    <td className={td}>{m.deliveryCount}</td>
                    <td className={td}>{formatWhen(m.enqueuedTime, now)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            <Pager {...pager} label="deferred messages" />
            {peek.data.messages.length >= LOOK && <p className="border-t border-[var(--color-border)] px-4 py-2 text-xs text-[var(--color-text-muted)]">Only the first {LOOK} messages of the place are looked at.</p>}
          </div>
        )}
      </section>
    </div>
  )
}
