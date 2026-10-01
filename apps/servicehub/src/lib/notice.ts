import { useSyncExternalStore } from 'react'

/**
 * A result that has to outlive the panel it came from: a purged dead letter has nowhere left to be shown, so its outcome is said
 * here, once, in a corner. One notice at a time — a newer one replaces the older.
 */
export interface Notice {
  readonly id: number
  readonly tone: 'good' | 'warn' | 'bad'
  readonly title: string
  readonly text: string
}

let current: Notice | null = null
let next = 1
const listeners = new Set<() => void>()
const emit = () => listeners.forEach((l) => l())

export function showNotice(n: Omit<Notice, 'id'>): void {
  current = { ...n, id: next++ }
  emit()
}

export function dismissNotice(): void {
  current = null
  emit()
}

export function useNotice(): Notice | null {
  return useSyncExternalStore((cb) => { listeners.add(cb); return () => { listeners.delete(cb) } }, () => current)
}
