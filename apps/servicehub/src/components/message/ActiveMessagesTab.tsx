import { useMemo } from 'react'
import { useQueries, useQuery } from '@tanstack/react-query'
import { Eye, RefreshCw } from 'lucide-react'
import { useSearchParams } from 'react-router-dom'
import { ExplainerCard, ExplainerToggle } from '../explainer/Explainer'
import { useExplainer } from '../explainer/useExplainer'
import { DataTable, type Column } from '../ui/DataTable'
import { columnHelp } from '../../content/columns'
import { EntityCell } from './EntityCell'
import { describeEntity, subscriptionParts } from '../../lib/entities'
import { WorkTabs } from './WorkTabs'
import { fetchEntities, type CloudProvider, type Entity, type Namespace } from '../../lib/api/namespaces'
import { peekMessages, type Message } from '../../lib/api/messages'
import { formatAge, formatBytes, formatWhen } from '../../lib/format'
import { providerLabel } from '../../lib/providers'
import { namespaceKeys } from '../../hooks/useNamespaces'

const PAGE = 25

/**
 * Home's `?tab=active`: what is in flight right now. The same table as Dead letters, drawn with Active's columns.
 *
 * Where a cloud can be looked at without side effects (`supportsRepeatablePeek`, read from the capability and never
 * from a name — R4) it lists messages. Where it cannot, every look is a delivery attempt that could push a message
 * into the dead-letter queue by itself, so it shows counts per queue and says why. A count the cloud cannot supply is
 * "can't count here", never 0 (R5). Nothing on this tab can be replayed: an active message has not failed.
 */
export function ActiveMessagesTab({ provider, namespaces }: { provider: CloudProvider; namespaces: readonly Namespace[] }) {
  const cloud = providerLabel[provider]
  const explainer = useExplainer('active')
  const browsable = namespaces.length > 0 && namespaces.every((n) => n.capabilities?.supportsRepeatablePeek === true)

  const lists = useQueries({
    queries: namespaces.map((n) => ({ queryKey: namespaceKeys.entities(n.id), queryFn: () => fetchEntities(n.id) })),
  })
  const rows = useMemo(
    () =>
      namespaces.flatMap((n, i) =>
        (lists[i]?.data?.entities ?? []).filter((e) => e.kind === 'queue' || e.kind === 'subscription').map((e) => ({ namespace: n, entity: e })),
      ),
    [namespaces, lists],
  )

  return (
    <section className="px-[22px] pb-6 pt-5">
      <header className="mb-4">
        <h1 className="flex items-center gap-2 text-2xl font-extrabold tracking-tight text-[var(--color-text)]">
          {cloud} — Active messages <ExplainerToggle visible={!explainer.shown} onShow={explainer.show} />
        </h1>
        <p className="mt-[3px] text-[13px] text-[var(--color-text-muted)]">
          {browsable
            ? `What is in flight right now. Looking doesn't touch anything — on ${cloud}, a peek is not a delivery.`
            : 'What is in flight right now, counted per queue.'}
        </p>
      </header>

      {explainer.shown && <ExplainerCard id="active" onDismiss={explainer.dismiss} />}
      <WorkTabs current="active" />

      {lists.some((l) => l.isPending) && <p role="status" className="text-sm text-[var(--color-text-muted)]">Reading {cloud}…</p>}
      {lists.some((l) => l.isError) && (
        <p role="alert" className="rounded-xl bg-[var(--color-warning-light)] px-4 py-3 text-sm">
          ServiceHub couldn’t read {cloud} just now.
        </p>
      )}
      {lists.every((l) => l.isSuccess) &&
        (rows.length === 0 ? (
          <p className="text-sm text-[var(--color-text-muted)]">No queues were found in {cloud} yet.</p>
        ) : browsable ? (
          <Browser rows={rows} />
        ) : (
          <CountsOnly cloud={cloud} rows={rows} />
        ))}
    </section>
  )
}

type Row = { namespace: Namespace; entity: Entity }

const optionLabel = (r: Row): string => {
  const e = describeEntity(r.entity.name, r.entity.kind)
  return e.kind === 'subscription' && e.topic ? `${e.topic} › ${e.name} (topic subscription)` : e.name
}

const keyOf = (r: Row) => `${r.namespace.id}|${r.entity.name}`

