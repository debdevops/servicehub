import { Suspense, useCallback, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { OverlayFrame } from '../../components/overlays/OverlayFrame'
import { OverlayTitleContext, type OverlayTitleOverride } from '../../components/overlays/overlayTitle'
import { nextNavigation, type NextOverlay } from '../nav/navigation'
import { nextOverlayBodies, nextOverlayCompanions, nextOverlayWide } from '../overlays/registry'

type Kind = 'dialog' | 'panel'

/**
 * Opens the registered component for `?dialog=` and `?panel=` over any page of the 5.0.0 shell (build-plan unit F1, host half).
 *
 * The URL is the only switch: a page never opens an overlay from local state, so every overlay is linkable, survives a refresh, and
 * closes with Esc, the ✕ or the browser's Back — closing is removing the parameter. A value that names nothing opens nothing; one that
 * names an overlay not built yet opens a frame that says so, never nothing. `?tab=` is a page's, not handled here.
 */
export function OverlayHost() {
  const [params, setParams] = useSearchParams()
  const [titles, setTitles] = useState<Readonly<Record<string, OverlayTitleOverride>>>({})
  // One setter per overlay, created once. A body re-runs its retitle effect whenever the setter it is given changes, so a setter that was
  // new on every render would set state, re-render the host, hand over a new setter, and loop for ever.
  const setters = useRef(new Map<string, (override: OverlayTitleOverride | null) => void>())
  const retitle = useCallback((key: string) => {
    let set = setters.current.get(key)
    if (!set) {
      set = (override) =>
        setTitles((all) => {
          if (override === null) return key in all ? Object.fromEntries(Object.entries(all).filter(([k]) => k !== key)) : all
          return all[key]?.title === override.title && all[key]?.description === override.description ? all : { ...all, [key]: override }
        })
      setters.current.set(key, set)
    }
    return set
  }, [])

  const find = (kind: Kind): NextOverlay | undefined => {
    const value = params.get(kind)
    return value === null ? undefined : nextNavigation.find((e): e is NextOverlay => e.kind === kind && e.value === value)
  }

  const close = (kind: Kind) => () =>
    // Replace, so the closed state is not a second history entry that Back would reopen.
    setParams((current) => {
      const next = new URLSearchParams(current)
      next.delete(kind)
      if (!next.has('dialog') && !next.has('panel')) nextOverlayCompanions.forEach((name) => next.delete(name))
      return next
    }, { replace: true })

  const open = (['panel', 'dialog'] as const).flatMap((kind) => {
    const entry = find(kind)
    return entry ? [{ kind, entry }] : []
  })

  return (
    <>
      {open.map(({ kind, entry }) => {
        const Body = nextOverlayBodies[entry.id]
        const onClose = close(kind)
        const key = `${kind}:${entry.id}`
        return (
          <OverlayFrame key={key} kind={kind === 'dialog' ? 'modal' : 'panel'} title={titles[key]?.title ?? entry.label} description={titles[key]?.description ?? entry.description}
            onClose={onClose} size={nextOverlayWide.has(entry.id) ? 'wide' : 'default'}>
            {Body ? (
              <Suspense fallback={<p role="status" className="text-sm text-[var(--color-text-muted)]">Loading…</p>}>
                <OverlayTitleContext.Provider value={retitle(key)}>
                  <Body entry={entry} close={onClose} />
                </OverlayTitleContext.Provider>
              </Suspense>
            ) : (
              <div className="py-6 text-center">
                <p className="text-sm font-semibold">Not built yet — unit {entry.unit}</p>
                <p className="mt-1 text-xs text-[var(--color-text-muted)]">Design: <span className="font-mono">{entry.design}</span></p>
              </div>
            )}
          </OverlayFrame>
        )
      })}
    </>
  )
}
