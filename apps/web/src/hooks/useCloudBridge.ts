import { useQuery } from '@tanstack/react-query';
import { cloudBridgeApi, type CloudEntity, type VisibilityStatus } from '@/lib/api/cloudBridge';
import type { ApiError } from '@/lib/api/types';

/** Returns the registration status of every supported cloud provider. */
export function useProviderStatus() {
  return useQuery({
    queryKey: ['cloud-bridge', 'provider-status'],
    queryFn: cloudBridgeApi.getProviderStatus,
    staleTime: 30_000,
    retry: 1,
  });
}

export interface UseCloudEntitiesParams {
  namespaceId: string | null;
  provider: string | null;
}

/** Lists all cloud messaging entities for the given namespace and provider. */
export function useCloudEntities({ namespaceId, provider }: UseCloudEntitiesParams) {
  return useQuery<CloudEntity[], ApiError>({
    queryKey: ['cloud-bridge', 'entities', namespaceId, provider],
    queryFn: () => cloudBridgeApi.listEntities(namespaceId!, provider!),
    enabled: !!namespaceId && !!provider,
    staleTime: 10_000,
    retry: (failureCount, error) => {
      if (error?.response?.status === 404) return false;
      if ((error?.response?.status ?? 0) >= 500) return false;
      return failureCount < 2;
    },
  });
}

export interface UseVisibilityStatusParams {
  namespaceId: string | null;
  queueName: string | null;
  provider: string | null;
}

/** Returns the visibility-window (SQS) or ack-deadline (Pub/Sub) status. */
export function useVisibilityStatus({ namespaceId, queueName, provider }: UseVisibilityStatusParams) {
  return useQuery<VisibilityStatus, ApiError>({
    queryKey: ['cloud-bridge', 'visibility', namespaceId, queueName, provider],
    queryFn: () => cloudBridgeApi.getVisibilityStatus(namespaceId!, queueName!, provider!),
    enabled: !!namespaceId && !!queueName && !!provider,
    staleTime: 5_000,
    retry: (failureCount, error) => {
      if (error?.response?.status === 404) return false;
      if ((error?.response?.status ?? 0) >= 500) return false;
      return failureCount < 2;
    },
  });
}
