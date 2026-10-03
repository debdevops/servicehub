import { useQuery } from '@tanstack/react-query';
import { getMockStats, type MockNamespaceStats } from '@servicehub/ui-shared/lib/demo/mockProviders';
import { useDemoData } from '../providers/DemoDataProvider';

export function useDemoStats() {
  const { cloudProvider } = useDemoData();

  return useQuery<MockNamespaceStats>({
    queryKey: ['demo', 'stats', cloudProvider],
    queryFn: () => Promise.resolve(getMockStats(cloudProvider)),
    staleTime: Infinity,
  });
}
