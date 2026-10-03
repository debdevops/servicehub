import { useQuery, UseQueryOptions } from '@tanstack/react-query';
import { incidentsApi, type IncidentDetailResponse } from '@servicehub/ui-shared/lib/api/incidents';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { getMockIncidentDetail } from '@servicehub/ui-shared/lib/demo/mockProviders';

export type { IncidentDetailResponse, IncidentSummary } from '@servicehub/ui-shared/lib/api/incidents';

/** The Incident workspace (roadmap W2.3) — one durable view per failure signature. */
export function useIncident(namespaceId?: string, signatureHash?: string) {
  const { isDemoMode, cloudProvider } = useDemoContext();

  const options: UseQueryOptions<IncidentDetailResponse, unknown> = isDemoMode && cloudProvider
    ? {
        queryKey: ['incident', 'demo', cloudProvider, signatureHash],
        queryFn: (): Promise<IncidentDetailResponse> => {
          const detail = getMockIncidentDetail(cloudProvider, signatureHash!);
          if (!detail) return Promise.reject(new Error('Incident not found'));
          return Promise.resolve(detail);
        },
        enabled: !!signatureHash,
        staleTime: Infinity,
        retry: false,
      }
    : {
        queryKey: ['incident', namespaceId, signatureHash],
        queryFn: () => incidentsApi.get(namespaceId!, signatureHash!),
        enabled: !!namespaceId && !!signatureHash,
        staleTime: 30_000,
      };

  return useQuery(options);
}
