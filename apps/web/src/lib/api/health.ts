import { apiClient } from './client';

// Note: Health endpoints are on /api/health, not /api/v1/health
// We use axios directly for this since apiClient has baseURL /api/v1
import axios from 'axios';

const healthClient = axios.create({
  baseURL: '/api/health',
  headers: { 'Content-Type': 'application/json' },
  timeout: 10000,
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
};
