import { useCallback } from 'react'
import type { PendingWorkItem } from '../lib/api/pendingWork'
import { useNamespaces } from './useNamespaces'

/**
 * Whether a waiting item is on a cloud where a person decides every replay and always will: one that cannot prove a message left
 * the queue (`canProveDlqAbsence` false — AWS and Google), so a fix can never be verified and ServiceHub can never earn the right
 * to replay on its own there. Read from the namespace's capability, never from the provider's name (rule R4).
 *
 * For those items the Simple screens say only what is needed — a person approves — and nothing about agents or earning trust.
 */
export function useManualApproval(): (item: PendingWorkItem) => boolean {
  const namespaces = useNamespaces().data
  return useCallback(
    (item) => {
      if (!namespaces) return false
      const own = item.namespaceId ? namespaces.find((n) => n.id === item.namespaceId) : undefined
      if (own?.capabilities) return !own.capabilities.canProveDlqAbsence
      const sameCloud = namespaces.filter((n) => n.provider === item.provider && n.capabilities)
      return sameCloud.length > 0 && sameCloud.every((n) => !n.capabilities!.canProveDlqAbsence)
    },
    [namespaces],
  )
}

/** The same question for a whole scope of namespaces: every one of them is on a cloud that can never verify a fix. */
export const allManual = (namespaces: readonly { capabilities: { canProveDlqAbsence: boolean } | null }[]): boolean =>
  namespaces.length > 0 && namespaces.every((n) => n.capabilities !== null && !n.capabilities.canProveDlqAbsence)

/** Whether this cloud is one where a person decides every replay (see {@link useManualApproval}). */
export function useCloudIsManual(provider: string | undefined): boolean {
  const namespaces = useNamespaces().data
  return allManual((namespaces ?? []).filter((n) => n.provider === provider))
}
