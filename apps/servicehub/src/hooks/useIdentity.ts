import { keepWithinScope } from '../lib/keepWithinScope'
import { useQuery } from '@tanstack/react-query'
import { fetchAudit, fetchMe, type AuditFilter } from '../lib/api/identity'

export const identityKeys = {
  me: ['me'] as const,
  audit: (filter: AuditFilter) => ['audit', filter] as const,
}

/** Who is acting, and what they may do. Roles can be granted or revoked while the page is open, so it is re-read now and then. */
export function useMe() {
  return useQuery({ queryKey: identityKeys.me, queryFn: fetchMe, staleTime: 60_000 })
}

/** Recent activity. Refreshed on focus (the default), because someone else may have acted. */
export function useAudit(filter: AuditFilter = {}) {
  return useQuery({ queryKey: identityKeys.audit(filter), queryFn: () => fetchAudit(filter), placeholderData: keepWithinScope(filter.provider, filter) })
}
