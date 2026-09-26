import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as s from '../lib/api/settings'
import { identityKeys } from './useIdentity'

export const settingsKeys = { all: ['settings'] as const, stop: ['settings', 'emergency-stop'] as const, grants: ['settings', 'grants'] as const }

export function useSettings() {
  return useQuery({ queryKey: settingsKeys.all, queryFn: s.fetchSettings })
}

/** Read by the banner on every screen, so it is polled: an emergency stop must never go unseen. */
export function useEmergencyStop() {
  return useQuery({ queryKey: settingsKeys.stop, queryFn: s.fetchEmergencyStop, refetchInterval: 30_000 })
}

export function useGrants(enabled: boolean) {
  return useQuery({ queryKey: settingsKeys.grants, queryFn: s.fetchGrants, enabled, retry: false })
}

function useRefresh() {
  const client = useQueryClient()
  return () => {
    void client.invalidateQueries({ queryKey: settingsKeys.all })
    void client.invalidateQueries({ queryKey: identityKeys.me })
  }
}

export function useSettingsMutation<TArgs, TResult>(fn: (args: TArgs) => Promise<TResult>) {
  const refresh = useRefresh()
  return useMutation({ mutationFn: fn, onSettled: refresh })
}
