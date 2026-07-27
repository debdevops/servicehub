import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { scheduledApi } from '@/lib/api/scheduled';
import { ApiError } from '@/lib/api/types';
import { useDemoContext, rejectDemoModeMutation } from '@/lib/demo/DemoContext';
import toast from 'react-hot-toast';

export function useScheduledMessages(namespaceId: string, queueName: string) {
  const { isDemoMode } = useDemoContext();

  return useQuery({
    queryKey: ['scheduled-messages', namespaceId, queueName],
    queryFn: () => scheduledApi.listScheduled(namespaceId, queueName),
    enabled: !isDemoMode && !!namespaceId && !!queueName,
    staleTime: 15_000,
    refetchInterval: 30_000,
    refetchIntervalInBackground: false,
    retry: (failureCount, error: ApiError) => {
      const status = error?.response?.status ?? 0;
      if (status === 404 || status === 401 || status === 403) return false;
      if (status === 429) return false;
      if (status >= 500) return false;
      return failureCount < 2;
    },
  });
}

export function useCancelScheduledMessage() {
  const queryClient = useQueryClient();
  const { isDemoMode } = useDemoContext();

  return useMutation({
    mutationFn: ({
      namespaceId,
      queueName,
      sequenceNumber,
    }: {
      namespaceId: string;
      queueName: string;
      sequenceNumber: number;
    }) =>
      isDemoMode
        ? rejectDemoModeMutation()
        : scheduledApi.cancelScheduled(namespaceId, queueName, sequenceNumber),

    onSuccess: (_data, variables) => {
      toast.success('Scheduled message cancelled');
      queryClient.invalidateQueries({
        queryKey: ['scheduled-messages', variables.namespaceId, variables.queueName],
      });
      queryClient.invalidateQueries({
        queryKey: ['queues', variables.namespaceId],
      });
      queryClient.invalidateQueries({
        queryKey: ['namespace-stats', variables.namespaceId],
      });
    },

    onError: (error: ApiError) => {
      const message =
        error?.response?.data?.detail ||
        error?.response?.data?.title ||
        'Failed to cancel scheduled message';
      toast.error(message);
    },
  });
}
