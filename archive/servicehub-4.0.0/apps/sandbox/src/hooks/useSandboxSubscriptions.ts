import { useQuery } from '@tanstack/react-query';
import { getMockSubscriptions } from '@servicehub/ui-shared/lib/demo/mockProviders';
import type { Subscription } from '@servicehub/ui-shared/hooks/useSubscriptions';
import { useSandboxData } from '../providers/SandboxDataProvider';

export function useSandboxSubscriptions(topicName: string) {
  const { cloudProvider } = useSandboxData();

  return useQuery<Subscription[]>({
    queryKey: ['sandbox', 'subscriptions', cloudProvider, topicName],
    queryFn: () => Promise.resolve(getMockSubscriptions(cloudProvider, topicName)),
    enabled: !!topicName,
    staleTime: Infinity,
  });
}
