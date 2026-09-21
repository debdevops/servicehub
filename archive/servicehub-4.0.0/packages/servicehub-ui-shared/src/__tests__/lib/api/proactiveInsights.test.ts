import { vi, describe, it, expect, beforeEach } from 'vitest';
import {
  narrationsApi,
  correlationFindingsApi,
  backlogForecastsApi,
  driftFindingsApi,
} from '../../../lib/api/proactiveInsights';
import { apiClient } from '../../../lib/api/client';

vi.mock('../../../lib/api/client', () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}));

const mocked = vi.mocked(apiClient, true);

describe('narrationsApi', () => {
  beforeEach(() => vi.clearAllMocks());

  it('calls POST /narrations/generate with an empty window by default', async () => {
    const response = { startTime: '2026-08-28T00:00:00Z', endTime: '2026-08-29T00:00:00Z', narrations: [], generatedAt: '2026-08-29T00:00:00Z' };
    mocked.post.mockResolvedValueOnce({ data: response } as any);

    const result = await narrationsApi.generate();

    expect(mocked.post).toHaveBeenCalledWith('/narrations/generate', null, { params: {} });
    expect(result).toEqual(response);
  });

  it('forwards a provided time window', async () => {
    mocked.post.mockResolvedValueOnce({ data: { startTime: '', endTime: '', narrations: [], generatedAt: '' } } as any);

    await narrationsApi.generate({ startTime: '2026-08-01T00:00:00Z', endTime: '2026-08-02T00:00:00Z' });

    expect(mocked.post).toHaveBeenCalledWith('/narrations/generate', null, {
      params: { startTime: '2026-08-01T00:00:00Z', endTime: '2026-08-02T00:00:00Z' },
    });
  });
});

describe('correlationFindingsApi', () => {
  beforeEach(() => vi.clearAllMocks());

  it('calls POST /correlation-findings/detect', async () => {
    const response = { startTime: '', endTime: '', findings: [], detectedAt: '' };
    mocked.post.mockResolvedValueOnce({ data: response } as any);

    const result = await correlationFindingsApi.detect();

    expect(mocked.post).toHaveBeenCalledWith('/correlation-findings/detect', null, { params: {} });
    expect(result).toEqual(response);
  });
});

describe('backlogForecastsApi', () => {
  beforeEach(() => vi.clearAllMocks());

  it('calls POST /backlog-forecasts/forecast with the namespaceId as a query param', async () => {
    const response = { namespaceId: 'ns1', startTime: '', endTime: '', forecasts: [], detectedAt: '' };
    mocked.post.mockResolvedValueOnce({ data: response } as any);

    const result = await backlogForecastsApi.forecast({ namespaceId: 'ns1' });

    expect(mocked.post).toHaveBeenCalledWith('/backlog-forecasts/forecast', null, {
      params: { namespaceId: 'ns1' },
    });
    expect(result).toEqual(response);
  });

  it('forwards an optional alertThreshold alongside the namespaceId', async () => {
    mocked.post.mockResolvedValueOnce({ data: { namespaceId: 'ns1', startTime: '', endTime: '', forecasts: [], detectedAt: '' } } as any);

    await backlogForecastsApi.forecast({ namespaceId: 'ns1', alertThreshold: 500 });

    expect(mocked.post).toHaveBeenCalledWith('/backlog-forecasts/forecast', null, {
      params: { namespaceId: 'ns1', alertThreshold: 500 },
    });
  });
});

describe('driftFindingsApi.exportContractViolations', () => {
  beforeEach(() => vi.clearAllMocks());

  it('calls POST /drift-findings/export with the namespaceId as a query param', async () => {
    const response = {
      namespaceId: 'ns1', namespaceName: 'My NS', startTime: '', endTime: '', generatedAt: '',
      violations: [], markdownReport: '# Report',
    };
    mocked.post.mockResolvedValueOnce({ data: response } as any);

    const result = await driftFindingsApi.exportContractViolations({ namespaceId: 'ns1' });

    expect(mocked.post).toHaveBeenCalledWith('/drift-findings/export', null, {
      params: { namespaceId: 'ns1' },
    });
    expect(result).toEqual(response);
  });
});
