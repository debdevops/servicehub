import type { ComponentType } from 'react'
import {
  Bot,
  Boxes,
  CircleHelp,
  Fingerprint,
  Home,
  Inbox,
  Layers,
  LayoutGrid,
  ListChecks,
  Play,
  Plus,
  RotateCcw,
  ScrollText,
  Settings,
  ShieldCheck,
  Timer,
  Cable,
} from 'lucide-react'

/**
 * THE navigation array — every destination in the product, of every kind.
 *
 * The sidebar, the route table, the overlay registry, the command palette and every breadcrumb are
 * derived from this one list. One list means it cannot drift.
 * `navigation.test.ts` asserts the router and this array cannot disagree.
 *
 * Most work happens in place (D45): Simple has two PAGES; everything else is a TAB of Home's table,
 * a side PANEL or a MODAL, each carried in the URL (?tab= · ?panel= · ?modal=) so it is linkable and
 * survives a refresh. Adding a destination of any kind is one entry here (ARCHITECTURE §4.5).
 *
 * Every entry belongs to exactly one surface — Simple, where people act, or Advanced, where they
 * investigate (ADR-0016). Advanced paths live under /advanced; there is no mode flag.
 */

/** Simple gives the answer and the action. Advanced gives the investigation, read-only in 4.1.0. */
export type Surface = 'simple' | 'advanced'

/** Where every Advanced path lives. */
export const ADVANCED_ROOT = '/advanced'

/**
 * What kind of destination an entry is.
 *   page  — a route. Only pages are routes.
 *   tab   — a view of a page's table: `<page>?tab=<value>`.
 *   panel — a side panel over whatever page you are on: `?panel=<value>`.
 *   modal — a dialog over whatever page you are on: `?modal=<value>`.
 */
export type NavKind = 'page' | 'tab' | 'panel' | 'modal'

/** Where an entry appears in the sidebar. `contextual` entries are opened from the page, not the menu. */
export type NavGroup = 'primary' | 'work' | 'clouds' | 'utility' | 'advanced' | 'contextual'

/** A condition that decides whether an entry is shown at all. */
export type NavVisibility =
  /** Always shown. */
  | 'always'
  /** Shown once at least one cloud is connected — there is nothing to work on before that. */
  | 'connected'
  /** Shown only when more than one cloud is connected — it is the only cross-cloud view. */
  | 'multiCloud'

interface NavCommon {
  /** Stable id. Used by the command palette and by tests; never reused for a different screen. */
  readonly id: string
  /** What a person calls it. */
  readonly label: string
  /** One short line, for the palette and for a tooltip. */
  readonly description: string
  readonly icon: ComponentType<{ className?: string }>
  readonly surface: Surface
  readonly group: NavGroup
  readonly visibility: NavVisibility
  /** The wave that builds it. Kept here so an unbuilt screen is never silently listed as done. */
  readonly wave: number
}

export interface PageEntry extends NavCommon {
  readonly kind: 'page'
  /** The route path. Query parameters carry the state; the path never does. */
  readonly path: string
}

export interface TabEntry extends NavCommon {
  readonly kind: 'tab'
  /** The page whose table this tab switches. */
  readonly path: string
  /** The `?tab=` value. */
  readonly value: string
}

export interface OverlayEntry extends NavCommon {
  readonly kind: 'panel' | 'modal'
  /** The `?panel=` or `?modal=` value. Opens over whatever page is current. */
  readonly value: string
}

export type NavEntry = PageEntry | TabEntry | OverlayEntry

