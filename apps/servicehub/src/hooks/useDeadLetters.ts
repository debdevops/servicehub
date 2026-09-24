import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { fetchDeadLetters, type DeadLetterQuery } from '../lib/api/deadLetters'

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
    placeholderData: keepPreviousData,
  })
}
