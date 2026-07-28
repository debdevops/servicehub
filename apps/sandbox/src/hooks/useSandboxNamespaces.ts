import { useQuery } from '@tanstack/react-query';
import { getMockNamespaces } from '@servicehub/ui-shared/lib/demo/mockProviders';
import type { Namespace } from '@servicehub/ui-shared/lib/api/types';
import { useSandboxData } from '../providers/SandboxDataProvider';

export function useSandboxNamespaces() {
  const { cloudProvider } = useSandboxData();

  return useQuery<Namespace[]>({
    queryKey: ['sandbox', 'namespaces', cloudProvider],
    queryFn: () => Promise.resolve(getMockNamespaces(cloudProvider)),
    staleTime: Infinity,
  });
}
