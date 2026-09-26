import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { keepWithinScope } from '../lib/keepWithinScope'
import { fetchDeadLetters, fetchDeadLetterTrend, type DeadLetterQuery, type TrendScope } from '../lib/api/deadLetters'
import { lookAtDeadLetters, type CloudProvider, type DeadLetterLook } from '../lib/api/namespaces'

/** What looking at several namespaces found, added up. `failed` counts namespaces that could not be read. */
export interface LookSummary {
  readonly queuesExamined: number
  readonly newMessages: number
  readonly resolved: number
  readonly unconfirmed: number
  readonly failed: number
  readonly reasons: readonly string[]
  readonly at: Date
}

export function summariseLooks(results: readonly DeadLetterLook[], at: Date): LookSummary {
  const looked = results.filter((r) => r.outcome === 'looked')
  const sum = (pick: (r: DeadLetterLook) => number) => looked.reduce((n, r) => n + pick(r), 0)
  return {
    queuesExamined: sum((r) => r.queuesExamined),
    newMessages: sum((r) => r.newMessages),
    resolved: sum((r) => r.resolved),
    unconfirmed: sum((r) => r.unconfirmed),
    failed: results.length - looked.length,
    reasons: [...new Set(results.filter((r) => r.outcome !== 'looked' && r.reason).map((r) => r.reason as string))],
    at,
  }
}

/**
 * Looks at the dead letters of each namespace in scope, one after another (a cloud's receives must not pile up),
 * then makes every list that reads the recorded rows look again.
 */
export function useLookAtDeadLetters() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (namespaceIds: readonly string[]) => {
      const results: DeadLetterLook[] = []
      for (const id of namespaceIds) results.push(await lookAtDeadLetters(id))
      return summariseLooks(results, new Date())
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: deadLetterKeys.all })
      void queryClient.invalidateQueries({ queryKey: ['audit'] })
    },
  })
}

export const deadLetterKeys = {
  all: ['dead-letters'] as const,
  list: (query: DeadLetterQuery) => [...deadLetterKeys.all, 'list', query] as const,
}

/**
 * A page of one cloud's dead letters. The previous page stays on screen while the next loads, so
 * paging and filtering never flash empty. No polling: what refreshes it is the person asking, and
 * ServiceHub's own scan behind the list (unit 2.1).
 */
export function useDeadLetters(query: DeadLetterQuery) {
  return useQuery({
    queryKey: deadLetterKeys.list(query),
    queryFn: () => fetchDeadLetters(query),
    placeholderData: keepWithinScope(query.provider, query),
  })
}

/** New vs resolved per day. Kept on screen while the range changes, so switching 7 → 14 days never flashes empty. */
export function useDeadLetterTrend(provider: CloudProvider, days: number, narrow: TrendScope = {}) {
  return useQuery({
    queryKey: [...deadLetterKeys.all, 'trend', provider, days, narrow] as const,
    queryFn: () => fetchDeadLetterTrend(provider, days, narrow),
    placeholderData: keepWithinScope(provider, narrow),
  })
}
