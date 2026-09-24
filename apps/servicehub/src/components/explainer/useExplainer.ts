import { useState } from 'react'
import type { ExplanationId } from '../../content/explanations'

const storageKey = (id: ExplanationId) => `servicehub.explainer.${id}`

function wasDismissed(id: ExplanationId): boolean {
  try {
    return window.localStorage.getItem(storageKey(id)) === 'dismissed'
  } catch {
    return false
  }
}

function remember(id: ExplanationId, dismissed: boolean) {
  try {
    if (dismissed) window.localStorage.setItem(storageKey(id), 'dismissed')
    else window.localStorage.removeItem(storageKey(id))
  } catch {
    // Remembering is a convenience. If storage is blocked the card simply shows again next visit.
  }
}

/**
 * The card's shown/dismissed state, remembered per browser. A page calls this once and hands the
 * pieces to <ExplainerCard> (open) and <ExplainerToggle> (the (?) beside the title, once dismissed).
 */
export function useExplainer(id: ExplanationId) {
  const [shown, setShown] = useState(() => !wasDismissed(id))
  return {
    shown,
    dismiss: () => {
      remember(id, true)
      setShown(false)
    },
    show: () => {
      remember(id, false)
      setShown(true)
    },
  }
}
