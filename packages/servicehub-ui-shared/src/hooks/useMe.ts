import { useQuery, UseQueryOptions } from '@tanstack/react-query';
import { meApi, type MeResponse } from '../lib/api/me';
import { useDemoContext } from '../lib/demo/DemoContext';

/**
 * Hook for fetching the caller's own identity and fleet-wide effective Governance role, so the
 * UI can show "what can I do here" without a 403 round-trip. Demo Mode has no real caller or
 * Governance grants — rather than fabricate a role, it honestly reports `governanceRole: null`
 * (the same "not yet activated" meaning `GovernanceGrantsPage`'s empty state already uses),
 * same reasoning as `useGovernanceGrants`.
 */
export function useMe() {
  const { isDemoMode } = useDemoContext();

  const options: UseQueryOptions<MeResponse> = isDemoMode
    ? {
        queryKey: ['me', 'demo'],
        queryFn: (): Promise<MeResponse> =>
          Promise.resolve({ ownerId: 'demo', authMethod: 'Demo', governanceRole: null }),
      }
    : {
        queryKey: ['me'],
        queryFn: () => meApi.getMe(),
        enabled: !isDemoMode,
        staleTime: 60_000,
        retry: 1,
      };

  return useQuery(options);
}
