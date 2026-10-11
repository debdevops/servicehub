import { Suspense, useState } from 'react'
import { useLocation, useSearchParams } from 'react-router-dom'
import { visibleEntries, type OverlayEntry } from '../../nav/navigation'
import { OverlayFrame } from './OverlayFrame'
import { guideTopic } from '../../content/guides/links'
import { screenHelpStep } from '../../content/help'
import { useGuideCloud } from '../help/useGuideCloud'
import { OverlayTitleContext, type OverlayTitleOverride } from './overlayTitle'
import { overlayBodies, overlayCompanionParams, overlayWide, overlayWider } from './registry'

type OverlayKind = 'modal' | 'panel'

/**
 * Opens the registered component for `?modal=` and `?panel=` (D45).
 *
 * The URL is the only switch: a page never opens an overlay from local state, so every overlay is
 * linkable, survives a refresh, and closes with Esc, ✕ or the browser's Back — closing is removing
 * the parameter. A value that names nothing, or names something not offered right now (Replay
 * before any cloud is connected), opens nothing.
 *
 * `?tab=` is not handled here: a tab is a view of a page's table, and the page reads it.
 */
export function OverlayHost({ connectedCloudCount }: { connectedCloudCount: number }) {
  const [params, setParams] = useSearchParams()
  const { pathname } = useLocation()
  const offered = visibleEntries(connectedCloudCount)
  const guideCloud = useGuideCloud()
  // A body may retitle its own frame (see overlayTitle.ts), keyed by the overlay so one never retitles another.
  const [titles, setTitles] = useState<Readonly<Record<string, OverlayTitleOverride>>>({})
  const retitle = (key: string) => (override: OverlayTitleOverride | null) =>
    setTitles((all) => {
      if (override === null) {
        if (!(key in all)) return all
        return Object.fromEntries(Object.entries(all).filter(([k]) => k !== key))
      }
      const same = all[key]?.title === override.title && all[key]?.description === override.description
      return same ? all : { ...all, [key]: override }
    })

  const find = (kind: OverlayKind): OverlayEntry | undefined => {
    const value = params.get(kind)
    if (value === null) return undefined
    // On Home, Auto Replay is a page section (HomePage renders it), not a side panel.
    return offered.find((e): e is OverlayEntry => e.kind === kind && e.value === value && !(e.id === 'auto-replay' && pathname === '/'))
  }

  const close = (kind: OverlayKind, id: string) => () => {
    // Replace, so the closed state is not a second history entry that Back would reopen.
    setParams(
      (current) => {
        const next = new URLSearchParams(current)
        next.delete(kind)
        if (!next.has('modal') && !next.has('panel')) // `entry` is the Approve modal's, but it is also the Recovery Ledger's selected entry: only Approve may clear it.
        overlayCompanionParams.forEach((name) => { if (name !== 'entry' || id === 'approve') next.delete(name) })
        return next
      },
      { replace: true },
    )
  }

  // The (?) swaps whatever is open for the Help panel, on this screen's step.
  const helpHref = (id: string) => {
    const step = screenHelpStep[id]
    if (!step || id === 'help') return undefined
    const next = new URLSearchParams(params)
    next.delete('modal'); next.delete('panel')
    next.set('panel', 'help'); next.set('topic', guideTopic(guideCloud)); next.set('step', step)
    return `${pathname}?${next}`
  }

  // A panel can sit under a modal (Replay opened from the rules panel), so both may be open.
  const open = (['panel', 'modal'] as const).flatMap((kind) => {
    const entry = find(kind)
    return entry ? [{ kind, entry }] : []
  })

  return (
    <>
      {open.map(({ kind, entry }) => {
        const Body = overlayBodies[entry.id]
        const onClose = close(kind, entry.id)
        return (
          <OverlayFrame key={`${kind}:${entry.id}`} kind={kind} title={titles[`${kind}:${entry.id}`]?.title ?? entry.label} description={titles[`${kind}:${entry.id}`]?.description ?? entry.description} onClose={onClose} helpHref={helpHref(entry.id)} size={overlayWider.has(entry.id) ? 'wider' : overlayWide.has(entry.id) ? 'wide' : 'default'}>
            {Body ? (
              <Suspense fallback={<p className="text-sm text-[var(--color-text-muted)]">Loading…</p>}>
                <OverlayTitleContext.Provider value={retitle(`${kind}:${entry.id}`)}>
                  <Body entry={entry} close={onClose} />
                </OverlayTitleContext.Provider>
              </Suspense>
            ) : (
              <p className="inline-block rounded-full bg-[var(--color-surface-muted)] px-4 py-1.5 text-sm text-[var(--color-text-muted)]">
                Not built yet — Wave {entry.wave}
              </p>
            )}
          </OverlayFrame>
        )
      })}
    </>
  )
}
