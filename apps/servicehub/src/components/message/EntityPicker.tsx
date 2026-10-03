import { useEffect, useMemo, useRef, useState } from 'react'
import { useQueries } from '@tanstack/react-query'
import { Check, ChevronDown, Inbox, Radio, Search } from 'lucide-react'
import { fetchEntities, type Namespace } from '../../lib/api/namespaces'
import { namespaceKeys } from '../../hooks/useNamespaces'
import { describeEntity } from '../../lib/entities'

interface Option {
  /** The name dead letters are stored under — the one the list filter matches. */
  readonly value: string
  readonly label: string
  readonly count: number | null
}

interface TopicGroup {
  readonly topic: string
  readonly items: readonly Option[]
}

/**
 * Filters the dead-letter list to one queue or one topic subscription.
 *
 * The two are kept clearly apart under their own titles — Queues, then Topics with each topic's subscriptions beneath it — and a
 * group a cloud does not have says "None in {cloud}" rather than vanishing, so an empty group reads as a fact, not a bug. The
 * list comes from the cloud's own entities (so it is complete even where nothing is dead-lettered), plus anything already
 * recorded that the cloud no longer lists. Counts are the cloud's own; where it cannot count, no number is drawn.
 */
export function EntityPicker({
  namespaces,
  cloud,
  recorded,
  value,
  onChange,
}: {
  namespaces: readonly Namespace[]
  cloud: string
  /** Names already recorded on dead letters — kept selectable even if the cloud no longer lists them. */
  recorded: readonly string[]
  value: string | undefined
  onChange: (entity: string | null) => void
}) {
  const [open, setOpen] = useState(false)
  const [filter, setFilter] = useState('')
  const root = useRef<HTMLDivElement>(null)
  const search = useRef<HTMLInputElement>(null)

  const lists = useQueries({ queries: namespaces.map((n) => ({ queryKey: namespaceKeys.entities(n.id), queryFn: () => fetchEntities(n.id) })) })
  const loading = lists.some((l) => l.isPending)

  const { queues, topics } = useMemo(() => {
    const queues = new Map<string, Option>()
    const topics = new Map<string, Map<string, Option>>()
    const addSub = (topic: string, name: string, count: number | null) => {
      const value = `${topic}/subscriptions/${name}`
      const group = topics.get(topic) ?? new Map<string, Option>()
      if (!group.has(value)) group.set(value, { value, label: name, count })
      topics.set(topic, group)
    }

    for (const list of lists) {
      for (const e of list.data?.entities ?? []) {
        if (e.kind === 'queue') queues.set(e.name, { value: e.name, label: e.name, count: e.deadLetterMessages })
        else if (e.kind === 'subscription') {
          const d = describeEntity(e.name, 'subscription')
          if (d.topic) addSub(d.topic, d.name, e.deadLetterMessages)
        }
      }
    }

    // Recorded rows the cloud no longer lists stay selectable — a filter must never hide where something is stuck.
    for (const name of recorded) {
      const d = describeEntity(name, name.includes('/') ? 'subscription' : 'queue')
      if (d.kind === 'queue') {
        if (!queues.has(name)) queues.set(name, { value: name, label: name, count: null })
      } else if (d.topic) addSub(d.topic, d.name, null)
    }

    return {
      queues: [...queues.values()].sort((a, b) => a.label.localeCompare(b.label)),
      topics: [...topics.entries()].sort(([a], [b]) => a.localeCompare(b)).map(([topic, m]): TopicGroup => ({ topic, items: [...m.values()].sort((a, b) => a.label.localeCompare(b.label)) })),
    }
  }, [lists, recorded])

  const q = filter.trim().toLowerCase()
  const shownQueues = queues.filter((o) => !q || o.label.toLowerCase().includes(q))
  const shownTopics = topics
    .map((t) => ({ topic: t.topic, items: t.items.filter((o) => !q || o.label.toLowerCase().includes(q) || t.topic.toLowerCase().includes(q)) }))
    .filter((t) => t.items.length > 0)

  useEffect(() => {
    if (!open) return
    search.current?.focus()
    const away = (e: MouseEvent) => { if (!root.current?.contains(e.target as Node)) setOpen(false) }
    const esc = (e: KeyboardEvent) => { if (e.key === 'Escape') { e.stopPropagation(); setOpen(false) } }
    document.addEventListener('mousedown', away)
    document.addEventListener('keydown', esc, true)
    return () => { document.removeEventListener('mousedown', away); document.removeEventListener('keydown', esc, true) }
  }, [open])

  const pick = (v: string | null) => { onChange(v); setOpen(false); setFilter('') }
  const current = value ? describeEntity(value, value.includes('/') ? 'subscription' : 'queue') : null
  const currentLabel = current ? (current.topic ? `${current.topic} › ${current.name}` : current.name) : 'All queues & topics'

  return (
    <div ref={root} className="relative">
      <button
        type="button"
        aria-haspopup="listbox"
        aria-expanded={open}
        onClick={() => setOpen((o) => !o)}
        className="flex min-w-[15rem] max-w-[24rem] items-center gap-2 rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-1.5 text-left hover:border-[var(--color-primary-300)]"
      >
        {current?.kind === 'subscription' ? <Radio className="h-4 w-4 shrink-0 text-[var(--color-primary-600)]" aria-hidden="true" /> : <Inbox className="h-4 w-4 shrink-0 text-[var(--color-primary-600)]" aria-hidden="true" />}
        <span className="min-w-0 flex-1 truncate">{currentLabel}</span>
        <ChevronDown className={`h-4 w-4 shrink-0 text-[var(--color-text-muted)] transition-transform ${open ? 'rotate-180' : ''}`} aria-hidden="true" />
      </button>

      {open && (
        <div className="absolute left-0 top-full z-30 mt-1.5 w-[26rem] max-w-[90vw] overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] shadow-[0_12px_32px_rgba(0,0,0,0.16)]">
          <div className="flex items-center gap-2 border-b border-[var(--color-border)] px-3 py-2">
            <Search className="h-4 w-4 text-[var(--color-text-muted)]" aria-hidden="true" />
            <input ref={search} value={filter} onChange={(e) => setFilter(e.target.value)} placeholder="Find a queue, topic or subscription" aria-label="Find a queue, topic or subscription" className="w-full bg-transparent text-sm outline-none" />
          </div>
          <div role="listbox" aria-label="Queue or topic" className="max-h-80 overflow-y-auto py-1">
            <Item selected={!value} onPick={() => pick(null)}>
              <span className="font-semibold">All queues &amp; topics</span>
            </Item>

            <Title icon={<Inbox className="h-3.5 w-3.5" aria-hidden="true" />} text="Queues" count={queues.length} />
            {loading ? (
              <p className="px-4 py-2 text-[13px] text-[var(--color-text-muted)]">Reading {cloud}…</p>
            ) : shownQueues.length === 0 ? (
              <None cloud={cloud} filtered={!!q && queues.length > 0} what="queues" />
            ) : (
              shownQueues.map((o) => (
                <Item key={o.value} selected={value === o.value} onPick={() => pick(o.value)} count={o.count}>
                  <span className="font-mono text-[12.5px]">{o.label}</span>
                </Item>
              ))
            )}

            <Title icon={<Radio className="h-3.5 w-3.5" aria-hidden="true" />} text="Topics" count={topics.length} hint="a topic holds nothing itself — each subscription has its own dead-letter queue" />
            {loading ? null : shownTopics.length === 0 ? (
              <None cloud={cloud} filtered={!!q && topics.length > 0} what="topics" />
            ) : (
              shownTopics.map((t) => (
                <div key={t.topic}>
                  <p className="truncate px-4 pb-0.5 pt-1.5 font-mono text-[12px] font-semibold text-[var(--color-text)]" title={`Topic: ${t.topic}`}>{t.topic}</p>
                  {t.items.map((o) => (
                    <Item key={o.value} selected={value === o.value} onPick={() => pick(o.value)} count={o.count} indent>
                      <span className="mr-1 text-[var(--color-text-muted)]" aria-hidden="true">›</span>
                      <span className="font-mono text-[12.5px]">{o.label}</span>
                    </Item>
                  ))}
                </div>
              ))
            )}
          </div>
        </div>
      )}
    </div>
  )
}

