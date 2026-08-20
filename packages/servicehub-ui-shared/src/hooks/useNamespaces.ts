import { useQuery, useMutation, useQueryClient, UseQueryOptions } from '@tanstack/react-query';
import { namespacesApi } from '../lib/api/namespaces';
import { Namespace, CreateNamespaceRequest, ApiError } from '../lib/api/types';
import { useDemoContext, rejectDemoModeMutation } from '../lib/demo/DemoContext';
import { getMockNamespaces } from '../lib/demo/mockProviders';
import toast from 'react-hot-toast';

export function useNamespaces() {
  const { isDemoMode, cloudProvider } = useDemoContext();

  const options: UseQueryOptions<Namespace[]> = isDemoMode && cloudProvider
    ? {
        queryKey: ['namespaces', 'demo', cloudProvider],
        queryFn: (): Promise<Namespace[]> => Promise.resolve(getMockNamespaces(cloudProvider)),
        staleTime: Infinity,
      }
    : {
        queryKey: ['namespaces'],
        queryFn: namespacesApi.list,
      };

  return useQuery(options);
}

export function useNamespace(id: string) {
  const { isDemoMode, cloudProvider } = useDemoContext();

  const options: UseQueryOptions<Namespace> = isDemoMode && cloudProvider
    ? {
        queryKey: ['namespaces', 'demo', cloudProvider, id],
        queryFn: (): Promise<Namespace> => Promise.resolve(getMockNamespaces(cloudProvider)[0]),
        enabled: !!id,
        staleTime: Infinity,
      }
    : {
        queryKey: ['namespaces', id],
        queryFn: () => namespacesApi.get(id),
        enabled: !!id,
      };

  return useQuery(options);
}

export function useCreateNamespace() {
  const queryClient = useQueryClient();
  const { isDemoMode } = useDemoContext();

  return useMutation({
    mutationFn: (data: CreateNamespaceRequest) =>
      isDemoMode ? rejectDemoModeMutation() : namespacesApi.create(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['namespaces'] });
      toast.success('Namespace connected successfully');
    },
    onError: (error: ApiError) => {
      // Extract the specific error message from the API response
      const errorMessage = 
        error?.response?.data?.detail || 
        error?.response?.data?.message || 
        error?.message || 
        'Failed to connect namespace. Verify the connection string format and permissions.';
      
      // Log error name only in dev; never log the full error object (may contain response data)
      if (import.meta.env.DEV) {
        console.error('Namespace creation error:', error?.message ?? 'unknown');
      }
      toast.error(errorMessage, {
        duration: 6000,
      });
    },
  });
}

export function useDeleteNamespace() {
  const queryClient = useQueryClient();
  const { isDemoMode } = useDemoContext();

  return useMutation({
    mutationFn: (id: string) => (isDemoMode ? rejectDemoModeMutation() : namespacesApi.delete(id)),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['namespaces'] });
      toast.success('Namespace deleted');
    },
    onError: () => {
      toast.error('Failed to delete namespace. The namespace may still be in use.', {
        duration: 5000,
      });
    },
  });
}

export function useTestConnection() {
  const { isDemoMode } = useDemoContext();

  return useMutation({
    mutationFn: (id: string) =>
      isDemoMode ? rejectDemoModeMutation() : namespacesApi.testConnection(id),
    onSuccess: (data) => {
      if (data.isConnected) {
        toast.success(data.message || 'Connection successful');
      } else {
        toast.error(data.message || 'Connection failed. Check if the namespace is accessible.', {
          duration: 5000,
        });
      }
    },
    onError: () => {
      toast.error('Failed to test connection. Ensure the API server is running.', {
        duration: 5000,
      });
    },
  });
}
