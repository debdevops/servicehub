import { Suspense } from 'react'
import { useSearchParams } from 'react-router-dom'
import { visibleEntries, type OverlayEntry } from '../../nav/navigation'
import { OverlayFrame } from './OverlayFrame'
import { overlayBodies, overlayCompanionParams, overlayWide } from './registry'

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
  const offered = visibleEntries(connectedCloudCount)

  const find = (kind: OverlayKind): OverlayEntry | undefined => {
    const value = params.get(kind)
    if (value === null) return undefined
    return offered.find((e): e is OverlayEntry => e.kind === kind && e.value === value)
  }

  const close = (kind: OverlayKind) => () => {
    // Replace, so the closed state is not a second history entry that Back would reopen.
    setParams(
      (current) => {
        const next = new URLSearchParams(current)
        next.delete(kind)
        if (!next.has('modal') && !next.has('panel')) overlayCompanionParams.forEach((name) => next.delete(name))
        return next
      },
      { replace: true },
    )
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
        const onClose = close(kind)
        return (
          <OverlayFrame key={`${kind}:${entry.id}`} kind={kind} title={entry.label} description={entry.description} onClose={onClose} size={overlayWide.has(entry.id) ? 'wide' : 'default'}>
            {Body ? (
              <Suspense fallback={<p className="text-sm text-[var(--color-text-muted)]">Loading…</p>}>
                <Body entry={entry} close={onClose} />
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
