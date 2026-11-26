import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Pager } from '../../../components/ui/Pager'
import { usePagedList } from '../../../hooks/usePagedList'
import { peekTransferDeadLetters, type Message } from '../../../lib/api/messages'
import { formatWhen } from '../../../lib/format'
import { subscriptionParts } from '../../../lib/entities'
import { entityLabel } from '../../lib/entityRows'
import { Th, Tip } from '../Tip'

const td = 'px-3 py-2.5 align-top text-sm'

/**
 * The transfer dead-letter queues of one Service Bus connection (build-plan unit G4): a LIST, read only, nothing replayed.
 * A message lands here only when a send-via transaction failed, and it sits on the entity the transaction went THROUGH; where it was
 * meant to go appears only in the reason text, so ServiceHub cannot offer to put it back. Peeking it is repeatable and changes nothing.
 * Shown only when some place has one, and read when a person asks, never in the background.
 */
export function TransferDeadLetters({ namespaceId, places }: { namespaceId: string; places: readonly { name: string; count: number }[] }) {
  const [open, setOpen] = useState<string | null>(null)
  if (places.length === 0) return null
  return (
    <section aria-labelledby="transfer-title" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
      <h2 id="transfer-title" className="px-4 pt-4 text-base font-semibold">Transfer dead-letter queues<Tip label="section:Transfer dead-letter queues" /></h2>
      <p className="px-4 pb-2 text-sm text-[var(--color-text-muted)]">A message lands here when a send-via transaction failed. ServiceHub lists them and does not put them back: where each was going is written only in its reason.</p>
      <ul className="divide-y divide-[var(--color-border)] border-t border-[var(--color-border)]">
        {places.map((p) => (
          <li key={p.name} className="px-4 py-2.5">
            <div className="flex flex-wrap items-center justify-between gap-2 text-sm">
              <span><span className="font-semibold">{entityLabel(p.name, null)}</span> · {p.count.toLocaleString('en-US')} {p.count === 1 ? 'message' : 'messages'}</span>
              <button type="button" aria-expanded={open === p.name} onClick={() => setOpen(open === p.name ? null : p.name)}
                className="rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold text-[var(--color-primary-700)] hover:bg-[var(--color-primary-50)]">
                {open === p.name ? 'Hide' : 'Read them'}
              </button>
            </div>
            {open === p.name && <TransferList namespaceId={namespaceId} name={p.name} />}
          </li>
        ))}
      </ul>
    </section>
  )
}

function TransferList({ namespaceId, name }: { namespaceId: string; name: string }) {
  const target = subscriptionParts(name)
  const peek = useQuery({ queryKey: ['transfer-dead-letter', namespaceId, name], queryFn: () => peekTransferDeadLetters(namespaceId, { ...target, max: 50 }) })
  if (peek.isPending) return <p role="status" className="py-3 text-sm text-[var(--color-text-muted)]">Reading…</p>
  if (peek.isError) return <p role="alert" className="py-3 text-sm text-red-800">The transfer dead-letter queue could not be read.</p>
  const messages = peek.data.messages
  if (messages.length === 0) return <p className="py-3 text-sm text-[var(--color-text-muted)]">Nothing is in it now.</p>
  return <TransferTable name={name} messages={messages} />
}

function TransferTable({ name, messages }: { name: string; messages: readonly Message[] }) {
  const { items, pager } = usePagedList(messages)
  const now = new Date()
  return (
    <div className="mt-2 overflow-x-auto">
      <table className="w-full border-t border-[var(--color-border)]">
        <caption className="sr-only">Messages in the transfer dead-letter queue of {name}</caption>
        <thead><tr><Th>Message</Th><Th>Reason</Th><Th>Description</Th><Th>Deliveries</Th><Th>Enqueued</Th></tr></thead>
        <tbody>
          {items.map((m) => (
            <tr key={m.sequenceNumber} className="border-t border-[var(--color-border)]">
              <td className={`${td} font-mono text-xs`}>{m.messageId}</td>
              <td className={td}>{m.deadLetterReason ?? '—'}</td>
              <td className={td}>{m.deadLetterErrorDescription ?? '—'}</td>
              <td className={td}>{m.deliveryCount}</td>
              <td className={td}>{formatWhen(m.enqueuedTime, now)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <Pager {...pager} label="transfer dead-letter messages" />
      {messages.length === 50 && <p className="py-2 text-xs text-[var(--color-text-muted)]">The first 50 are shown.</p>}
    </div>
  )
}
