import type { DeadLetterQuery } from './api/deadLetters'

/**
 * What the table hands to Bulk Replay. Either the ids the person ticked, or — for "select all N matching" — the filter
 * itself, which the modal resolves into ids (at most `LIMIT`). It lives here, not in the URL: 500 ids do not belong
 * in an address bar. Nothing here can execute anything; it only says what to PREVIEW.
 */
export type BulkSelection = { readonly ids: readonly number[] } | { readonly query: DeadLetterQuery; readonly total: number }

let current: BulkSelection | null = null
const finishedListeners = new Set<() => void>()

export const bulkSelection = {
  set: (value: BulkSelection | null) => {
    current = value
  },
  get: (): BulkSelection | null => current,
  /** A bulk run has ended: the selection it carried is spent, so a table may untick it. Returns the unsubscribe. */
  onFinished: (listener: () => void) => {
    finishedListeners.add(listener)
    return () => { finishedListeners.delete(listener) }
  },
  finished: () => {
    current = null
    finishedListeners.forEach((l) => l())
  },
}

/** The most one bulk replay carries — the server refuses more. */
export const BULK_LIMIT = 500
