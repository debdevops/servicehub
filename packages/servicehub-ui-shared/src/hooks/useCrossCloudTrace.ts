import { useMutation } from '@tanstack/react-query';
import toast from 'react-hot-toast';
import { crossCloudTraceApi } from '../lib/api/crossCloudTrace';
import type { CrossCloudTraceResponse } from '../lib/api/types';
import type { ApiError } from '../lib/api/types';
import { useDemoContext, rejectDemoModeMutation } from '../lib/demo/DemoContext';

export function useCrossCloudTrace() {
  const { isDemoMode } = useDemoContext();

  return useMutation<CrossCloudTraceResponse, ApiError, string>({
    mutationFn: (traceId: string) =>
      isDemoMode ? rejectDemoModeMutation() : crossCloudTraceApi.trace(traceId),
    onError: (error: ApiError) => {
      const errorMessage =
        error?.response?.data?.detail ||
        error?.response?.data?.message ||
        error?.message ||
        'Cross-cloud trace failed.';
      if (import.meta.env.DEV) console.error('Cross-cloud trace error:', error?.message ?? 'unknown');
      toast.error(errorMessage, { duration: 6000 });
    },
  });
}
