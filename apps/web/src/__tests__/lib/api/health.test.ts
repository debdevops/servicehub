import { describe, it, expect, vi, beforeEach } from 'vitest';
import { healthApi } from '@/lib/api/health';

// Mock axios at the module level
vi.mock('axios', () => {
  const mockInstance = {
    get: vi.fn(),
    post: vi.fn(),
    interceptors: { request: { use: vi.fn() }, response: { use: vi.fn() } },
  };
  return {
    default: { create: vi.fn(() => mockInstance), ...mockInstance },
    __mockInstance: mockInstance,
  };
});

// Re-import to get mock reference
import axios from 'axios';
const mockAxiosInstance = (axios as any).create();

describe('healthApi', () => {
  beforeEach(() => vi.clearAllMocks());

  it('getVersion calls GET /version', async () => {
    const versionData = {
      version: '1.0.0',
      informationalVersion: '1.0.0+abc',
      environment: 'Development',
      machineName: 'test',
      osDescription: 'macOS',
      frameworkDescription: '.NET 10',
      startedAt: '2024-01-01T00:00:00Z',
    };
    mockAxiosInstance.get.mockResolvedValue({ data: versionData });

    const result = await healthApi.getVersion();
    expect(result).toEqual(versionData);
    expect(mockAxiosInstance.get).toHaveBeenCalledWith('/version');
  });

  it('getStatus calls GET /status', async () => {
    const statusData = {
      isHealthy: true,
      uptime: '00:30:00',
      memoryUsageMb: 64,
      threadCount: 12,
      gcTotalMemoryMb: 32,
      gen0Collections: 10,
      gen1Collections: 2,
      gen2Collections: 1,
      timestamp: '2024-01-01T00:30:00Z',
    };
    mockAxiosInstance.get.mockResolvedValue({ data: statusData });

    const result = await healthApi.getStatus();
    expect(result).toEqual(statusData);
    expect(mockAxiosInstance.get).toHaveBeenCalledWith('/status');
  });
});
