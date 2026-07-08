import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/lib/api/client';
import { Topic, ApiError } from '@/lib/api/types';
import { useDemoContext } from '@/lib/demo/DemoContext';
import { getMockTopics } from '@/lib/demo/mockProviders';

export function useTopics(namespaceId: string, autoRefresh: boolean = true) {
  const { isDemoMode, cloudProvider } = useDemoContext();

  const options = isDemoMode && cloudProvider
    ? {
        queryKey: ['topics', 'demo', cloudProvider] as [string, string, string],
        queryFn: (): Promise<Topic[]> => Promise.resolve(getMockTopics(cloudProvider)),
        staleTime: Infinity as number,
        enabled: true,
        refetchInterval: false as const,
        refetchIntervalInBackground: false,
        retry: false as const,
      }
    : {
        queryKey: ['topics', namespaceId] as [string, string],
        queryFn: async (): Promise<Topic[]> => {
          const response = await apiClient.get<Topic[]>(`/namespaces/${namespaceId}/topics`, {
            _silent: true,
          });
          return response.data;
        },
        enabled: !!namespaceId,
        staleTime: 15_000,
        refetchInterval: autoRefresh ? 30_000 : (false as const),
        refetchIntervalInBackground: false,
        retry: (failureCount: number, error: ApiError) => {
          if (error?.response?.status === 404) return false;
          if (error?.response?.status === 429) return false;
          if ((error?.response?.status ?? 0) >= 500) return false;
          return failureCount < 2;
        },
      };

  return useQuery<Topic[], ApiError>(options as Parameters<typeof useQuery<Topic[], ApiError>>[0]);
}
