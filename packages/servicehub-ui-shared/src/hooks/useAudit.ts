import { useQuery } from '@tanstack/react-query';
import { auditApi, type AuditParams } from '../lib/api/audit';
import { useDemoContext } from '../lib/demo/DemoContext';

/**
 * Hook for fetching paginated audit log entries.
 */
export function useAuditLogs(params: AuditParams, enabled = true) {
  const { isDemoMode } = useDemoContext();

  return useQuery({
    queryKey: ['audit-logs', params],
    queryFn: () => auditApi.getLogs(params),
    enabled: !isDemoMode && enabled,
    staleTime: 30_000,
    refetchInterval: 60_000,
    refetchIntervalInBackground: false,
    retry: (failureCount, error: unknown) => {
      const err = error as { response?: { status?: number } };
      if (err?.response?.status === 404) return false;
      if (err?.response?.status === 403) return false;
      return failureCount < 2;
    },
  });
}

/**
 * Hook for fetching audit trail summary statistics.
 */
export function useAuditSummary(namespaceId?: string, enabled = true) {
  const { isDemoMode } = useDemoContext();

  return useQuery({
    queryKey: ['audit-summary', namespaceId],
    queryFn: () => auditApi.getSummary(namespaceId),
    enabled: !isDemoMode && enabled,
    staleTime: 60_000,
    refetchInterval: 120_000,
    refetchIntervalInBackground: false,
    retry: (failureCount, error: unknown) => {
      const err = error as { response?: { status?: number } };
      if (err?.response?.status === 404) return false;
      if (err?.response?.status === 403) return false;
      return failureCount < 2;
    },
  });
}
