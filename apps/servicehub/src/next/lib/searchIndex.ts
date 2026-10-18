import type { ConnectedComponent } from '../../lib/api/components'
import type { NextHelpAnswer } from '../content/help'
import { cloudLabel } from './clouds'
import { fullName } from './entityRows'
import { nextEntry, nextHref, nextNavigation, type NextEntry } from '../nav/navigation'

export interface NextSearchResult {
  readonly key: string
  readonly group: 'Go to' | 'Pages' | 'Do' | 'Help'
  readonly title: string
  readonly detail: string
  readonly href: string
}

const matches = (text: string, words: readonly string[]) => words.every((w) => text.toLowerCase().includes(w))

/**
 * What search finds in the 5.0.0 shell (build-plan unit F7): connections and their queues, topics and subscriptions, every BUILT page of
 * the navigation array (so a new page is searchable the moment its `built` flag is true), the standalone dialogs that are built, and Help
 * answers. **Never message contents**: ServiceHub keeps only a short preview of a message and a body can hold personal data. Names come
 * from the one components read, so a search result can never name something the pages do not show.
 */
export function searchNext(query: string, components: readonly ConnectedComponent[], help: readonly NextHelpAnswer[], cloud: string, currentPath: string): NextSearchResult[] {
  const words = query.trim().toLowerCase().split(/\s+/).filter(Boolean)
  if (words.length === 0) return []
  const out: NextSearchResult[] = []

  for (const c of components) {
    const detail = `${c.componentName} · ${cloudLabel[c.cloud]}`
    const here = nextHref(nextEntry('component-detail'), { cloud: c.cloud, component: c.componentKey, namespaceId: c.namespaceId })
    if (matches(`${c.name} ${c.componentName} ${cloudLabel[c.cloud]}`, words)) out.push({ key: `c:${c.namespaceId}:${c.componentKey}`, group: 'Go to', title: c.name, detail, href: here })
    for (const e of c.entities) {
      if (e.kind === 'topic') continue
      const label = e.parent ? `${e.parent} / ${e.name}` : e.name
      if (!matches(label, words)) continue
      out.push({ key: `e:${c.namespaceId}:${fullName(e)}`, group: 'Go to', title: label, detail: `${e.kind === 'subscription' ? 'Subscription' : 'Queue'} in ${c.name} · ${cloudLabel[c.cloud]}`, href: `${here}?tab=dead-letters&entity=${encodeURIComponent(fullName(e))}` })
    }
  }
  const pageEntry = (p: NextEntry) => p.built && p.kind === 'page' && !p.path.includes(':component') && (p.group === 'tool' || p.group === 'home' || p.group === 'reference')
  for (const p of nextNavigation.filter(pageEntry)) {
    if (p.id === 'home' && cloud === 'all') continue // Home of "all clouds" is its own page, `home-all`
    if (!matches(`${p.label} ${p.description}`, words)) continue
    out.push({ key: `p:${p.id}`, group: 'Pages', title: p.label, detail: p.description, href: nextHref(p, { cloud }) })
  }
  for (const d of nextNavigation.filter((e) => e.built && e.kind !== 'page' && (e.id === 'emergency-stop' || e.id === 'help'))) {
    if (!matches(`${d.label} ${d.description}`, words)) continue
    out.push({ key: `d:${d.id}`, group: 'Do', title: d.label, detail: d.description, href: nextHref(d, {}, currentPath) })
  }
  for (const h of help) {
    if (!matches(`${h.question} ${h.answer}`, words)) continue
    out.push({ key: `h:${h.id}`, group: 'Help', title: h.question, detail: h.answer.slice(0, 90) + (h.answer.length > 90 ? '…' : ''), href: `${currentPath}?panel=help&topic=${h.id}` })
  }
  return out.slice(0, 40)
}
