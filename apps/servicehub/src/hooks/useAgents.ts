import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { fetchAgentActivity, fetchAgents, pauseAgent, resumeAgent } from '../lib/api/agents'

export const agentKeys = {
  all: ['agents'] as const,
  list: () => [...agentKeys.all, 'list'] as const,
  activity: (id: string | null) => [...agentKeys.all, 'activity', id] as const,
}

/** The agents running now. Refreshed every 15 s so "last run" and health stay true without a reload. */
export function useAgents() {
  return useQuery({ queryKey: agentKeys.list(), queryFn: fetchAgents, refetchInterval: 15_000 })
}

export function useAgentActivity(id: string | null) {
  return useQuery({ queryKey: agentKeys.activity(id), queryFn: () => fetchAgentActivity(id as string), enabled: id !== null, refetchInterval: 15_000 })
}

/** Pause or resume several agents at once (Home's bar pauses every acting agent). One after another, all audited. */
export function useSetAgentsPaused() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: async ({ ids, paused }: { ids: readonly string[]; paused: boolean }) => {
      for (const id of ids) await (paused ? pauseAgent(id) : resumeAgent(id))
    },
    onSettled: () => client.invalidateQueries({ queryKey: agentKeys.all }),
  })
}
