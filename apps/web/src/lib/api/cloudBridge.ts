import { apiClient } from './client';

/** Backend CloudEntity shape from ServiceHub.Core.Models.CloudEntity */
export interface CloudEntity {
  name: string;
  displayName?: string;
  entityType: string;
  provider: string;
  region?: string;
  projectId?: string;
  messageCount?: number;
  activeMessageCount?: number;
  dlqMessageCount?: number;
  metadata?: Record<string, string>;
}

/** Provider status map from GET /cloud-bridge/provider-status */
export type ProviderStatusMap = Record<string, boolean>;

/** AWS SQS visibility info from GET /cloud-bridge/namespaces/{id}/visibility/{queue}?provider=Aws */
export interface SqsVisibilityInfo {
  inFlightCount: number;
  visibilityTimeoutSeconds: number;
  dlqCount: number;
}

/** GCP Pub/Sub ack-deadline status from the same endpoint with ?provider=Gcp */
export interface GcpAckDeadlineStatus {
  ackDeadlineSeconds: number;
  hasDeadLetterPolicy: boolean;
  deadLetterTopic?: string | null;
  maxDeliveryAttempts?: number | null;
  messageOrderingEnabled: boolean;
}

export type VisibilityStatus = SqsVisibilityInfo | GcpAckDeadlineStatus;

export const cloudBridgeApi = {
  /** GET /api/v1/cloud-bridge/provider-status */
  getProviderStatus: async (): Promise<ProviderStatusMap> => {
    const response = await apiClient.get<ProviderStatusMap>('/cloud-bridge/provider-status');
    return response.data;
  },

  /** GET /api/v1/cloud-bridge/namespaces/{namespaceId}/entities?provider={provider} */
  listEntities: async (namespaceId: string, provider: string): Promise<CloudEntity[]> => {
    const response = await apiClient.get<Array<Record<string, unknown>>>(
      `/cloud-bridge/namespaces/${namespaceId}/entities`,
      { params: { provider } },
    );
    // Normalise backend field names (activeMessageCount → messageCount alias)
    return response.data.map((e) => ({
      ...(e as unknown as CloudEntity),
      messageCount: (e.messageCount ?? e.activeMessageCount) as number | undefined,
      dlqMessageCount: (e.dlqMessageCount ?? e.deadLetterCount) as number | undefined,
    }));
  },

  /** GET /api/v1/cloud-bridge/namespaces/{namespaceId}/visibility/{queueName}?provider={provider} */
  getVisibilityStatus: async (
    namespaceId: string,
    queueName: string,
    provider: string,
  ): Promise<VisibilityStatus> => {
    const response = await apiClient.get<VisibilityStatus>(
      `/cloud-bridge/namespaces/${namespaceId}/visibility/${encodeURIComponent(queueName)}`,
      { params: { provider } },
    );
    return response.data;
  },
};
