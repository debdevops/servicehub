import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import toast from 'react-hot-toast';
import {
  getSimulatorStatus,
  injectFault,
  clearFaults,
  resetSimulator,
  advanceTime,
  injectDlqFlood,
  type SimulatorStatus,
  type InjectFaultRequest,
  type DlqFloodRequest,
} from '@/lib/api/simulator';
import type { ApiError } from '@/lib/api/types';

/** Polls simulator status every 5 seconds while the page is in focus. */
export function useSimulatorStatus() {
  return useQuery<SimulatorStatus, ApiError>({
    queryKey: ['simulator', 'status'],
    queryFn: getSimulatorStatus,
    refetchInterval: 5000,
    refetchIntervalInBackground: false,
    retry: 1,
    retryDelay: 1000,
  });
}

/**
 * One-shot check: returns true if the API is running in Simulator mode.
 * Never retries, never refetches — used by the Sidebar to conditionally
 * show the Simulator link.
 */
export function useIsSimulatorMode() {
  const { data, isLoading } = useQuery<SimulatorStatus, ApiError>({
    queryKey: ['simulator', 'mode-check'],
    queryFn: getSimulatorStatus,
    staleTime: Infinity,
    retry: false,
    refetchOnWindowFocus: false,
    refetchOnMount: false,
  });
  return {
    isSimulator: data?.environment === 'Simulator',
    isChecking: isLoading,
  };
}

/** Injects a fault — invalidates simulator status on success. */
export function useInjectFault() {
  const queryClient = useQueryClient();
  return useMutation<void, ApiError, InjectFaultRequest>({
    mutationFn: injectFault,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['simulator', 'status'] });
      toast.success('Fault injected');
    },
    onError: (error: ApiError) => {
      const msg =
        error?.response?.data?.detail ??
        error?.response?.data?.message ??
        error?.message ??
        'Failed to inject fault.';
      if (import.meta.env.DEV) console.error('Inject fault error:', error?.message ?? 'unknown');
      toast.error(msg, { duration: 6000 });
    },
  });
}

/** Clears all active faults — invalidates simulator status on success. */
export function useClearFaults() {
  const queryClient = useQueryClient();
  return useMutation<void, ApiError, void>({
    mutationFn: clearFaults,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['simulator', 'status'] });
      toast.success('All faults cleared');
    },
    onError: (error: ApiError) => {
      const msg =
        error?.response?.data?.detail ??
        error?.message ??
        'Failed to clear faults.';
      if (import.meta.env.DEV) console.error('Clear faults error:', error?.message ?? 'unknown');
      toast.error(msg);
    },
  });
}

/** Resets and reseeds the simulator — invalidates all relevant queries. */
export function useResetSimulator() {
  const queryClient = useQueryClient();
  return useMutation<void, ApiError, void>({
    mutationFn: resetSimulator,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['simulator'] });
      queryClient.invalidateQueries({ queryKey: ['namespaces'] });
      queryClient.invalidateQueries({ queryKey: ['messages'] });
      toast.success('Simulator reset and reseeded');
    },
    onError: (error: ApiError) => {
      const msg =
        error?.response?.data?.detail ??
        error?.message ??
        'Failed to reset simulator.';
      if (import.meta.env.DEV) console.error('Reset simulator error:', error?.message ?? 'unknown');
      toast.error(msg);
    },
  });
}

/** Advances simulated time — invalidates simulator status on success. */
export function useAdvanceTime() {
  const queryClient = useQueryClient();
  return useMutation<void, ApiError, number>({
    mutationFn: (seconds: number) => advanceTime(seconds),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['simulator', 'status'] });
      toast.success('Time advanced');
    },
    onError: (error: ApiError) => {
      const msg =
        error?.response?.data?.detail ??
        error?.message ??
        'Failed to advance time.';
      if (import.meta.env.DEV) console.error('Advance time error:', error?.message ?? 'unknown');
      toast.error(msg);
    },
  });
}

/** Injects DLQ flood — invalidates simulator status on success. */
export function useInjectDlqFlood() {
  const queryClient = useQueryClient();
  return useMutation<void, ApiError, DlqFloodRequest>({
    mutationFn: injectDlqFlood,
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: ['simulator', 'status'] });
      toast.success(`Injected ${variables.count} messages into ${variables.entityName} DLQ`);
    },
    onError: (error: ApiError) => {
      const msg =
        error?.response?.data?.detail ??
        error?.message ??
        'Failed to inject DLQ flood.';
      if (import.meta.env.DEV) console.error('DLQ flood error:', error?.message ?? 'unknown');
      toast.error(msg);
    },
  });
}
