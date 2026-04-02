import { useMutation } from '@tanstack/react-query';
import { correlationApi } from '@/lib/api/correlation';
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
    onError: (error: any) => {
      const msg =
        error?.response?.data?.detail ||
        error?.message ||
        'Correlation search failed';
      toast.error(msg, { duration: 5000 });
    },
  });
}
