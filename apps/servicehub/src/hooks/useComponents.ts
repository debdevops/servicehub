import { useQuery } from '@tanstack/react-query'
import { fetchComponent, fetchComponents } from '../lib/api/components'
import type { CloudKind } from '../lib/api/coverage'

export const componentKeys = {
  list: (cloud: CloudKind | null) => ['components', cloud] as const,
  one: (namespaceId: string, componentKey: string) => ['components', 'one', namespaceId, componentKey] as const,
}

/** Every connected component, for one cloud or (null) all. The one read behind Home's table, the all-clouds cards and a component's page. */
export function useComponents(cloud: CloudKind | null) {
  return useQuery({ queryKey: componentKeys.list(cloud), queryFn: () => fetchComponents(cloud ?? undefined) })
}

/** One component with all its entities. Disabled until both parts of its address are known. */
export function useComponent(namespaceId: string | undefined, componentKey: string | undefined) {
  return useQuery({
    queryKey: componentKeys.one(namespaceId ?? '', componentKey ?? ''),
    queryFn: () => fetchComponent(namespaceId!, componentKey!),
    enabled: Boolean(namespaceId && componentKey),
  })
}
