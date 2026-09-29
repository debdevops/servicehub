import { useMemo, useState } from 'react'
import { useQueries, useQuery } from '@tanstack/react-query'
import { Clock, Download, Eye, FileCheck, Inbox, Layers, MessageSquare, RefreshCw, RotateCcw, Search, Send } from 'lucide-react'
import { Link, useSearchParams } from 'react-router-dom'
import { ExplainerCard, ExplainerToggle } from '../explainer/Explainer'
import { useExplainer } from '../explainer/useExplainer'
import { Pager } from '../ui/Pager'
import { usePageSize } from '../../lib/pageSize'
import { DataTable, type Column } from '../ui/DataTable'
import { columnHelp } from '../../content/columns'
import { EntityCell } from './EntityCell'
import { describeEntity, subscriptionParts } from '../../lib/entities'
import { WorkTabs } from './WorkTabs'
import { OverlayFrame } from '../overlays/OverlayFrame'
import { LiveTail } from './LiveTail'
import { ScheduledView } from './ScheduledView'
import { fetchEntities, type CloudProvider, type Entity, type Namespace } from '../../lib/api/namespaces'
import { peekMessages, type Message } from '../../lib/api/messages'
import { formatAge, formatBytes, formatWhen } from '../../lib/format'
import { providerLabel } from '../../lib/providers'
import { environmentOrder, namespaceTag } from '../provider/scopeChoice'
import { namespaceKeys } from '../../hooks/useNamespaces'
import { RetryLink } from '../ui/RetryLink'
import { Skeleton } from '../ui/Skeleton'

