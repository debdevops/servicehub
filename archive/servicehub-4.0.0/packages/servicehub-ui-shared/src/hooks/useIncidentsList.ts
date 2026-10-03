import { useQuery, UseQueryOptions } from '@tanstack/react-query';
import { incidentsListApi, type IncidentListResponse } from '@servicehub/ui-shared/lib/api/incidentsList';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { getMockIncidentsList } from '@servicehub/ui-shared/lib/demo/mockProviders';

export type {
  IncidentListResponse,
  IncidentListMetrics,
  IncidentTrendPoint,
  IncidentCategoryBreakdown,
  IncidentListItem,
  IncidentSeverity,
} from '@servicehub/ui-shared/lib/api/incidentsList';

/** The Incident Center's fleet-wide incident list (Incident Center redesign). */
export function useIncidentsList(days: number) {
  const { isDemoMode, cloudProvider } = useDemoContext();

  const options: UseQueryOptions<IncidentListResponse, Error> =
    isDemoMode && cloudProvider
      ? {
          queryKey: ['incidents-list', 'demo', cloudProvider, days],
          queryFn: (): Promise<IncidentListResponse> => Promise.resolve(getMockIncidentsList(cloudProvider, days)),
        }
      : {
          queryKey: ['incidents-list', days],
          queryFn: () => incidentsListApi.get(days),
          enabled: !isDemoMode,
          refetchInterval: 60000,
        };

  return useQuery(options);
}
