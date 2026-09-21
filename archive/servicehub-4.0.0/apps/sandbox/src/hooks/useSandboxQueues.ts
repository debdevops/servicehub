import { useQuery } from '@tanstack/react-query';
import { getMockQueues } from '@servicehub/ui-shared/lib/demo/mockProviders';
import type { Queue } from '@servicehub/ui-shared/lib/api/types';
import { useSandboxData } from '../providers/SandboxDataProvider';

export function useSandboxQueues() {
  const { cloudProvider } = useSandboxData();

  return useQuery<Queue[]>({
    queryKey: ['sandbox', 'queues', cloudProvider],
    queryFn: () => Promise.resolve(getMockQueues(cloudProvider)),
    staleTime: Infinity,
  });
}
