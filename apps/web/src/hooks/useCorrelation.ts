import { useMutation } from '@tanstack/react-query';
import { correlationApi } from '@/lib/api/correlation';
import { ApiError } from '@/lib/api/types';
import toast from 'react-hot-toast';

export function useCorrelationSearch() {
  return useMutation({
    mutationFn: ({
      correlationId,
      namespaceId,
    }: {
      correlationId: string;
      namespaceId?: string;
    }) => correlationApi.searchTimeline(correlationId, namespaceId),
    onError: (error: ApiError) => {
      const msg =
        error?.response?.data?.detail ||
        error?.message ||
        'Correlation search failed';
      toast.error(msg, { duration: 5000 });
    },
  });
}