export const navigation: readonly NavEntry[] = [
  // ── Simple · pages — the only two ─────────────────────────────────────────────────────────────
  // Fleet Overview leads, because it is the only cross-cloud view and Home is deliberately one
  // cloud. It appears only when there is more than one cloud to compare.
  {
    kind: 'page', id: 'fleet', label: 'Fleet Overview', path: '/fleet',
    description: 'Across every cloud you have connected, where is the trouble?',
    icon: Layers, surface: 'simple', group: 'primary', visibility: 'multiCloud', wave: 3,
  },
  {
    kind: 'page', id: 'home', label: 'Home', path: '/',
    description: 'What needs you, and the state of the one cloud you are working in. The welcome, before anything is connected.',
    icon: Home, surface: 'simple', group: 'primary', visibility: 'always', wave: 1,
  },

  // ── Simple · work — views of Home's table and the rules panel ─────────────────────────────────
  {
    kind: 'tab', id: 'dlq', label: 'Dead letters', path: '/', value: 'dlq',
    description: 'What is dead-lettered, and why — replay happens right here, in the drawer.',
    icon: Inbox, surface: 'simple', group: 'work', visibility: 'connected', wave: 2,
  },
  {
    kind: 'tab', id: 'active', label: 'Active messages', path: '/', value: 'active',
    description: 'What is in flight right now.',
    icon: Boxes, surface: 'simple', group: 'work', visibility: 'connected', wave: 3,
  },
  {
    kind: 'tab', id: 'replayed', label: 'Replayed', path: '/', value: 'replayed',
    description: 'What was replayed, by whom, and whether it stayed fixed.',
    icon: RotateCcw, surface: 'simple', group: 'work', visibility: 'connected', wave: 2,
  },
  {
    kind: 'panel', id: 'auto-replay', label: 'Auto Replay', value: 'rules',
    description: 'What ServiceHub is allowed to retry on its own.',
    icon: Timer, surface: 'simple', group: 'work', visibility: 'connected', wave: 3,
  },

  // ── Simple · clouds and utilities ─────────────────────────────────────────────────────────────
  {
    kind: 'panel', id: 'connections', label: 'Connections', value: 'connections',
    description: 'Every cloud you have connected — is it reachable, and when did it last answer?',
    icon: Cable, surface: 'simple', group: 'clouds', visibility: 'connected', wave: 1,
  },
  {
    kind: 'modal', id: 'add-cloud', label: 'Add a cloud', value: 'add-cloud',
    description: 'Connect Azure, AWS or Google Cloud — and see what it can prove.',
    icon: Plus, surface: 'simple', group: 'clouds', visibility: 'always', wave: 1,
  },
  {
    kind: 'modal', id: 'settings', label: 'Settings', value: 'settings',
    description: 'Connections, notifications and preferences.',
    icon: Settings, surface: 'simple', group: 'utility', visibility: 'always', wave: 6,
  },
  {
    kind: 'panel', id: 'help', label: 'Help', value: 'help',
    description: 'How do I do the thing I am trying to do?',
    icon: CircleHelp, surface: 'simple', group: 'utility', visibility: 'always', wave: 6,
  },

  // ── Simple · contextual — opened from the page, never from the menu ───────────────────────────
  {
    kind: 'modal', id: 'replay', label: 'Replay', value: 'replay',
    description: 'The proposal for one message: what will happen, the checks, then replay.',
    icon: Play, surface: 'simple', group: 'contextual', visibility: 'connected', wave: 2,
  },
  {
    kind: 'modal', id: 'bulk-replay', label: 'Bulk Replay', value: 'bulk-replay',
    description: 'Put many messages back, with a preview first.',
    icon: ListChecks, surface: 'simple', group: 'contextual', visibility: 'connected', wave: 3,
  },
  {
    kind: 'modal', id: 'approve', label: 'Approve', value: 'approve',
    description: 'What the Agent stopped and asked you about, and why.',
    icon: ShieldCheck, surface: 'simple', group: 'contextual', visibility: 'connected', wave: 5,
  },

  // ── Advanced — four read-only pages (ADR-0016, D45) ───────────────────────────────────────────
  // Each arrives in the wave that produces its data. None of them can act: replay and approval
  // happen in Simple's modals; an agent's pause is the one control Advanced carries.
  {
    kind: 'page', id: 'advanced-overview', label: 'Advanced Overview', path: ADVANCED_ROOT,
    description: 'What happened, why the system decided it, and what needs a person — broken down.',
    icon: LayoutGrid, surface: 'advanced', group: 'advanced', visibility: 'always', wave: 6,
  },
  {
    kind: 'page', id: 'ledger', label: 'Recovery Ledger', path: `${ADVANCED_ROOT}/ledger`,
    description: 'Every recovery action, its evidence and its outcome. Its Waiting tab is the approval queue.',
    icon: ScrollText, surface: 'advanced', group: 'advanced', visibility: 'always', wave: 2,
  },
  {
    kind: 'page', id: 'signatures', label: 'Failure Signatures', path: `${ADVANCED_ROOT}/signatures`,
    description: 'Failures grouped by how they fail — what the rules and the Agent reason about.',
    icon: Fingerprint, surface: 'advanced', group: 'advanced', visibility: 'always', wave: 3,
  },
  {
    kind: 'page', id: 'agents', label: 'Agents', path: `${ADVANCED_ROOT}/agents`,
    description: 'What machinery is running, what each one may do, and what it has done.',
    icon: Bot, surface: 'advanced', group: 'advanced', visibility: 'always', wave: 4,
  },
]

