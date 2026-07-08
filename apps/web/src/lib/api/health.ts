// Note: Health endpoints are on /api/health, not /api/v1/health
// We use axios directly for this since apiClient has baseURL /api/v1
import axios from 'axios';

const healthClient = axios.create({
  baseURL: '/api/health',
  headers: { 'Content-Type': 'application/json' },
  timeout: 10000,
});

// ASP.NET health check endpoint lives at /health (no /api prefix).
// It returns 503 with a full JSON report when any check is Unhealthy,
// so 503 must be treated as a valid response, not an error.
const healthReportClient = axios.create({
  headers: { 'Content-Type': 'application/json' },
  timeout: 10000,
  validateStatus: (status) => status === 200 || status === 503,
});

export interface VersionInfo {
  version: string;
  informationalVersion: string;
  environment: string;
  machineName: string;
  osDescription: string;
  frameworkDescription: string;
  startedAt: string;
}

export interface HealthReportEntry {
  status: string;
  description?: string | null;
  duration: number;
  data?: Record<string, unknown> | null;
  exception?: string | null;
}

export interface HealthReport {
  status: string;
  totalDuration: number;
  entries: Record<string, HealthReportEntry>;
}

export interface StatusInfo {
  isHealthy: boolean;
  uptime: string;
  memoryUsageMb: number;
  threadCount: number;
  gcTotalMemoryMb: number;
  gen0Collections: number;
  gen1Collections: number;
  gen2Collections: number;
  timestamp: string;
}

export const healthApi = {
  getVersion: async (): Promise<VersionInfo> => {
    const { data } = await healthClient.get<VersionInfo>('/version');
    return data;
  },

  getStatus: async (): Promise<StatusInfo> => {
    const { data } = await healthClient.get<StatusInfo>('/status');
    return data;
  },

  getReport: async (): Promise<HealthReport> => {
    const { data } = await healthReportClient.get<HealthReport>('/health');
    return data;
  },
};
