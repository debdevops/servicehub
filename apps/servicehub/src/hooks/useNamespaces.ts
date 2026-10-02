import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  configureDlqObserver,
  connectNamespace,
  fetchDlqObserver,
  fetchEntities,
  fetchNamespace,
  fetchNamespaceStats,
  fetchNamespaces,
  removeNamespace,
  testConnection,
  type ConnectNamespaceInput,
  type DlqObserverInput,
  type EntityKind,
} from '../lib/api/namespaces'

/** Every namespace query hangs off one key, so a mutation can invalidate them together. */
export const namespaceKeys = {
  all: ['namespaces'] as const,
  list: () => [...namespaceKeys.all, 'list'] as const,
  one: (id: string) => [...namespaceKeys.all, 'one', id] as const,
  stats: (id: string) => [...namespaceKeys.all, 'stats', id] as const,
  observer: (id: string) => [...namespaceKeys.all, 'observer', id] as const,
  entities: (id: string, kind?: EntityKind) => [...namespaceKeys.all, 'entities', id, kind ?? 'all'] as const,
}

export function useNamespaces() {
  return useQuery({ queryKey: namespaceKeys.list(), queryFn: fetchNamespaces })
}

export function useNamespace(id: string | undefined) {
  return useQuery({
    queryKey: namespaceKeys.one(id ?? ''),
    queryFn: () => fetchNamespace(id!),
    enabled: id !== undefined,
  })
}

export function useNamespaceStats(id: string | undefined) {
  return useQuery({
    queryKey: namespaceKeys.stats(id ?? ''),
    queryFn: () => fetchNamespaceStats(id!),
    enabled: id !== undefined,
  })
}

export function useEntities(id: string | undefined, kind?: EntityKind) {
  return useQuery({
    queryKey: namespaceKeys.entities(id ?? '', kind),
    queryFn: () => fetchEntities(id!, kind),
    enabled: id !== undefined,
  })
}

export function useConnectNamespace() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: ConnectNamespaceInput) => connectNamespace(input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: namespaceKeys.all }),
  })
}

export function useTestConnection() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => testConnection(id),
    // The test records its outcome on the namespace, so the namespace is stale afterwards.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: namespaceKeys.all }),
  })
}

export function useRemoveNamespace() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => removeNamespace(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: namespaceKeys.all }),
  })
}

/** The DLQ observer's state for one cloud. While it is turned on but not yet proven live, it is re-read so the answer arrives by itself. */
export function useDlqObserver(id: string, enabled = true) {
  return useQuery({
    queryKey: namespaceKeys.observer(id),
    queryFn: () => fetchDlqObserver(id),
    enabled,
    refetchInterval: (query) => (query.state.data?.enabled && !query.state.data.live ? 30_000 : false),
  })
}

export function useConfigureDlqObserver(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: DlqObserverInput) => configureDlqObserver(id, input),
    onSuccess: (data) => queryClient.setQueryData(namespaceKeys.observer(id), data),
  })
}
