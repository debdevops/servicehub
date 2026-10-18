import type { ComponentType } from 'react'
import {
  Bot, Boxes, Cable, CircleHelp, Fingerprint, Hand, Home, Inbox, LayoutGrid, ListChecks, Play, Plus, RotateCcw, ScrollText, Search,
  Send, Settings, ShieldCheck, Table2, Timer, TriangleAlert,
} from 'lucide-react'

/**
 * THE 5.0.0 navigation array — every destination of the new layout, of every kind.
 *
 * It lives beside the 4.2.0 array (`src/nav/navigation.ts`) until the cutover (build-plan unit I1), and is mounted under
 * `/next` so both can run in one build. At the cutover this file replaces that one and `NEXT_ROOT` becomes `''`.
 *
 * The rules it keeps from 4.1.0 (ARCHITECTURE §4.5): one array drives the menu, the route table, the overlay host, search and
 * the breadcrumbs; adding a destination is one entry; a test asserts the router and this array cannot disagree
 * (`tests/web/unit/next/navigation.test.ts`).
 *
 * What changes (ADR draft 0019): there is one surface, not Simple and Advanced. A destination is a PAGE with its own address,
 * or a DIALOG or PANEL carried in the URL (`?dialog=` · `?panel=`). Pages that investigate still never act on their own:
 * they open the same dialogs as everywhere else.
 */

/** Where the 5.0.0 layout is mounted until the cutover. */
export const NEXT_ROOT = '/next'

/** page — a route. dialog / panel — opens over the page you are on, carried in the URL. */
export type NextKind = 'page' | 'dialog' | 'panel'

/**
 * Where an entry appears.
 *   home       — the Home link.
 *   component  — reached from the PaaS Components list (which the coverage catalogue fills, not this array).
 *   tool       — the Operational Tools list, in this order.
 *   reference  — footer links: configuration, coverage.
 *   contextual — opened from a page, never from the menu.
 */
export type NextGroup = 'home' | 'component' | 'tool' | 'reference' | 'contextual'

interface NextCommon {
  /** Stable id. Used by search and by tests; never reused for a different screen. */
  readonly id: string
  /** What a person calls it. */
  readonly label: string
  /** One short line, for search, a tooltip and the placeholder. */
  readonly description: string
  readonly icon: ComponentType<{ className?: string }>
  readonly group: NextGroup
  /** The build-plan unit that builds it (`docs-private/future/BUILD-PLAN-5.0.0.md`). `built` once its page is real. */
  readonly unit: string
  /** The accepted design it must match: a file name in `docs-private/future/design-5.0.0/` (without extension). */
  readonly design: string
  /** True once the placeholder has been replaced by the real screen. A test counts these, so progress is not a matter of opinion. */
  readonly built: boolean
}

export interface NextPage extends NextCommon {
  readonly kind: 'page'
  /**
   * The route, relative to `NEXT_ROOT`. `''` is the index. `:cloud` is `all`, `azure`, `aws` or `gcp`.
   * The URL is the state: scope, component and connection all live in the path; tabs and filters in the query.
   */
  readonly path: string
  /** A badge the menu shows beside it, by name. The layout reads the number from the pending-work query — never from here. */
  readonly badge?: 'needsYou' | 'approvals'
}

export interface NextOverlay extends NextCommon {
  readonly kind: 'dialog' | 'panel'
  /** The `?dialog=` or `?panel=` value. */
  readonly value: string
}

export type NextEntry = NextPage | NextOverlay

