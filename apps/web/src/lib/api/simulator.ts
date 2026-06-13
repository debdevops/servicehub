import { apiClient } from './client';

// ── Response types (match SimulatorController DTOs, camelCase) ─────────────

export interface SimulatorNamespaceSummary {
  id: string;
  name: string;
  provider: string;
  activeMessageCount: number;
  dlqMessageCount: number;
  entityCount: number;
}

export interface SimulatorFaultSummary {
  faultType: string;
  targetEntity: string;
  namespaceId: string;
  severity: number;
  expiresAt: string;
}

export interface SimulatorStatus {
  environment: string;
  simulatedUtcNow: string;
  namespaces: SimulatorNamespaceSummary[];
  activeFaultCount: number;
  activeFaults: SimulatorFaultSummary[];
}

// ── Request types ──────────────────────────────────────────────────────────

export interface InjectFaultRequest {
  faultType: 'MaxDelivery' | 'VisibilityExpiry' | 'AckDeadlineStorm' | 'KmsError' | 'OrderingStall' | 'NetworkTimeout';
  namespaceId: string;
  targetEntity?: string;
  severity: number;      // 1–10
  durationSeconds: number;
}

export interface DlqFloodRequest {
  namespaceId: string;
  entityName: string;
  count: number;
  reason: string;
  errorDescription?: string;
  deliveryCount?: number;
}

// ── API functions ──────────────────────────────────────────────────────────

/** GET /api/v1/simulator/status */
export async function getSimulatorStatus(): Promise<SimulatorStatus> {
  const response = await apiClient.get<SimulatorStatus>('/simulator/status');
  return response.data;
}

/** POST /api/v1/simulator/faults */
export async function injectFault(req: InjectFaultRequest): Promise<void> {
  await apiClient.post('/simulator/faults', req);
}

/** DELETE /api/v1/simulator/faults */
export async function clearFaults(): Promise<void> {
  await apiClient.delete('/simulator/faults');
}

/** POST /api/v1/simulator/reset */
export async function resetSimulator(): Promise<void> {
  await apiClient.post('/simulator/reset');
}

/** POST /api/v1/simulator/advance-time  body: { seconds } */
export async function advanceTime(seconds: number): Promise<void> {
  await apiClient.post('/simulator/advance-time', { seconds });
}

/** POST /api/v1/simulator/inject-dlq-flood */
export async function injectDlqFlood(req: DlqFloodRequest): Promise<void> {
  await apiClient.post('/simulator/inject-dlq-flood', req);
}