/** A peek is one request for up to this many of the oldest messages (the API's ceiling); the grid pages through them locally. */
const PEEK_MAX = 100

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
      // Production first, then UAT, then Development — the order the scope picker uses.
      [...namespaces.entries()]
        .sort(([, a], [, b]) => environmentOrder.indexOf(a.environment) - environmentOrder.indexOf(b.environment))
        .flatMap(([i, n]) =>
        (lists[i]?.data?.entities ?? []).filter((e) => e.kind === 'queue' || e.kind === 'subscription').map((e) => ({ namespace: n, entity: e })),
      ),
    [namespaces, lists],
  )

  return (
    <section className="px-[22px] pb-6 pt-5">
      <header className="mb-4">
        <h1 className="flex flex-wrap items-center gap-2 text-2xl font-extrabold tracking-tight text-[var(--color-text)]">
          {cloud} — Active messages <ExplainerToggle visible={!explainer.shown} onShow={explainer.show} />
          <SendLink />
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
          ServiceHub couldn’t read {cloud} just now. <RetryLink onRetry={() => lists.forEach((l) => { if (l.isError) void l.refetch() })} />
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

const nameOf = namespaceTag

/** Two namespaces can each own an `orders`; once more than one is in scope, a queue is named with its namespace. */
const spansSeveral = (rows: readonly Row[]): boolean => new Set(rows.map((r) => r.namespace.id)).size > 1

const keyOf = (r: Row) => `${r.namespace.id}|${r.entity.name}`

const STATES = [
  { id: 'all', label: 'All' },
  { id: 'fresh', label: 'Not delivered yet' },
  { id: 'retried', label: 'Delivered before (being retried)' },
] as const
const WINDOWS = [
  { id: 'all', label: 'All time', ms: null },
  { id: '1h', label: 'Last hour', ms: 3_600_000 },
  { id: '24h', label: 'Last 24 hours', ms: 86_400_000 },
] as const
const INTERVALS = [15, 30, 60] as const

function Browser({ rows }: { rows: readonly Row[] }) {
  const [params, setParams] = useSearchParams()
  const chosen = rows.find((r) => keyOf(r) === params.get('queue')) ?? rows.find((r) => (r.entity.activeMessages ?? 0) > 0) ?? rows[0]
  const selected = params.get('active')
  const several = spansSeveral(rows)
  const [pageSize, setPageSize] = usePageSize()
  const [page, setPage] = useState(1)
  const [autoRefresh, setAutoRefresh] = useState(true)
  const [every, setEvery] = useState<(typeof INTERVALS)[number]>(15)
  const [state, setState] = useState<(typeof STATES)[number]['id']>('all')
  const [windowId, setWindowId] = useState<(typeof WINDOWS)[number]['id']>('all')
  const [text, setText] = useState('')
  const [ticked, setTicked] = useState<ReadonlySet<string>>(new Set())

  const view = params.get('view') === 'live' ? 'live' : params.get('view') === 'scheduled' ? 'scheduled' : 'browse'
  const setView = (v: string) => setParams((c) => { const n = new URLSearchParams(c); if (v === 'browse') n.delete('view'); else n.set('view', v); n.delete('active'); return n }, { replace: true })
  const target = subscriptionParts(chosen.entity.name)
  const peek = useQuery({
    queryKey: ['active-peek', chosen.namespace.id, chosen.entity.name],
    queryFn: () => peekMessages(chosen.namespace.id, { ...target, max: PEEK_MAX }),
    enabled: view === 'browse',
    // Looking is free here, so the list keeps itself current; the button is for "now".
    refetchInterval: autoRefresh ? every * 1000 : false,
  })
  const now = new Date()
  const all = peek.data?.messages ?? []
  const cutoff = WINDOWS.find((w) => w.id === windowId)?.ms
  const needle = text.trim().toLowerCase()
  // Narrowed here, over what was peeked: state, how recent, and a word in the ID, the correlation ID or the subject.
  const messages = all.filter((m) =>
    (state === 'all' || (state === 'fresh' ? m.deliveryCount <= 0 : m.deliveryCount > 0))
    && (cutoff == null || now.getTime() - new Date(m.enqueuedTime).getTime() <= cutoff)
    && (needle === '' || [m.messageId, m.correlationId, m.subject].some((v) => v?.toLowerCase().includes(needle))))
  const narrowed = messages.length !== all.length
  const lastPage = Math.max(1, Math.ceil(messages.length / pageSize))
  const shown = messages.slice((Math.min(page, lastPage) - 1) * pageSize, Math.min(page, lastPage) * pageSize)
  const open = all.find((m) => String(m.sequenceNumber) === selected)
  const oldest = all.length > 0 ? all.reduce((a, m) => (m.enqueuedTime < a.enqueuedTime ? m : a)) : null
  const retried = all.filter((m) => m.deliveryCount > 0).length
  const one = ticked.size === 1 ? all.find((m) => String(m.sequenceNumber) === [...ticked][0]) : undefined
  const toggle = (k: string) => setTicked((t) => { const n = new Set(t); if (n.has(k)) n.delete(k); else n.add(k); return n })
  const togglePage = () => setTicked((t) => { const keys = shown.map((m) => String(m.sequenceNumber)); const every = keys.every((k) => t.has(k)); const n = new Set(t); keys.forEach((k) => (every ? n.delete(k) : n.add(k))); return n })
  // One click opens one thing: the details drawer, with no send window or message drawer left beside it.
  const openDetails = (m: Message) => setParams((c) => { const n = new URLSearchParams(c); ;['modal', 'message', 'replay', 'view'].forEach((k) => n.delete(k)); n.set('active', String(m.sequenceNumber)); return n })
  const download = (m: Message) => {
    const url = URL.createObjectURL(new Blob([m.body ?? ''], { type: m.contentType ?? 'text/plain' }))
    const a = document.createElement('a')
    a.href = url
    a.download = `${m.messageId}.${(m.contentType ?? '').includes('json') ? 'json' : 'txt'}`
    a.click()
    URL.revokeObjectURL(url)
  }
  const field = 'rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2 text-sm'
  const label = 'mb-0.5 block text-[12px] font-medium text-[var(--color-text-muted)]'

  const columns: Column<Message>[] = [
    { key: 'enq', header: 'Received at', info: columnHelp.active.enqueued, className: 'whitespace-nowrap', render: (m) => (<><span className="block font-medium">{formatWhen(m.enqueuedTime, now)}</span><span className="block text-xs text-[var(--color-text-muted)]">{formatAge(m.enqueuedTime, now)} ago</span></>) },
    { key: 'q', header: 'Queue or topic', info: columnHelp.active.where, render: () => <EntityCell size="sm" entityName={chosen.entity.name} entityType={chosen.entity.kind} /> },
    { key: 'id', header: 'Message ID', info: columnHelp.drawer.messageId, render: (m) => <span title={m.messageId} className="block max-w-[12rem] truncate font-mono text-[11.5px]">{m.messageId}</span> },
    { key: 'size', header: 'Size', info: columnHelp.active.size, numeric: true, className: 'whitespace-nowrap', render: (m) => formatBytes(m.sizeInBytes) },
    { key: 'd', header: 'Delivery count', info: columnHelp.active.delivery, numeric: true, render: (m) => m.deliveryCount },
    { key: 'age', header: 'Age', info: columnHelp.active.age, numeric: true, className: 'whitespace-nowrap', render: (m) => formatAge(m.enqueuedTime, now) },
    { key: 'labels', header: 'Labels', info: columnHelp.active.labels, render: (m) => (m.subject ? <span className="rounded-full bg-[var(--color-surface-muted)] px-2 py-0.5 text-xs font-medium">{m.subject}</span> : <span className="text-[var(--color-text-muted)]">—</span>) },
    {
      key: 'view',
      header: 'Actions',
      info: columnHelp.active.view,
      render: (m) => (
        <button type="button" onClick={() => openDetails(m)} aria-label={`Details of message ${m.messageId}`} className="whitespace-nowrap rounded-lg bg-[var(--color-primary-50)] px-3 py-1.5 text-[12px] font-semibold text-[var(--color-primary-700)] hover:bg-[var(--color-primary-100)]">
          Details →
        </button>
      ),
    },
  ]

  return (
    <div className="flex items-start gap-3.5">
      <div className="min-w-0 flex-1 space-y-4">
        <section aria-label="Active messages at a glance" className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <Tile Icon={MessageSquare} tone="bg-[#dbeafe] text-[#1d4ed8]" value={chosen.entity.activeMessages === null ? '—' : chosen.entity.activeMessages.toLocaleString()} label="Active messages" note={`waiting in ${optionLabel(chosen)}`} />
          <Tile Icon={Clock} tone="bg-[#ffedd5] text-[#ea580c]" value={oldest ? formatAge(oldest.enqueuedTime, now) : '—'} label="Oldest message" note={oldest ? 'among those peeked' : 'nothing to peek'} />
          <Tile Icon={RotateCcw} tone="bg-[#f3e8ff] text-[#9333ea]" value={view === 'browse' ? String(retried) : '—'} label="Delivered before" note="peeked messages being retried" />
          <Tile Icon={Inbox} tone="bg-[#fee2e2] text-[#dc2626]" value={chosen.entity.deadLetterMessages === null ? '—' : chosen.entity.deadLetterMessages.toLocaleString()} label="Dead-lettered" note="set aside in this queue" href="?tab=dlq" />
        </section>

        <div className="overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] shadow-[var(--shadow-card)]">
          <div className="flex flex-wrap items-end gap-3 border-b border-[var(--color-border)] px-4 py-3">
            <div className="min-w-[14rem] flex-1">
              <label className={label} htmlFor="active-queue">Queue or topic</label>
              <select
                id="active-queue"
                value={keyOf(chosen)}
                onChange={(e) => { setParams((c) => { const n = new URLSearchParams(c); n.set('queue', e.target.value); n.delete('active'); return n }); setPage(1); setTicked(new Set()) }}
                className={`${field} w-full`}
              >
                {rows.map((r) => (
                  <option key={keyOf(r)} value={keyOf(r)}>
                    {several ? `${nameOf(r.namespace)} / ` : ''}{optionLabel(r)} · {r.entity.activeMessages === null ? "can't count" : `${r.entity.activeMessages.toLocaleString()} waiting`}
                  </option>
                ))}
              </select>
            </div>
            {view === 'browse' && (
              <>
                <div><label className={label} htmlFor="active-state">Message state</label>
                  <select id="active-state" value={state} onChange={(e) => { setState(e.target.value as typeof state); setPage(1) }} className={field}>{STATES.map((o) => <option key={o.id} value={o.id}>{o.label}</option>)}</select></div>
                <div><label className={label} htmlFor="active-window">Time window</label>
                  <select id="active-window" value={windowId} onChange={(e) => { setWindowId(e.target.value as typeof windowId); setPage(1) }} className={field}>{WINDOWS.map((o) => <option key={o.id} value={o.id}>{o.label}</option>)}</select></div>
                <div className="min-w-[14rem] flex-1"><label className={label} htmlFor="active-search">Search</label>
                  <span className="relative block"><Search aria-hidden="true" className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--color-text-muted)]" />
                    <input id="active-search" type="search" value={text} onChange={(e) => { setText(e.target.value); setPage(1) }} placeholder="Message ID, correlation ID, subject…" className={`${field} w-full pl-8`} /></span></div>
              </>
            )}
            <label className="flex items-center gap-2 pb-2 text-sm font-medium">
              <input type="checkbox" role="switch" checked={view === 'live'} onChange={(e) => setView(e.target.checked ? 'live' : 'browse')} className="h-4 w-4" /> Follow live
            </label>
            {view === 'browse' && (
              <>
                <label className="flex items-center gap-1.5 pb-2 text-[12.5px] text-[var(--color-text-muted)]">
                  <input type="checkbox" checked={autoRefresh} onChange={(e) => setAutoRefresh(e.target.checked)} /> Auto-refresh every
                  <select aria-label="Auto-refresh interval" value={every} onChange={(e) => setEvery(Number(e.target.value) as typeof every)} className="rounded border border-[var(--color-border)] bg-[var(--color-surface)] px-1 py-0.5">{INTERVALS.map((n) => <option key={n} value={n}>{n}s</option>)}</select>
                </label>
                <button type="button" onClick={() => void peek.refetch()} disabled={peek.isFetching} className="flex items-center gap-1.5 rounded-lg border border-[var(--color-border)] px-3 py-2 text-sm font-semibold">
                  <RefreshCw className={`h-4 w-4 ${peek.isFetching ? 'animate-spin' : ''}`} aria-hidden="true" /> {peek.isFetching ? 'Refreshing…' : 'Refresh'}
                </button>
              </>
            )}
            <button type="button" onClick={() => setView(view === 'scheduled' ? 'browse' : 'scheduled')} aria-pressed={view === 'scheduled'} className={`rounded-lg border px-3 py-2 text-sm font-semibold ${view === 'scheduled' ? 'border-[var(--color-primary-600)] bg-[var(--color-primary-50)] text-[var(--color-primary-700)]' : 'border-[var(--color-border)]'}`}>Scheduled</button>
          </div>

          {view === 'live' && <LiveTail key={keyOf(chosen)} namespaceId={chosen.namespace.id} {...target} />}
          {view === 'scheduled' && (chosen.namespace.capabilities?.supportsScheduledMessages
            ? <ScheduledView namespaceId={chosen.namespace.id} {...target} />
            : <p className="px-4 py-6 text-center text-sm text-[var(--color-text-muted)]">This cloud has no scheduled messages — messages here are delivered when they are sent.</p>)}
          {view === 'browse' && <>
            {peek.isPending && <Skeleton label="Looking…" rows={3} className="px-4 py-3" />}
            {peek.isError && <p role="alert" className="px-4 py-3 text-sm">ServiceHub couldn’t look at this queue just now. <RetryLink onRetry={() => void peek.refetch()} /></p>}
            {peek.data && (
              <div className="flex flex-wrap items-center gap-3 border-b border-[var(--color-border)] px-4 py-2.5">
                <span className="font-semibold">{messages.length.toLocaleString()} {messages.length === 1 ? 'message' : 'messages'}{narrowed ? ` of ${all.length.toLocaleString()} peeked` : ''}</span>
                <span className="ml-auto flex items-center gap-2">
                  <button type="button" disabled={!one} onClick={() => one && openDetails(one)} title={one ? undefined : 'Tick exactly one message'} className="inline-flex items-center gap-1.5 rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-sm font-medium disabled:cursor-not-allowed disabled:opacity-50"><Eye className="h-4 w-4" aria-hidden="true" /> Peek message</button>
                  <button type="button" disabled={!one} onClick={() => one && download(one)} title={one ? undefined : 'Tick exactly one message'} className="inline-flex items-center gap-1.5 rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-sm font-medium disabled:cursor-not-allowed disabled:opacity-50"><Download className="h-4 w-4" aria-hidden="true" /> Download body</button>
                </span>
              </div>
            )}
            {peek.data && messages.length === 0 && (
              <div className="px-4 py-12 text-center">
                <span className="mx-auto mb-3 flex h-14 w-14 items-center justify-center rounded-full bg-[var(--color-primary-50)] text-[var(--color-primary-600)]"><FileCheck className="h-7 w-7" aria-hidden="true" /></span>
                <p className="text-lg font-bold">{narrowed ? 'No messages match' : 'No active messages'}</p>
                <p className="mt-1 text-sm text-[var(--color-text-muted)]">{narrowed ? 'Nothing peeked matches the state, time window or search. Clear them to see everything.' : 'Nothing is waiting in this queue right now.'}</p>
                <div className="mt-4 flex justify-center gap-3">
                  {narrowed
                    ? <button type="button" onClick={() => { setState('all'); setWindowId('all'); setText('') }} className="rounded-lg border border-[var(--color-border)] px-4 py-2 text-sm font-semibold">Clear the filters</button>
                    : <button type="button" onClick={() => document.getElementById('active-queue')?.focus()} className="inline-flex items-center gap-2 rounded-lg border border-[var(--color-border)] px-4 py-2 text-sm font-semibold"><Layers className="h-4 w-4" aria-hidden="true" /> Try a different queue</button>}
                  <button type="button" onClick={() => void peek.refetch()} className="inline-flex items-center gap-2 rounded-lg border border-[var(--color-primary-200)] bg-[var(--color-primary-50)] px-4 py-2 text-sm font-semibold text-[var(--color-primary-700)]"><RefreshCw className="h-4 w-4" aria-hidden="true" /> Refresh now</button>
                </div>
              </div>
            )}
            {messages.length > 0 && (
              <>
                <DataTable
                  caption={`Active messages in ${chosen.entity.name}`}
                  compact
                  columns={columns}
                  rows={shown}
                  rowKey={(m) => String(m.sequenceNumber)}
                  selection={{ selected: ticked, onToggle: toggle, onTogglePage: togglePage }}
                  rowLabel={(m) => `Select message ${m.messageId}`}
                />
                <Pager page={Math.min(page, lastPage)} pageSize={pageSize} total={messages.length} onPage={setPage} onPageSize={(s) => { setPageSize(s); setPage(1) }} />
                <p className="border-t border-[var(--color-border)] px-4 py-2.5 text-[12px] text-[var(--color-text-muted)]">
                  Peeked the oldest {all.length}
                  {/* The queue's count and the peek are read at different moments; only say "of N" when N can still be right. */}
                  {chosen.entity.activeMessages != null && chosen.entity.activeMessages > all.length ? ` of ${chosen.entity.activeMessages.toLocaleString()}` : ''}. {autoRefresh ? `Refreshes every ${every} seconds, or when you ask.` : 'Refreshes when you ask.'}
                </p>
              </>
            )}
          </>}
        </div>
      </div>
      {open && <ActiveDrawer message={open} entity={chosen.entity.name} now={now} onClose={() => setParams((c) => { const n = new URLSearchParams(c); n.delete('active'); return n })} />}
    </div>
  )
}

