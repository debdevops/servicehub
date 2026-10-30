import { lazy, type ComponentType, type LazyExoticComponent } from 'react'
import type { NextOverlay } from '../nav/navigation'

/** What every 5.0.0 overlay body receives. The frame around it (title, ✕, Esc, focus) is the host's job. */
export interface NextOverlayBodyProps {
  readonly entry: NextOverlay
  readonly close: () => void
}

/**
 * The overlays that are built, keyed by their navigation entry id (`nextNavigation`). Adding one is a lazy import here plus the entry
 * that already exists in the array: the host never changes. An entry with no body opens a plain "not built yet" frame, so a link to it
 * is never silently dead (R5). `tests/web/unit/next/overlayHost.test.tsx` checks every key here names an entry in the array.
 */
export const nextOverlayBodies: Readonly<Record<string, LazyExoticComponent<ComponentType<NextOverlayBodyProps>>>> = {
  message: lazy(() => import('./MessageDialog')),
  review: lazy(() => import('./ReviewDialog')),
  connect: lazy(() => import('./ConnectDialog')),
  'add-component': lazy(() => import('./AddComponentDialog')),
  help: lazy(() => import('./HelpPanel')),
  security: lazy(() => import('./SecurityPanel')),
  autonomy: lazy(() => import('./AutonomyPanel')),
  send: lazy(() => import('./SendDialog')),
  search: lazy(() => import('./SearchDialog')),
  'emergency-stop': lazy(() => import('./EmergencyStopDialog')),
  'hand-off': lazy(() => import('./HandOffPanel')),
  replay: lazy(() => import('./ReplayDialog')),
  redrive: lazy(() => import('./RedriveDialog')),
  'bulk-replay': lazy(() => import('./BulkReplayDialog')),
}

/**
 * Query parameters an overlay reads beyond `?dialog=` / `?panel=`. They belong to the overlay, so closing it removes them too and a
 * closed dialog leaves no debris in the address. (`entity` and `tab` are the page's, never an overlay's.)
 */
export const nextOverlayCompanions: readonly string[] = ['id', 'entry', 'entries', 'component', 'job', 'topic', 'ns', 'item']

/** Overlays drawn wider than the default: the Message dialog is read side by side (summary and properties). */
export const nextOverlayWide: ReadonlySet<string> = new Set(['message', 'review', 'bulk-replay', 'add-component'])

/**
 * Overlays that show one record in full. They open as a docked **side window** beside the page, and the expand button turns the same window
 * into a modal; everything else (a form, a decision, a confirm) is a plain modal or panel. This is the one rule for "details" in 5.0.0.
 */
export const nextOverlayDetails: ReadonlySet<string> = new Set(['message', 'hand-off'])
