import { useQueries } from '@tanstack/react-query'
import { fetchEntities, fetchNamespaceStats, type Namespace } from '../lib/api/namespaces'
import { summarise, type CloudSummary } from '../lib/homeSummary'
import { namespaceKeys } from './useNamespaces'

export type ProviderSummary =
  | { readonly status: 'loading' }
  | { readonly status: 'error'; readonly retry: () => void }
  | { readonly status: 'ready'; readonly summary: CloudSummary }

/**
 * Everything one cloud's Home shows, from `GET /namespaces/{id}/stats` and `/entities` for each of
 * its namespaces. Nothing here comes from another cloud's namespaces.
 */
export function useProviderSummary(namespaces: readonly Namespace[]): ProviderSummary {
  const stats = useQueries({
    queries: namespaces.map((n) => ({ queryKey: namespaceKeys.stats(n.id), queryFn: () => fetchNamespaceStats(n.id) })),
  })
  const entities = useQueries({
    queries: namespaces.map((n) => ({ queryKey: namespaceKeys.entities(n.id), queryFn: () => fetchEntities(n.id) })),
  })

  const all = [...stats, ...entities]
  if (all.some((q) => q.isError)) return { status: 'error', retry: () => all.forEach((q) => void q.refetch()) }
  if (all.some((q) => !q.isSuccess)) return { status: 'loading' }

  return {
    status: 'ready',
    summary: summarise(
      stats.map((q) => q.data!),
      entities.map((q) => q.data!.entities),
    ),
  }
}
