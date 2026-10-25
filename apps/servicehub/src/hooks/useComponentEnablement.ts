import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { checkComponent, fetchComponentList, fetchEnabledComponents, setComponent } from '../lib/api/componentEnablement'

export const componentKeys = {
  all: ['components'] as const,
  list: (namespaceId: string) => [...componentKeys.all, 'list', namespaceId] as const,
  enabled: () => [...componentKeys.all, 'enabled'] as const,
}

/** What can be brought in on one connection, and what already is. */
export function useComponentList(namespaceId: string | undefined) {
  return useQuery({ queryKey: componentKeys.list(namespaceId ?? ''), queryFn: () => fetchComponentList(namespaceId!), enabled: namespaceId !== undefined,
    // Switching connection keeps the rows on screen until the new list arrives, so what the person is working on does not jump.
    placeholderData: keepPreviousData })
}

/** Every component ticked on any connection: the menu lists a read-on-demand component only where someone turned it on. */
export function useEnabledComponents() {
  return useQuery({ queryKey: componentKeys.enabled(), queryFn: fetchEnabledComponents })
}

/** Check access. A mutation because it asks the cloud, even though it changes nothing. */
export function useCheckComponent(namespaceId: string) {
  return useMutation({ mutationFn: (componentKey: string) => checkComponent(namespaceId, componentKey) })
}

export function useSetComponent(namespaceId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ componentKey, enabled }: { componentKey: string; enabled: boolean }) => setComponent(namespaceId, componentKey, enabled),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: componentKeys.all }),
  })
}
