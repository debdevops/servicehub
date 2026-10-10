import { createContext, useContext, useEffect } from 'react'

/** What an overlay body may say about itself when its frame's default title would mislead for what it is showing. */
export interface OverlayTitleOverride {
  readonly title: string
  readonly description?: string
}

/** Provided by the host around each overlay body; the default does nothing, so a body renders the same outside a host. */
export const OverlayTitleContext = createContext<(override: OverlayTitleOverride | null) => void>(() => undefined)

/**
 * Lets the body retitle its own frame while it is mounted. The Approve modal also answers "an attempt has no recorded answer",
 * which is not an approval — its frame must not say "Approve" over a question that has nothing to approve.
 */
export function useOverlayTitle(title: string, description?: string): void {
  const set = useContext(OverlayTitleContext)
  useEffect(() => {
    set({ title, description })
    return () => set(null)
  }, [set, title, description])
}