function Title({ icon, text, count, hint }: { icon: React.ReactNode; text: string; count: number; hint?: string }) {
  return (
    <div className="mt-1 border-t border-[var(--color-border)] bg-[var(--color-surface-muted)] px-4 py-1.5">
      <p className="flex items-center gap-1.5 text-[10.5px] font-bold uppercase tracking-[0.7px] text-[var(--color-text-muted)]">
        {icon} {text} <span className="rounded-full bg-[var(--color-surface)] px-1.5 text-[10px] font-semibold normal-case tracking-normal">{count}</span>
      </p>
      {hint && <p className="text-[11px] normal-case tracking-normal text-[var(--color-text-muted)]">{hint}</p>}
    </div>
  )
}

function None({ cloud, filtered, what }: { cloud: string; filtered: boolean; what: string }) {
  return <p className="px-4 py-2 text-[13px] italic text-[var(--color-text-muted)]">{filtered ? `No ${what} match.` : `None in ${cloud}`}</p>
}

function Item({ selected, onPick, count, indent, children }: { selected: boolean; onPick: () => void; count?: number | null; indent?: boolean; children: React.ReactNode }) {
  return (
    <button
      type="button"
      role="option"
      aria-selected={selected}
      onClick={onPick}
      className={`flex w-full items-center gap-2 py-1.5 pr-3 text-left text-sm hover:bg-[var(--color-primary-50)] ${indent ? 'pl-8' : 'pl-4'} ${selected ? 'bg-[var(--color-primary-50)]' : ''}`}
    >
      <span className="min-w-0 flex-1 truncate">{children}</span>
      {typeof count === 'number' && <span className={`rounded-full px-2 text-[11px] font-bold ${count > 0 ? 'bg-[var(--color-error-light)] text-[#b91c1c]' : 'bg-[var(--color-surface-muted)] text-[var(--color-text-muted)]'}`} title="Dead letters the cloud reports right now">{count}</span>}
      {selected && <Check className="h-4 w-4 shrink-0 text-[var(--color-primary-600)]" aria-hidden="true" />}
    </button>
  )
}
