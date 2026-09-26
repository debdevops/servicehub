import type { HelpAnswer } from '../content/help'
import type { CloudProvider, Entity, Namespace } from './api/namespaces'
import type { NavEntry } from '../nav/navigation'
import { providerLabel } from './providers'

export interface SearchResult {
  readonly key: string
  readonly group: 'Queues and topics' | 'Namespaces' | 'Do something' | 'Help'
  readonly title: string
  readonly detail: string
  /** Where it goes. A leading '?' opens over the current page. */
  readonly href: string
  /** Switch Home to this cloud first (Home never mixes clouds). */
  readonly provider?: CloudProvider
}

const matches = (text: string, words: readonly string[]) => words.every((w) => text.toLowerCase().includes(w))

/**
 * What ⌘K finds (unit 6.9): queues and topics, namespaces, every place in the navigation array (so a new destination is
 * searchable the moment it exists) and Help answers. Never message contents — that is not searchable here, by design.
 */
export function search(
  query: string,
  namespaces: readonly Namespace[],
  entities: ReadonlyMap<string, readonly Entity[]>,
  places: readonly NavEntry[],
  help: readonly HelpAnswer[],
): SearchResult[] {
  const words = query.trim().toLowerCase().split(/\s+/).filter(Boolean)
  if (words.length === 0) return []
  const out: SearchResult[] = []

  for (const ns of namespaces) {
    for (const e of entities.get(ns.id) ?? []) {
      if (e.kind === 'topic' || !matches(e.name, words)) continue
      const dead = e.deadLetterMessages
      out.push({
        key: `e:${ns.id}:${e.name}`, group: 'Queues and topics', title: e.name,
        detail: `${providerLabel[ns.provider]} · ${ns.displayName ?? ns.name}${dead === null ? '' : ` · ${dead.toLocaleString()} dead-lettered`}`,
        href: `/?tab=dlq&ns=${encodeURIComponent(ns.id)}&entity=${encodeURIComponent(e.name)}`, provider: ns.provider,
      })
    }
  }
  for (const ns of namespaces) {
    const name = ns.displayName ?? ns.name
    if (!matches(`${name} ${ns.name} ${providerLabel[ns.provider]}`, words)) continue
    out.push({ key: `n:${ns.id}`, group: 'Namespaces', title: name, detail: `${providerLabel[ns.provider]} · ${ns.environment}`, href: `/?ns=${encodeURIComponent(ns.id)}`, provider: ns.provider })
  }
  for (const p of places) {
    if (!matches(`${p.label} ${p.description ?? ''}`, words)) continue
    const href = p.kind === 'page' ? p.path : p.kind === 'tab' ? `${p.path}?tab=${p.value}` : `?${p.kind}=${p.value}`
    out.push({ key: `p:${p.id}`, group: 'Do something', title: p.label, detail: p.description ?? '', href })
  }
  for (const h of help) {
    if (!matches(`${h.question} ${h.answer}`, words)) continue
    out.push({ key: `h:${h.id}`, group: 'Help', title: h.question, detail: h.answer.slice(0, 90) + (h.answer.length > 90 ? '…' : ''), href: '?panel=help' })
  }
  return out.slice(0, 40)
}
