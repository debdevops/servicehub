import { useQuery, useMutation, useQueryClient, UseQueryOptions } from '@tanstack/react-query';
import toast from 'react-hot-toast';
import { governanceApi, type GovernanceGrant, type GrantGovernanceRoleRequest } from '../lib/api/governance';
import { useDemoContext, rejectDemoModeMutation } from '../lib/demo/DemoContext';

/**
 * Hook for fetching the caller's active Governance/RBAC grants (M3 of the persistence wave,
 * roadmap item 10's enforcement layer). Demo Mode has no synthetic grant fixture — rather than
 * fabricate one, it honestly reports an empty list, same reasoning as `usePlaybookEntries`.
 */
export function useGovernanceGrants(granteeIdentity?: string) {
  const { isDemoMode } = useDemoContext();

  const options: UseQueryOptions<GovernanceGrant[]> = isDemoMode
    ? {
        queryKey: ['governance-grants', 'demo', granteeIdentity],
        queryFn: (): Promise<GovernanceGrant[]> => Promise.resolve([]),
      }
    : {
        queryKey: ['governance-grants', granteeIdentity],
        queryFn: () => governanceApi.getGrants(granteeIdentity),
        enabled: !isDemoMode,
        staleTime: 15_000,
        retry: (failureCount, error: unknown) => {
          const err = error as { response?: { status?: number } };
          if (err?.response?.status === 403) return false;
          return failureCount < 2;
        },
      };

  return useQuery(options);
}

/** Hook for granting a Governance role to an identity, scoped to an optional namespace/pillar. */
export function useGrantGovernanceRole() {
  const { isDemoMode } = useDemoContext();
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: GrantGovernanceRoleRequest) =>
      isDemoMode ? rejectDemoModeMutation() : governanceApi.grant(request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['governance-grants'] });
      toast.success('Grant created');
    },
    onError: (error: unknown) => {
      const err = error as { response?: { data?: { detail?: string; message?: string } }; message?: string };
      const msg = err?.response?.data?.detail || err?.response?.data?.message || err?.message || 'Failed to create grant';
      toast.error(msg);
    },
  });
}

/** Hook for revoking one Governance grant by ID. Soft-revoke only — never deleted server-side. */
export function useRevokeGovernanceGrant() {
  const { isDemoMode } = useDemoContext();
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => (isDemoMode ? rejectDemoModeMutation() : governanceApi.revoke(id)),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['governance-grants'] });
      toast.success('Grant revoked');
    },
    onError: (error: unknown) => {
      const err = error as { response?: { data?: { detail?: string; message?: string } }; message?: string };
      const msg = err?.response?.data?.detail || err?.response?.data?.message || err?.message || 'Failed to revoke grant';
      toast.error(msg);
    },
  });
}