function Browser({ rows }: { rows: readonly Row[] }) {
  const [params, setParams] = useSearchParams()
  const chosen = rows.find((r) => keyOf(r) === params.get('queue')) ?? rows.find((r) => (r.entity.activeMessages ?? 0) > 0) ?? rows[0]
  const selected = params.get('active')

  const peek = useQuery({
    queryKey: ['active-peek', chosen.namespace.id, chosen.entity.name],
    queryFn: () => peekMessages(chosen.namespace.id, { ...subscriptionParts(chosen.entity.name), max: PAGE }),
  })
  const now = new Date()
  const messages = peek.data?.messages ?? []
  const open = messages.find((m) => String(m.sequenceNumber) === selected)

  const columns: Column<Message>[] = [
    { key: 'enq', header: 'Enqueued', info: columnHelp.active.enqueued, render: (m) => formatWhen(m.enqueuedTime, now) },
    { key: 'q', header: 'Queue or topic', info: columnHelp.active.where, render: () => <EntityCell size="sm" entityName={chosen.entity.name} entityType={chosen.entity.kind} /> },
    { key: 'd', header: 'Delivery', info: columnHelp.active.delivery, numeric: true, render: (m) => m.deliveryCount },
    { key: 'age', header: 'Age', info: columnHelp.active.age, numeric: true, render: (m) => formatAge(m.enqueuedTime, now) },
    { key: 'size', header: 'Size', info: columnHelp.active.size, numeric: true, render: (m) => formatBytes(m.sizeInBytes) },
    {
      key: 'view',
      header: 'Details',
      info: columnHelp.active.view,
      render: (m) => (
        <button
          type="button"
          onClick={() => setParams((c) => { const n = new URLSearchParams(c); n.set('active', String(m.sequenceNumber)); return n })}
          className="text-[11.5px] font-semibold text-[var(--color-primary-600)] hover:underline"
        >
          Details →
        </button>
      ),
    },
  ]

  return (
    <div className="flex items-start gap-3.5">
      <div className="min-w-0 flex-1 overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] shadow-[var(--shadow-card)]">
        <div className="flex items-center gap-2 border-b border-[var(--color-border)] px-4 py-2.5">
          <label className="text-[12px] text-[var(--color-text-muted)]" htmlFor="active-queue">Queue or topic</label>
          <select
            id="active-queue"
            value={keyOf(chosen)}
            onChange={(e) => setParams((c) => { const n = new URLSearchParams(c); n.set('queue', e.target.value); n.delete('active'); return n })}
            className="rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-2 py-1.5 text-sm"
          >
            {rows.map((r) => (
              <option key={keyOf(r)} value={keyOf(r)}>
                {optionLabel(r)} · {r.entity.activeMessages === null ? "can't count" : r.entity.activeMessages.toLocaleString()}
              </option>
            ))}
          </select>
          <button
            type="button"
            onClick={() => void peek.refetch()}
            className="ml-auto flex items-center gap-1.5 rounded-lg border border-[var(--color-border)] px-2.5 py-1.5 text-[12px] font-semibold"
          >
            <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" /> Refresh
          </button>
        </div>
        {peek.isPending && <p role="status" className="px-4 py-3 text-sm text-[var(--color-text-muted)]">Looking…</p>}
        {peek.isError && <p role="alert" className="px-4 py-3 text-sm">ServiceHub couldn’t look at this queue just now.</p>}
        {peek.data && messages.length === 0 && <p className="px-4 py-6 text-center text-sm text-[var(--color-text-muted)]">Nothing is waiting in this queue right now.</p>}
        {messages.length > 0 && (
          <>
            <DataTable caption={`Active messages in ${chosen.entity.name}`} columns={columns} rows={messages} rowKey={(m) => String(m.sequenceNumber)} />
            <p className="border-t border-[var(--color-border)] px-4 py-2.5 text-[12px] text-[var(--color-text-muted)]">
              Showing the oldest {messages.length}
              {chosen.entity.activeMessages != null ? ` of ${chosen.entity.activeMessages.toLocaleString()}` : ''}. Refreshed when you ask.
            </p>
          </>
        )}
      </div>
      {open && <ActiveDrawer message={open} entity={chosen.entity.name} now={now} onClose={() => setParams((c) => { const n = new URLSearchParams(c); n.delete('active'); return n })} />}
    </div>
  )
}

