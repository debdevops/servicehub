import { useQuery } from '@tanstack/react-query';
import { getMockMessages } from '@servicehub/ui-shared/lib/demo/mockProviders';
import type { Message, PaginatedResponse } from '@servicehub/ui-shared/lib/api/types';
import { useDemoData } from '../providers/DemoDataProvider';

export function useDemoMessages() {
  const { cloudProvider } = useDemoData();

  return useQuery<PaginatedResponse<Message>>({
    queryKey: ['demo', 'messages', cloudProvider],
    queryFn: () => Promise.resolve(getMockMessages(cloudProvider, 'all', 'active')),
    staleTime: Infinity,
  });
}