export const nextNavigation: readonly NextEntry[] = [
  // ── Home ──────────────────────────────────────────────────────────────────────────────────────
  {
    kind: 'page', id: 'home-all', label: 'All clouds', path: '',
    description: 'Every connected cloud side by side, each in its own terms, and everything waiting for a person.',
    icon: LayoutGrid, group: 'home', unit: 'C2', design: 'p02-all-clouds-home', built: true,
  },
  {
    kind: 'page', id: 'home', label: 'Home', path: ':cloud',
    description: 'One cloud: what is in dead-letter, what needs you, and each connected component.',
    icon: Home, group: 'home', unit: 'C1', design: 'azure-home', built: true,
  },

  // ── Components — the list itself comes from the coverage catalogue ────────────────────────────
  {
    kind: 'page', id: 'component-list', label: 'Component', path: ':cloud/c/:component',
    description: 'Every connection of one component type on this cloud.',
    icon: Boxes, group: 'component', unit: 'D1', design: 'p04-azure-service-bus-list', built: true,
  },
  {
    kind: 'page', id: 'component-detail', label: 'Component detail', path: ':cloud/c/:component/:namespaceId',
    description: 'One connection: its queues, topics and subscriptions, its dead-letter messages, and what was done about them.',
    icon: Inbox, group: 'component', unit: 'D2', design: 'azure-service-bus-detail', built: true,
  },

  // ── Operational tools, in menu order ──────────────────────────────────────────────────────────
  {
    kind: 'page', id: 'attention', label: 'Needs your attention', path: ':cloud/attention', badge: 'needsYou',
    description: 'Everything waiting for a person: replays the Agent is holding, and items ServiceHub handed off.',
    icon: TriangleAlert, group: 'tool', unit: 'E1', design: 'p06-needs-your-attention', built: true,
  },
  {
    kind: 'page', id: 'approvals', label: 'Pending approvals', path: ':cloud/approvals', badge: 'approvals',
    description: 'Replays the Agent prepared and is holding until a person says yes or no.',
    icon: ShieldCheck, group: 'tool', unit: 'E2', design: 'p07-pending-approvals', built: true,
  },
  {
    kind: 'page', id: 'active', label: 'Active messages', path: ':cloud/active',
    description: 'Messages that have not failed: still waiting in a queue or subscription.',
    icon: Boxes, group: 'tool', unit: 'E3', design: 'p08-active-messages', built: true,
  },
  {
    kind: 'page', id: 'replays', label: 'Replay history', path: ':cloud/replays',
    description: 'Every message put back, by whom, and what ServiceHub observed afterwards.',
    icon: RotateCcw, group: 'tool', unit: 'E4', design: 'p09-replay-history', built: true,
  },
  {
    kind: 'page', id: 'rules', label: 'Auto Replay rules', path: ':cloud/rules',
    description: 'What ServiceHub may replay without asking, how fast, and whether each rule is still working.',
    icon: Timer, group: 'tool', unit: 'E5', design: 'p10-auto-replay-rules', built: true,
  },
  {
    kind: 'page', id: 'signatures', label: 'Failure signatures', path: ':cloud/signatures',
    description: 'Each kind of failure: is it still happening, does replaying help, and may the Agent fix it alone?',
    icon: Fingerprint, group: 'tool', unit: 'E6', design: 'p11-failure-signatures', built: true,
  },
  {
    kind: 'page', id: 'ledger', label: 'Evidence ledger', path: ':cloud/ledger',
    description: 'Every recovery action with who did it, each step, and exactly what was observed afterwards.',
    icon: ScrollText, group: 'tool', unit: 'E7', design: 'p12-evidence-ledger', built: true,
  },
  {
    kind: 'page', id: 'agents', label: 'Agents', path: ':cloud/agents',
    description: 'The background workers that watch, replay, verify and learn.',
    icon: Bot, group: 'tool', unit: 'E8', design: 'p13-agents', built: true,
  },

  // ── Reference — not scoped to a cloud ─────────────────────────────────────────────────────────
  {
    kind: 'page', id: 'configuration', label: 'Configuration', path: 'configuration',
    description: 'What ServiceHub is connected to, whom it tells, when it calls something stuck, and who may do what.',
    icon: Settings, group: 'reference', unit: 'E9', design: 'p14-configuration', built: true,
  },
  {
    kind: 'page', id: 'coverage', label: 'What ServiceHub covers', path: 'coverage',
    description: 'Every component on every cloud: where failed messages end up, and whether ServiceHub can move them on.',
    icon: Table2, group: 'reference', unit: 'A3', design: 'p15-what-servicehub-covers', built: true,
  },

  // ── Contextual — opened from a page ───────────────────────────────────────────────────────────
  {
    kind: 'dialog', id: 'connect', label: 'Connect a component', value: 'connect',
    description: 'Connect a cloud: one connection, one credential, then ServiceHub tests it and says what it can see.',
    icon: Plus, group: 'contextual', unit: 'F1', design: 'd01-connect-a-component', built: true,
  },
  {
    kind: 'dialog', id: 'replay', label: 'Replay', value: 'replay',
    description: 'The preview for one message: what will happen, the checks, then replay.',
    icon: Play, group: 'contextual', unit: 'F2', design: 'd02-replay', built: true,
  },
  {
    kind: 'dialog', id: 'bulk-replay', label: 'Bulk replay', value: 'bulk-replay',
    description: 'Put many messages back, with a preview first.',
    icon: ListChecks, group: 'contextual', unit: 'F3', design: 'd03-bulk-replay', built: true,
  },
  {
    kind: 'dialog', id: 'review', label: 'Review', value: 'review',
    description: 'What the Agent stopped and asked you about, and why. Approve or decline.',
    icon: ShieldCheck, group: 'contextual', unit: 'E2', design: 'd05-review', built: true,
  },
  {
    kind: 'panel', id: 'hand-off', label: 'Handed to a person', value: 'hand-off',
    description: 'What, where, since when, why ServiceHub stops, who was told, and where to look next.',
    icon: Hand, group: 'contextual', unit: 'F4', design: 'd06-hand-off', built: true,
  },
  {
    kind: 'dialog', id: 'message', label: 'Message', value: 'message',
    description: 'One message in full: why it failed, its body, properties and history. Read-only.',
    icon: Inbox, group: 'contextual', unit: 'D3', design: 'd07-message-full-view', built: true,
  },
  {
    kind: 'dialog', id: 'emergency-stop', label: 'Emergency stop', value: 'emergency-stop',
    description: 'Stop every replay ServiceHub would do on its own, on every cloud, now.',
    icon: TriangleAlert, group: 'contextual', unit: 'F5', design: 'd08-emergency-stop-confirm', built: true,
  },
  {
    kind: 'panel', id: 'help', label: 'Help', value: 'help',
    description: 'How do I do the thing I am trying to do?',
    icon: CircleHelp, group: 'contextual', unit: 'F6', design: 'd09-help', built: true,
  },
  {
    kind: 'dialog', id: 'search', label: 'Search', value: 'search',
    description: 'Go anywhere and do the common things. Never message contents.',
    icon: Search, group: 'contextual', unit: 'F7', design: 'd10-search', built: true,
  },
  {
    kind: 'panel', id: 'security', label: 'Security and data', value: 'security',
    description: 'What ServiceHub keeps on this machine, for how long, and what it may do in your cloud.',
    icon: ShieldCheck, group: 'contextual', unit: 'F10', design: 'd13-security-and-data', built: true,
  },
  {
    kind: 'panel', id: 'autonomy', label: 'How autonomy is earned', value: 'autonomy',
    description: 'How a kind of failure goes from a person approving each replay to the Agent handling it, and what takes that away.',
    icon: Bot, group: 'contextual', unit: 'F11', design: 'd14-how-autonomy-is-earned', built: true,
  },
  {
    kind: 'dialog', id: 'send', label: 'Send a test message', value: 'send',
    description: 'Put one test message onto a queue or topic.',
    icon: Send, group: 'contextual', unit: 'F8', design: 'd11-send-test-message', built: true,
  },
  {
    kind: 'dialog', id: 'cloud-redrive', label: 'Let the cloud redrive', value: 'cloud-redrive',
    description: 'The cloud’s own redrive, on approval. Built only if the live checks in unit G6 pass.',
    icon: Cable, group: 'contextual', unit: 'G6', design: 'd04-cloud-redrive', built: false,
  },
]

