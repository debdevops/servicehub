import { lazy, type ComponentType, type LazyExoticComponent } from 'react'
import { navigation, type OverlayEntry } from '../../nav/navigation'

/** What every overlay body receives. The frame around it (title, ✕, Esc, focus) is the host's job. */
export interface OverlayBodyProps {
  readonly entry: OverlayEntry
  readonly close: () => void
}

/**
 * The overlays that are built, keyed by their navigation entry id.
 *
 * Adding one is a lazy import here plus the entry that already exists in `navigation.ts` — the host
 * never changes. An entry with no component here opens a plain "not built yet" frame rather than
 * nothing, so a link to it is never silently dead (rule R5).
 */
export const overlayBodies: Readonly<Record<string, LazyExoticComponent<ComponentType<OverlayBodyProps>>>> = {
  'add-cloud': lazy(() => import('../connect/AddCloudModal')),
  connections: lazy(() => import('../connect/ConnectionsPanel')),
  replay: lazy(() => import('../message/ReplayModal')),
  'auto-replay': lazy(() => import('../rules/AutoReplayPanel')),
  'bulk-replay': lazy(() => import('../message/BulkReplayModal')),
}

/** Every modal and panel the navigation array lists — the registry test checks `overlayBodies` against it. */
export const overlayEntries = navigation.filter((e): e is OverlayEntry => e.kind === 'modal' || e.kind === 'panel')

/**
 * Extra query parameters an overlay reads (`?cloud=` preselects a cloud in Add a cloud). They belong
 * to the overlay, so closing it removes them too — otherwise a closed modal leaves debris in the URL.
 */
export const overlayCompanionParams: readonly string[] = ['cloud', 'job', 'rule']
