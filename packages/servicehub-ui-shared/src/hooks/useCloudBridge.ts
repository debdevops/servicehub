import { useQuery, UseQueryOptions } from '@tanstack/react-query';
import { cloudBridgeApi, type CloudEntity, type ProviderCapabilitiesMap, type VisibilityStatus } from '../lib/api/cloudBridge';
import type { ApiError } from '../lib/api/types';
import { useDemoContext } from '../lib/demo/DemoContext';
import { getMockCapabilitiesMap } from '../lib/demo/mockProviders';

/** Returns the registration status of every supported cloud provider. */
export function useProviderStatus() {
  const { isDemoMode } = useDemoContext();

  return useQuery({
    queryKey: ['cloud-bridge', 'provider-status'],
    queryFn: cloudBridgeApi.getProviderStatus,
    enabled: !isDemoMode,
    staleTime: 30_000,
    retry: 1,
  });
}

/**
 * Returns what each cloud provider genuinely supports (message counts, manual dead-letter,
 * purge, scheduled messages) — the single source of truth for provider-gated UI, replacing
 * ad-hoc per-page capability checks. Capabilities are static platform facts, so this is
 * cached generously and never depends on which provider flags are enabled.
 *
 * In Demo Mode this must NOT hit the real API — every other demo data hook (useNamespaces,
 * useMessages, ...) already short-circuits to mock data, and capabilities is a platform fact
 * about the demo's own active provider, not something a live backend round-trip should decide.
 */
export function useProviderCapabilities() {
  const { isDemoMode } = useDemoContext();

  const options: UseQueryOptions<ProviderCapabilitiesMap> = isDemoMode
    ? {
        queryKey: ['cloud-bridge', 'capabilities', 'demo'],
        queryFn: (): Promise<ProviderCapabilitiesMap> => Promise.resolve(getMockCapabilitiesMap()),
        staleTime: Infinity,
      }
    : {
        queryKey: ['cloud-bridge', 'capabilities'],
        queryFn: cloudBridgeApi.getCapabilities,
        staleTime: 5 * 60_000,
        retry: 1,
      };

  return useQuery(options);
}

export interface UseCloudEntitiesParams {
  namespaceId: string | null;
  provider: string | null;
}

/** Lists all cloud messaging entities for the given namespace and provider. */
export function useCloudEntities({ namespaceId, provider }: UseCloudEntitiesParams) {
  const { isDemoMode } = useDemoContext();

  return useQuery<CloudEntity[], ApiError>({
    queryKey: ['cloud-bridge', 'entities', namespaceId, provider],
    queryFn: () => cloudBridgeApi.listEntities(namespaceId!, provider!),
    enabled: !isDemoMode && !!namespaceId && !!provider,
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
  const { isDemoMode } = useDemoContext();

  return useQuery<VisibilityStatus, ApiError>({
    queryKey: ['cloud-bridge', 'visibility', namespaceId, queueName, provider],
    queryFn: () => cloudBridgeApi.getVisibilityStatus(namespaceId!, queueName!, provider!),
    enabled: !isDemoMode && !!namespaceId && !!queueName && !!provider,
    staleTime: 5_000,
    retry: (failureCount, error) => {
      if (error?.response?.status === 404) return false;
      if ((error?.response?.status ?? 0) >= 500) return false;
      return failureCount < 2;
    },
  });
}