/** The tabs of a component's own page, in order. A tab is a view of that page (`?tab=`), not a place of its own. */
export interface ComponentTab {
  readonly value: string
  readonly label: string
  readonly unit: string
  readonly design: string
  readonly built: boolean
}

export const componentTabs: readonly ComponentTab[] = [
  { value: 'overview', label: 'Overview', unit: 'D2', design: 'azure-service-bus-detail', built: true },
  { value: 'dead-letters', label: 'Dead-letter messages', unit: 'D3', design: 'p05a-service-bus-dead-letters', built: true },
  { value: 'stuck', label: 'Stuck messages', unit: 'G5', design: 'p05b-service-bus-stuck', built: false },
  { value: 'recovery', label: 'Recovery', unit: 'D4', design: 'p05c-service-bus-recovery', built: true },
  { value: 'access', label: 'Access', unit: 'D5', design: 'p05d-service-bus-access', built: true },
  { value: 'configuration', label: 'Configuration', unit: 'D6', design: 'p05e-service-bus-configuration', built: true },
  { value: 'audit', label: 'Audit', unit: 'D7', design: 'p05f-service-bus-audit', built: true },
]

export function isNextPage(entry: NextEntry): entry is NextPage {
  return entry.kind === 'page'
}

/** Every route of the new layout. Only pages are routes. */
export const nextPages: readonly NextPage[] = nextNavigation.filter(isNextPage)

/** Entries in one group, in declaration order. */
export function nextEntriesIn(group: NextGroup): readonly NextEntry[] {
  return nextNavigation.filter((entry) => entry.group === group)
}

export function nextEntry(id: string): NextEntry {
  const found = nextNavigation.find((entry) => entry.id === id)
  if (!found) throw new Error(`No 5.0.0 navigation entry with id "${id}"`)
  return found
}

/** Values that fill a page's path. A missing value is an error, never a silent `undefined` in an address. */
export interface NextParams {
  readonly cloud?: string
  readonly component?: string
  readonly namespaceId?: string
}

/**
 * Where an entry takes you. A page's path is filled from `params`; a dialog or panel opens over `currentPath`.
 * This is the only place an address of the new layout is built.
 */
export function nextHref(entry: NextEntry, params: NextParams = {}, currentPath: string = NEXT_ROOT): string {
  if (entry.kind !== 'page') return `${currentPath}?${entry.kind}=${entry.value}`
  const filled = entry.path.replace(/:(\w+)/g, (_whole, name: string) => {
    const value = params[name as keyof NextParams]
    if (!value) throw new Error(`"${entry.id}" needs "${name}" to build its address`)
    return encodeURIComponent(value)
  })
  return filled ? `${NEXT_ROOT}/${filled}` : NEXT_ROOT
}

/** How much of the new layout is real. Used by the placeholder and by the progress test. */
export function nextProgress(): { readonly built: number; readonly total: number } {
  const all = [...nextNavigation, ...componentTabs]
  return { built: all.filter((e) => e.built).length, total: all.length }
}
