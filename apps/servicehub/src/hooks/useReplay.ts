import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { keepWithinScope } from '../lib/keepWithinScope'
import { fetchReplayProposal, fetchReplays, replayMessage, type ReplayQuery } from '../lib/api/replay'
import { deadLetterKeys } from './useDeadLetters'

export const replayKeys = {
  all: ['replays'] as const,
  proposal: (id: number | null) => [...replayKeys.all, 'proposal', id] as const,
  list: (query: ReplayQuery) => [...replayKeys.all, 'list', query] as const,
}

/** What replaying would do. Never cached across opens: a proposal older than a minute may no longer be true. */
export function useReplayProposal(id: number | null) {
  return useQuery({
    queryKey: replayKeys.proposal(id),
    queryFn: () => fetchReplayProposal(id as number),
    enabled: id !== null,
    retry: false,
    gcTime: 0,
  })
}

/** Runs the replay. Never retried — a second attempt is a decision for a person, not the client. */
export function useReplay() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => replayMessage(id),
    retry: false,
    onSettled: () => {
      void client.invalidateQueries({ queryKey: deadLetterKeys.all })
      void client.invalidateQueries({ queryKey: replayKeys.all })
    },
  })
}

export function useReplays(query: ReplayQuery) {
  return useQuery({ queryKey: replayKeys.list(query), queryFn: () => fetchReplays(query), placeholderData: keepWithinScope(query.provider, query) })
}
