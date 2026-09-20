import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import toast from 'react-hot-toast';
import { crossCloudTraceApi } from '../lib/api/crossCloudTrace';
import type { CrossCloudTraceResponse } from '../lib/api/types';
import type { ApiError } from '../lib/api/types';
import { useDemoContext, rejectDemoModeMutation } from '../lib/demo/DemoContext';

/**
 * Cross-cloud trace search. Implemented on top of `useQuery` (keyed by the searched traceId)
 * rather than `useMutation` — the page fires this from a `useEffect` on mount for deep links
 * (`?traceId=`), and a mutation invoked imperatively from an effect is a known-fragile pattern
 * under React StrictMode: its dev-mode double-invoke of effects could leave the mutation's
 * result never reflected in the UI (search completes server-side, spinner never clears).
 * `useQuery`'s enabled/queryKey-driven fetch is the pattern the library's StrictMode
 * guarantees actually target, so the same "run once on mount" need is safe here.
 * The `mutate`-shaped return keeps every existing caller/test unchanged.
 */
export function useCrossCloudTrace(initialTraceId?: string) {
  const { isDemoMode } = useDemoContext();
  // Lazy initializer so a deep link (?traceId=) starts the search on the very first render —
  // no mount-time effect involved, which sidesteps StrictMode's dev-mode double-invoke of
  // effects entirely rather than trying to make an effect-triggered kickoff safe against it.
  const [traceId, setTraceId] = useState<string | null>(() => initialTraceId || null);

  const query = useQuery<CrossCloudTraceResponse, ApiError>({
    queryKey: ['cross-cloud-trace', traceId],
    queryFn: async () => {
      try {
        return isDemoMode ? await rejectDemoModeMutation() : await crossCloudTraceApi.trace(traceId as string);
      } catch (err) {
        const error = err as ApiError;
        const errorMessage =
          error?.response?.data?.detail ||
          error?.response?.data?.message ||
          error?.message ||
          'Cross-cloud trace failed.';
        if (import.meta.env.DEV) console.error('Cross-cloud trace error:', error?.message ?? 'unknown');
        toast.error(errorMessage, { duration: 6000 });
        throw err;
      }
    },
    enabled: traceId !== null,
    retry: false,
    gcTime: 0,
  });

  return {
    mutate: (id: string) => {
      // Re-searching the same traceId wouldn't otherwise refetch — same queryKey, no state
      // change to trigger it.
      if (id === traceId) {
        query.refetch();
      } else {
        setTraceId(id);
      }
    },
    data: query.data,
    isPending: query.isFetching,
    isSuccess: query.isSuccess,
    isError: query.isError,
    error: query.error,
  };
}
