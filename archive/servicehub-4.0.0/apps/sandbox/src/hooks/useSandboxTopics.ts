import { useQuery } from '@tanstack/react-query';
import { getMockTopics } from '@servicehub/ui-shared/lib/demo/mockProviders';
import type { Topic } from '@servicehub/ui-shared/lib/api/types';
import { useSandboxData } from '../providers/SandboxDataProvider';

export function useSandboxTopics() {
  const { cloudProvider } = useSandboxData();

  return useQuery<Topic[]>({
    queryKey: ['sandbox', 'topics', cloudProvider],
    queryFn: () => Promise.resolve(getMockTopics(cloudProvider)),
    staleTime: Infinity,
  });
}
