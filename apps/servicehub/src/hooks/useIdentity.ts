import { useQuery } from '@tanstack/react-query'
import { fetchAudit, fetchMe, type AuditFilter } from '../lib/api/identity'

export const identityKeys = {
  me: ['me'] as const,
  audit: (filter: AuditFilter) => ['audit', filter] as const,
}

/** Who is acting. Identity does not change mid-session, so it is not re-fetched on focus. */
export function useMe() {
  return useQuery({ queryKey: identityKeys.me, queryFn: fetchMe, staleTime: Infinity, refetchOnWindowFocus: false })
}

/** Recent activity. Refreshed on focus (the default), because someone else may have acted. */
export function useAudit(filter: AuditFilter = {}) {
  return useQuery({ queryKey: identityKeys.audit(filter), queryFn: () => fetchAudit(filter) })
}
