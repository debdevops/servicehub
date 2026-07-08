import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { namespacesApi } from '@/lib/api/namespaces';
import { Namespace, CreateNamespaceRequest, ApiError } from '@/lib/api/types';
import { useDemoContext } from '@/lib/demo/DemoContext';
import { getMockNamespaces } from '@/lib/demo/mockProviders';
import toast from 'react-hot-toast';

export function useNamespaces() {
  const { isDemoMode, cloudProvider } = useDemoContext();

  const options = isDemoMode && cloudProvider
    ? {
        queryKey: ['namespaces', 'demo', cloudProvider] as [string, string, string],
        queryFn: (): Promise<Namespace[]> => Promise.resolve(getMockNamespaces(cloudProvider)),
        staleTime: Infinity as number,
      }
    : {
        queryKey: ['namespaces'] as [string],
        queryFn: namespacesApi.list,
      };

  return useQuery<Namespace[]>(options as Parameters<typeof useQuery<Namespace[]>>[0]);
}

export function useNamespace(id: string) {
  const { isDemoMode, cloudProvider } = useDemoContext();

  const options = isDemoMode && cloudProvider
    ? {
        queryKey: ['namespaces', 'demo', cloudProvider, id] as [string, string, string, string],
        queryFn: (): Promise<Namespace> => Promise.resolve(getMockNamespaces(cloudProvider)[0]),
        enabled: !!id,
        staleTime: Infinity as number,
      }
    : {
        queryKey: ['namespaces', id] as [string, string],
        queryFn: () => namespacesApi.get(id),
        enabled: !!id,
      };

  return useQuery<Namespace>(options as Parameters<typeof useQuery<Namespace>>[0]);
}

export function useCreateNamespace() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: CreateNamespaceRequest) => namespacesApi.create(data),
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

  return useMutation({
    mutationFn: (id: string) => namespacesApi.delete(id),
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
  return useMutation({
    mutationFn: (id: string) => namespacesApi.testConnection(id),
    onSuccess: (data) => {
      if (data.isConnected) {
        toast.success(data.message || 'Connection successful');
      } else {
        toast.error(data.message || 'Connection failed. Check if the Service Bus namespace is accessible.', {
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
