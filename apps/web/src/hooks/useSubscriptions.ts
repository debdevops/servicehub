import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/lib/api/client';
import { ApiError } from '@/lib/api/types';
import { useDemoContext } from '@/lib/demo/DemoContext';
import { getMockSubscriptions } from '@/lib/demo/mockProviders';

export interface Subscription {
  name: string;
  activeMessageCount: number;
  deadLetterMessageCount: number;
  topicName: string;
  status: string;
}

export function useSubscriptions(namespaceId: string, topicName: string, autoRefresh: boolean = true) {
  const { isDemoMode, cloudProvider } = useDemoContext();

  const options = isDemoMode && cloudProvider
    ? {
        queryKey: ['subscriptions', 'demo', cloudProvider, topicName] as [string, string, string, string],
        queryFn: (): Promise<Subscription[]> => Promise.resolve(getMockSubscriptions(cloudProvider, topicName)),
        enabled: !!topicName,
        staleTime: Infinity as number,
        refetchInterval: false as const,
        refetchIntervalInBackground: false,
        retry: false as const,
      }
    : {
        queryKey: ['subscriptions', namespaceId, topicName] as [string, string, string],
        queryFn: async (): Promise<Subscription[]> => {
          const response = await apiClient.get<Subscription[]>(
            `/namespaces/${namespaceId}/topics/${topicName}/subscriptions`,
            { _silent: true }
          );
          return response.data;
        },
        enabled: !!namespaceId && !!topicName,
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

  return useQuery<Subscription[], ApiError>(options as Parameters<typeof useQuery<Subscription[], ApiError>>[0]);
}
