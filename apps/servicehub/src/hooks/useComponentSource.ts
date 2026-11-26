import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { fetchComponentSource, fetchFailedItem, fetchFailedItems, fetchRedriveCheck, redriveRun } from '../lib/api/componentSources'

export const componentSourceKeys = {
  source: (namespaceId: string, componentKey: string) => ['component-source', namespaceId, componentKey] as const,
  failed: (namespaceId: string, componentKey: string, place: string | null) => ['component-source', namespaceId, componentKey, 'failed', place] as const,
  item: (namespaceId: string, componentKey: string, itemId: string) => ['component-source', namespaceId, componentKey, 'item', itemId] as const,
}

/** Whether a connection can read a component that is not a message store, and its places. */
export function useComponentSource(namespaceId: string, componentKey: string) {
  return useQuery({ queryKey: componentSourceKeys.source(namespaceId, componentKey), queryFn: () => fetchComponentSource(namespaceId, componentKey) })
}

/** Failed runs or deliveries, newest first. Not fetched until the connection is known to be readable. */
export function useFailedItems(namespaceId: string, componentKey: string, place: string | null, enabled: boolean) {
  return useQuery({ queryKey: componentSourceKeys.failed(namespaceId, componentKey, place), queryFn: () => fetchFailedItems(namespaceId, componentKey, place), enabled })
}

/** One item in full. Read only when a person opens it, so a listing never costs a call per row. */
export function useFailedItem(namespaceId: string, componentKey: string, itemId: string | null) {
  return useQuery({
    queryKey: componentSourceKeys.item(namespaceId, componentKey, itemId ?? ''),
    queryFn: () => fetchFailedItem(namespaceId, componentKey, itemId!),
    enabled: itemId !== null,
  })
}

/** Whether a redrive is allowed now. Never cached across opens: the answer can change as soon as another request is made. */
export function useRedriveCheck(namespaceId: string | null, componentKey: string | null, itemId: string | null) {
  return useQuery({
    queryKey: ['component-redrive-check', namespaceId, componentKey, itemId] as const,
    queryFn: () => fetchRedriveCheck(namespaceId!, componentKey!, itemId!),
    enabled: namespaceId !== null && componentKey !== null && itemId !== null,
    retry: false,
    gcTime: 0,
  })
}

/** Asks the cloud to redrive the run. Never retried: a second attempt is a decision for a person, not the client. */
export function useRedrive() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (v: { namespaceId: string; componentKey: string; itemId: string }) => redriveRun(v.namespaceId, v.componentKey, v.itemId),
    retry: false,
    onSettled: () => {
      void client.invalidateQueries({ queryKey: ['component-source'] })
      void client.invalidateQueries({ queryKey: ['recovery'] })
    },
  })
}