function Tile({ Icon, tone, value, label, note, href }: { Icon: typeof Eye; tone: string; value: string; label: string; note: string; href?: string }) {
  const body = (
    <>
      <span className={`flex h-12 w-12 shrink-0 items-center justify-center rounded-xl ${tone}`}><Icon className="h-6 w-6" aria-hidden="true" /></span>
      <span className="min-w-0"><span className="tabular block text-2xl font-extrabold leading-none">{value}</span><span className="mt-1 block text-sm font-semibold">{label}</span><span className="block truncate text-xs text-[var(--color-text-muted)]">{note}</span></span>
    </>
  )
  const cls = 'flex items-center gap-4 rounded-2xl border border-[var(--color-border)] bg-[var(--color-surface)] px-5 py-4'
  return href ? <Link to={href} className={`${cls} hover:shadow-[var(--shadow-card)]`}>{body}</Link> : <div className={cls}>{body}</div>
}

function ActiveDrawer({ message, entity, now, onClose }: { message: Message; entity: string; now: Date; onClose: () => void }) {
  // The same docked side panel a dead letter opens in, so Details behaves alike on every tab.
  return (
    <OverlayFrame kind="panel" size="drawer" docked title="Message details" onClose={onClose}>
      <div className="space-y-4">
        <span className="inline-block rounded-full bg-[var(--color-primary-100)] px-3 py-0.5 text-[12px] font-bold text-[var(--color-primary-700)]">In flight</span>
        <p className="text-[13px]">
          <b className="font-mono">{entity}</b> · delivery <b>{message.deliveryCount}</b> · {formatBytes(message.sizeInBytes)}
          {message.contentType ? ` · ${message.contentType}` : ''} · {formatAge(message.enqueuedTime, now)} old
        </p>
        <div>
          <div className="text-[10.5px] font-bold uppercase tracking-wide text-[var(--color-text-muted)]">Message body</div>
          <pre className="mt-1.5 max-h-[50vh] min-h-24 overflow-auto whitespace-pre-wrap rounded-lg bg-[#0f172a] p-3 text-[12px] leading-relaxed text-[#e2e8f0]">{message.body ?? '(no body)'}</pre>
        </div>
        <dl className="space-y-1 text-[12.5px]">
          <Kv k="Message ID" v={message.messageId} />
          {message.subject && <Kv k="Subject" v={message.subject} />}
          {message.sessionId && <Kv k="Session ID" v={message.sessionId} />}
          {message.correlationId && <Kv k="Correlation ID" v={message.correlationId} />}
          <Kv k="Enqueued" v={formatWhen(message.enqueuedTime, now)} />
        </dl>
        {message.applicationProperties && Object.keys(message.applicationProperties).length > 0 && (
          <div>
            <div className="text-[10.5px] font-bold uppercase tracking-wide text-[var(--color-text-muted)]">Properties</div>
            <dl className="mt-1.5 space-y-1 text-[12.5px]">
              {Object.entries(message.applicationProperties).map(([k, v]) => <Kv key={k} k={k} v={typeof v === 'string' ? v : JSON.stringify(v)} />)}
            </dl>
          </div>
        )}
        <p className="flex items-start gap-2 rounded-lg border border-[var(--color-primary-200)] bg-[var(--color-primary-50)] px-3 py-2.5 text-[12.5px]">
          <Eye className="mt-0.5 h-4 w-4 shrink-0 text-[var(--color-primary-700)]" aria-hidden="true" />
          <span>Active messages are for looking. <b>Nothing here can be replayed</b> — it hasn’t failed yet.</span>
        </p>
      </div>
    </OverlayFrame>
  )
}