/**
 * The ceilings (PLAN 1.1, ADR-0016 as amended by D45), counted as pages plus panels plus modals —
 * everything a person has to learn as a place. Tabs are views of a table, not places, and are not
 * counted. Passing a ceiling is a decision recorded in the decisions log, never an extra entry.
 */
export const SCREEN_CEILING: Readonly<Record<Surface, number>> = { simple: 12, advanced: 4 }

export function isPage(entry: NavEntry): entry is PageEntry {
  return entry.kind === 'page'
}

/** Every route in the product. Only pages are routes. */
export const pages: readonly PageEntry[] = navigation.filter(isPage)

/** Where an entry takes you. Panels and modals open over the page you are on. */
export function hrefOf(entry: NavEntry, currentPath = '/'): string {
  switch (entry.kind) {
    case 'page':
      return entry.path
    case 'tab':
      return `${entry.path}?tab=${entry.value}`
    case 'panel':
    case 'modal':
      return `${currentPath}?${entry.kind}=${entry.value}`
  }
}

/** Which surface a location belongs to. The URL is the only place this is decided. */
export function surfaceOf(pathname: string): Surface {
  return pathname === ADVANCED_ROOT || pathname.startsWith(`${ADVANCED_ROOT}/`) ? 'advanced' : 'simple'
}

/** Entries on one surface, in declaration order. */
export function entriesOn(surface: Surface): readonly NavEntry[] {
  return navigation.filter((entry) => entry.surface === surface)
}

/** Entries in a sidebar group, in declaration order. */
export function entriesInGroup(group: NavGroup): readonly NavEntry[] {
  return navigation.filter((entry) => entry.group === group)
}

/**
 * Which entries to show, given what is actually connected.
 *
 * Nothing here is configurable: the sidebar reflects the truth about this installation. Greyed-out
 * rows for unconnected clouds were considered and rejected — they are permanent adverts on the
 * product's most valuable screen real-estate.
 */
export function visibleEntries(connectedCloudCount: number): readonly NavEntry[] {
  return navigation.filter((entry) => {
    switch (entry.visibility) {
      case 'multiCloud':
        return connectedCloudCount > 1
      case 'connected':
        return connectedCloudCount > 0
      case 'always':
        return true
    }
  })
}

/**
 * Where a visitor lands, computed from what is connected rather than from a setting (IA §2.4).
 *
 *   0 clouds → Home, which is the welcome until something is connected (D45 — no Connect page)
 *   1 cloud  → Home, because Fleet Overview would repeat it exactly
 *   2+       → Fleet Overview, because picking one cloud for someone would be arbitrary
 */
export function landingPath(connectedCloudCount: number): string {
  return connectedCloudCount > 1 ? '/fleet' : '/'
}
