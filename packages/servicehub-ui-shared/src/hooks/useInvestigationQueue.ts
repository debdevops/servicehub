import { useQuery, UseQueryOptions, UseQueryResult } from '@tanstack/react-query';
import { apiClient } from '@servicehub/ui-shared/lib/api/client';
import type { FleetNamespaceHealth } from '@servicehub/ui-shared/lib/api/fleet';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { getMockInvestigationQueue } from '@servicehub/ui-shared/lib/demo/mockProviders';

export interface CompactMetricsSummary {
  totalSignatures: number;
  activeSignatures: number;
  resolvedSignatures: number;
  suppressedSignatures: number;
  archivedSignatures: number;
  requiresAction: number;
}

export interface InvestigationQueueItem {
  signatureHash: string;
  namespaceId: string;
  displayName: string;
  dominantDeadletterReason: string;
  messageCount: number;
  status: string;
  trend: string;
  priorityScore: number;
  hasKnowledge: boolean;
  isEscalating: boolean;
  owner: string | null;
  recommendedNextAction: string | null;
  explanation: string | null;
}

export interface FailedReplayItem {
  jobId: string;
  namespaceId: string;
  signatureHash: string;
  signatureName: string;
  jobStatus: string;
  failureReason: string | null;
  createdAt: string;
  completedAt: string | null;
  attemptedCount: number;
  failedCount: number;
  recommendedNextAction: string | null;
}

export interface KnowledgeReviewItem {
  signatureHash: string;
  namespaceId: string;
  displayName: string;
  messageCount: number;
  status: string;
  owner: string | null;
  hasKnowledge: boolean;
  isReviewOverdue: boolean;
  reviewDueAt: string | null;
  lastUpdatedAt: string | null;
  recommendedNextAction: string | null;
}

export interface NewSignatureItem {
  signatureHash: string;
  namespaceId: string;
  displayName: string;
  dominantDeadletterReason: string;
  messageCount: number;
  firstSeenAt: string;
  lastSeenAt: string;
  explanation: string | null;
  recommendedNextAction: string | null;
}

export interface RecentChangeItem {
  changeType: string;
  signatureHash: string;
  namespaceId: string;
  signatureName: string;
  occurredAt: string;
  details: string | null;
  actor: string | null;
}

/** Fleet-wide health signal for the Fleet Health section, composed server-side from IFleetOverviewService. */
export interface FleetHealthSummary {
  namespaceCount: number;
  totalActive: number;
  totalNewInWindow: number;
  totalResolvedInWindow: number;
  topUnhealthyNamespaces: FleetNamespaceHealth[];
}

export interface InvestigationCenterResponse {
  metrics: CompactMetricsSummary;
  investigationQueue: InvestigationQueueItem[];
  failedReplays: FailedReplayItem[];
  knowledgeReview: KnowledgeReviewItem[];
  newSignatures: NewSignatureItem[];
  recentlyChanged: RecentChangeItem[];
  fleetHealth: FleetHealthSummary | null;
}

export function useInvestigationQueue(): UseQueryResult<InvestigationCenterResponse, Error> {
  const { isDemoMode, cloudProvider } = useDemoContext();

  const options: UseQueryOptions<InvestigationCenterResponse, Error> =
    isDemoMode && cloudProvider
      ? {
          queryKey: ['investigation-center', 'demo', cloudProvider],
          queryFn: (): Promise<InvestigationCenterResponse> =>
            Promise.resolve(getMockInvestigationQueue(cloudProvider)),
        }
      : {
          queryKey: ['investigation-center'],
          queryFn: async () => {
            const response = await apiClient.get<InvestigationCenterResponse>(
              '/api/v1/failure-intelligence/investigation-center'
            );
            return response.data;
          },
          enabled: !isDemoMode,
          retry: 3,
          retryDelay: (attemptIndex) => Math.min(1000 * 2 ** attemptIndex, 30000),
          refetchInterval: 60000, // Refetch every 60 seconds
        };

  return useQuery(options);
}
