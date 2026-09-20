import { useMutation } from '@tanstack/react-query';
import toast from 'react-hot-toast';
import {
  narrationsApi,
  correlationFindingsApi,
  backlogForecastsApi,
  driftFindingsApi,
  type NarrationGenerationResponse,
  type CorrelationDetectionResponse,
  type BacklogForecastResponse,
  type ContractViolationExportResponse,
  type ForecastBacklogParams,
  type ExportContractViolationsParams,
} from '../lib/api/proactiveInsights';
import type { ApiError } from '../lib/api/types';
import { extractApiError } from '../lib/api/errors';
import { useDemoContext, rejectDemoModeMutation } from '../lib/demo/DemoContext';

/**
 * Triggers I4 auto-narration (roadmap §5.B): a template-based plain-English summary stitching
 * I1-I3's structured output into one narration per emergent pattern, across every namespace the
 * caller can access. On-demand rather than a persisted list — narrations are cached server-side
 * only long enough to back a follow-up `GET /narrations/{id}` (see `INarrationResultCache`), so
 * demo mode has no fixture to fall back to and honestly rejects the mutation instead.
 */
export function useGenerateNarrations() {
  const { isDemoMode } = useDemoContext();

  return useMutation<NarrationGenerationResponse, ApiError, void>({
    mutationFn: () =>
      isDemoMode ? rejectDemoModeMutation() : narrationsApi.generate({}),
    onError: (error) => {
      toast.error(extractApiError(error, 'Failed to generate narrations.'), { duration: 6000 });
    },
  });
}

/**
 * Triggers proactive correlation detection — same-provider (C1) and cross-cloud (C2) — across
 * every namespace the caller can access. Same on-demand, non-persisted shape as narrations.
 */
export function useDetectCorrelationFindings() {
  const { isDemoMode } = useDemoContext();

  return useMutation<CorrelationDetectionResponse, ApiError, void>({
    mutationFn: () =>
      isDemoMode ? rejectDemoModeMutation() : correlationFindingsApi.detect({}),
    onError: (error) => {
      toast.error(extractApiError(error, 'Failed to detect correlations.'), { duration: 6000 });
    },
  });
}

/**
 * Triggers P4 predictive backlog-breach forecasting for one namespace: an arithmetic growth-rate
 * projection over data DLQ Intelligence already stores, not ML.
 */
export function useForecastBacklog() {
  const { isDemoMode } = useDemoContext();

  return useMutation<BacklogForecastResponse, ApiError, ForecastBacklogParams>({
    mutationFn: (params: ForecastBacklogParams) =>
      isDemoMode ? rejectDemoModeMutation() : backlogForecastsApi.forecast(params),
    onError: (error) => {
      toast.error(extractApiError(error, 'Failed to forecast backlog growth.'), { duration: 6000 });
    },
  });
}

/**
 * Triggers P3's producer-facing contract-violation export: P2's drift findings packaged for the
 * upstream team that can fix the root cause, in plain language and as ready-to-hand-off Markdown.
 */
export function useExportContractViolations() {
  const { isDemoMode } = useDemoContext();

  return useMutation<ContractViolationExportResponse, ApiError, ExportContractViolationsParams>({
    mutationFn: (params: ExportContractViolationsParams) =>
      isDemoMode ? rejectDemoModeMutation() : driftFindingsApi.exportContractViolations(params),
    onError: (error) => {
      toast.error(extractApiError(error, 'Failed to generate the contract-violation export.'), { duration: 6000 });
    },
  });
}
