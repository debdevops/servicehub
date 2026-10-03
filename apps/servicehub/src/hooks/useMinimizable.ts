import { useState } from 'react'

const storageKey = (id: string) => `servicehub.minimized.${id}`

function wasMinimized(id: string): boolean {
  try {
    return window.localStorage.getItem(storageKey(id)) === 'minimized'
  } catch {
    return false
  }
}

function remember(id: string, minimized: boolean) {
  try {
    if (minimized) window.localStorage.setItem(storageKey(id), 'minimized')
    else window.localStorage.removeItem(storageKey(id))
  } catch {
    // Remembering is a convenience. If storage is blocked the panel simply shows in full next visit.
  }
}

/**
 * Whether a large panel is minimized, remembered per browser — the same "dismiss, then a small
 * control brings it back" shape `useExplainer` uses for the "what you're looking at" cards,
 * generalized to any panel across ServiceHub that can take a lot of vertical space (e.g. "Needs
 * your attention"). Nothing is ever lost when minimized, just collapsed to its header until
 * restored. `id` should be stable and unique per panel (e.g. `'needs-your-attention'`) so
 * different panels don't share state.
 */
export function useMinimizable(id: string) {
  const [minimized, setMinimized] = useState(() => wasMinimized(id))
  return {
    minimized,
    minimize: () => {
      remember(id, true)
      setMinimized(true)
    },
    restore: () => {
      remember(id, false)
      setMinimized(false)
    },
  }
}