function ActiveDrawer({ message, entity, now, onClose }: { message: Message; entity: string; now: Date; onClose: () => void }) {
  return (
    <aside aria-label="Active message" className="w-[380px] shrink-0 overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] shadow-[var(--shadow-card)]">
      <div className="flex items-center justify-between border-b border-[#f3f4f6] px-4 py-3">
        <span className="rounded-full bg-[var(--color-primary-100)] px-3 py-0.5 text-[12px] font-bold text-[var(--color-primary-700)]">In flight</span>
        <button type="button" onClick={onClose} className="text-sm text-[var(--color-text-muted)]" aria-label="Close">✕</button>
      </div>
      <p className="px-4 pt-3 text-[13px]">
        <b className="font-mono">{entity}</b> · delivery <b>{message.deliveryCount}</b> · {formatBytes(message.sizeInBytes)}
        {message.contentType ? ` · ${message.contentType}` : ''} · {formatAge(message.enqueuedTime, now)} old
      </p>
      <div className="px-4 pt-3">
        <div className="text-[10.5px] font-bold uppercase tracking-wide text-[var(--color-text-muted)]">Message body</div>
        <pre className="mt-1.5 max-h-56 overflow-auto rounded-lg bg-[#0f172a] p-3 text-[12px] leading-relaxed text-[#e2e8f0]">{message.body ?? '(no body)'}</pre>
      </div>
      <dl className="space-y-1 px-4 py-3 text-[12.5px]">
        <Kv k="Message ID" v={message.messageId} />
        {message.correlationId && <Kv k="Correlation ID" v={message.correlationId} />}
        <Kv k="Enqueued" v={formatWhen(message.enqueuedTime, now)} />
      </dl>
      <p className="mx-4 mb-4 flex items-start gap-2 rounded-lg border border-[var(--color-primary-200)] bg-[var(--color-primary-50)] px-3 py-2.5 text-[12.5px]">
        <Eye className="mt-0.5 h-4 w-4 shrink-0 text-[var(--color-primary-700)]" aria-hidden="true" />
        <span>Active messages are for looking. <b>Nothing here can be replayed</b> — it hasn’t failed yet.</span>
      </p>
    </aside>
  )
}

function Kv({ k, v }: { k: string; v: string }) {
  return (
    <div className="flex justify-between gap-3">
      <dt className="text-[var(--color-text-muted)]">{k}</dt>
      <dd className="truncate font-semibold">{v}</dd>
    </div>
  )
}

function CountsOnly({ cloud, rows }: { cloud: string; rows: readonly Row[] }) {
  return (
    <div className="space-y-3.5">
      <p className="flex items-start gap-2.5 rounded-xl border border-[#fde68a] bg-[var(--color-warning-light)] px-4 py-3 text-[13px] text-[#92400e]">
        <Eye className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
        <span>
          <b>On {cloud}, ServiceHub counts active messages but doesn’t open them.</b> There is no way to look at a message here
          without it counting as a delivery — watching could push a message into the dead-letter queue by itself. You’ll see
          counts per queue instead.
        </span>
      </p>
      <div className="overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] shadow-[var(--shadow-card)]">
        <DataTable
          caption={`Active message counts per queue in ${cloud}`}
          rows={rows}
          rowKey={keyOf}
          columns={[
            { key: 'q', header: 'Queue or topic', info: columnHelp.active.where, render: (r) => <EntityCell size="sm" entityName={r.entity.name} entityType={r.entity.kind} /> },
            {
              key: 'n',
              header: 'Waiting now',
              info: columnHelp.active.waitingNow,
              numeric: true,
              render: (r) =>
                r.entity.activeMessages === null ? (
                  <span className="text-[var(--color-text-muted)]">can’t count here</span>
                ) : (
                  r.entity.activeMessages.toLocaleString()
                ),
            },
            {
              key: 'dl',
              header: 'Dead-lettered',
              info: columnHelp.active.deadLettered,
              numeric: true,
              render: (r) => (r.entity.deadLetterMessages === null ? <span className="text-[var(--color-text-muted)]">can’t count here</span> : r.entity.deadLetterMessages.toLocaleString()),
            },
          ]}
        />
      </div>
    </div>
  )
}
