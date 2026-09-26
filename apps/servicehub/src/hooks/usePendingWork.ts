import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { toProblem } from '../lib/api/client'
import { approvePending, declinePending, fetchPendingWork, type PendingWorkScope } from '../lib/api/pendingWork'
import { deadLetterKeys } from './useDeadLetters'
import { recoveryKeys } from './useRecoverySummary'
import { replayKeys } from './useReplay'

export const pendingKeys = {
  all: ['pending-work'] as const,
  list: (scope: PendingWorkScope) => [...pendingKeys.all, scope] as const,
}

/**
 * What is waiting for a person — the ONE query behind the bell, Home's Needs-you strip and the Ledger's Waiting tab.
 * Also polled every 30 s: the stream is a hint that can drop, and an escalation must never be lost to that.
 */
export function usePendingWork(scope: PendingWorkScope = {}) {
  return useQuery({ queryKey: pendingKeys.list(scope), queryFn: () => fetchPendingWork(scope), refetchInterval: 30_000 })
}

function useInvalidateAfterAnswer() {
  const client = useQueryClient()
  return () => {
    void client.invalidateQueries({ queryKey: pendingKeys.all })
    void client.invalidateQueries({ queryKey: recoveryKeys.all })
    void client.invalidateQueries({ queryKey: replayKeys.all })
    void client.invalidateQueries({ queryKey: deadLetterKeys.all })
  }
}

/** Approve several, one after another (each its own gated replay and ledger entry). Returns what happened to each. */
export function useApprovePending() {
  const refresh = useInvalidateAfterAnswer()
  return useMutation({
    mutationFn: async (entryIds: readonly string[]) => {
      const results: { entryId: string; ok: boolean; error?: string }[] = []
      for (const entryId of entryIds) {
        try {
          await approvePending(entryId)
          results.push({ entryId, ok: true })
        } catch (e) {
          results.push({ entryId, ok: false, error: toProblem(e).message })
        }
      }
      return results
    },
    onSettled: refresh,
  })
}

export function useDeclinePending() {
  const refresh = useInvalidateAfterAnswer()
  return useMutation({
    mutationFn: async ({ entryIds, reason }: { entryIds: readonly string[]; reason: string }) => {
      for (const id of entryIds) await declinePending(id, reason)
    },
    onSettled: refresh,
  })
}
