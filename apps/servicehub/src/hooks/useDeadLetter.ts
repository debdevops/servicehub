import { useQuery } from '@tanstack/react-query'
import { fetchDeadLetter } from '../lib/api/deadLetters'
import { deadLetterKeys } from './useDeadLetters'

/** One opened dead letter. A missing one (404) is an answer, not a fault, so it is not retried. */
export function useDeadLetter(id: number | null) {
  return useQuery({
    queryKey: [...deadLetterKeys.all, 'one', id],
    queryFn: () => fetchDeadLetter(id as number),
    enabled: id !== null,
    retry: false,
  })
}
