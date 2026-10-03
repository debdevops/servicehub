import type { Namespace } from '../api/namespaces'

/** The connection pill: only what the last real test said, never an assumed "Connected". */
export function connectionState(namespaces: readonly Namespace[]): { label: string; ok: boolean | null } {
  if (namespaces.every((n) => n.lastConnectionTestSucceeded === true)) return { label: 'Connected', ok: true }
  if (namespaces.some((n) => n.lastConnectionTestSucceeded === false)) return { label: 'Could not connect at last check', ok: false }
  return { label: 'Not tested yet', ok: null }
}