function Kv({ k, v }: { k: string; v: string }) {
  return (
    <div className="flex justify-between gap-3">
      <dt className="text-[var(--color-text-muted)]">{k}</dt>
      <dd className="min-w-0 break-all text-right font-semibold">{v}</dd>
    </div>
  )
}

function CountsOnly({ cloud, rows }: { cloud: string; rows: readonly Row[] }) {
  const several = spansSeveral(rows)
  return (
    <div className="space-y-3.5">
      <p className="flex items-start gap-2.5 rounded-xl border border-[#fde68a] bg-[var(--color-warning-light)] px-4 py-3 text-[13px] text-[#92400e]">
        <Eye className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
        <span>
          <b>On {cloud}, ServiceHub counts active messages but doesn’t open them.</b> There is no way to look at a message here
          without it counting as a delivery — watching could push a message into the dead-letter queue by itself. You’ll see
          counts per queue instead, and <b>Follow live</b> isn’t offered here for the same reason.
        </span>
      </p>
      <div className="overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] shadow-[var(--shadow-card)]">
        <DataTable
          caption={`Active message counts per queue in ${cloud}`}
          rows={rows}
          rowKey={keyOf}
          columns={[
            ...(several ? [{ key: 'ns', header: 'Namespace', render: (r: Row) => nameOf(r.namespace) }] : []),
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

/** Opens Send a message (6.14) over this tab, keeping the scope so the namespace in view is preselected. */
function SendLink() {
  const [params] = useSearchParams()
  const next = new URLSearchParams(params)
  next.set('modal', 'send')
  ;['active', 'message', 'replay'].forEach((k) => next.delete(k))
  return (
    <Link to={`?${next}`} className="ml-auto inline-flex items-center gap-1.5 rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-1.5 text-sm font-semibold tracking-normal hover:bg-[var(--color-surface-muted)]">
      <Send className="h-4 w-4" aria-hidden="true" /> Send a message
    </Link>
  )
}
