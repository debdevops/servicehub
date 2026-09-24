import { api } from './client'

/** What `/health` reports. */
export interface HealthStatus {
  readonly status: string
}

/**
 * Liveness. Deliberately the only API call the Wave 0 skeleton makes — there is no product data
 * to fetch yet, and inventing some would break rule R5 on the very first screen.
 */
export async function fetchHealth(): Promise<HealthStatus> {
  // /health sits outside /api/v1 — it is an infrastructure probe, not a product resource.
  const response = await api.get<string | HealthStatus>('/health', { baseURL: '/' })
  return typeof response.data === 'string' ? { status: response.data } : response.data
}
