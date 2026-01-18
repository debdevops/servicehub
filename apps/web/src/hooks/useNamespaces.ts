import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { namespacesApi } from '@/lib/api/namespaces';
import { CreateNamespaceRequest } from '@/lib/api/types';
import toast from 'react-hot-toast';

export function useNamespaces() {
  return useQuery({
    queryKey: ['namespaces'],
    queryFn: namespacesApi.list,
  });
}

export function useNamespace(id: string) {
  return useQuery({
    queryKey: ['namespaces', id],
    queryFn: () => namespacesApi.get(id),
    enabled: !!id,
  });
}

export function useCreateNamespace() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: CreateNamespaceRequest) => namespacesApi.create(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['namespaces'] });
      toast.success('Namespace connected successfully');
    },
    onError: () => {
      toast.error('Failed to connect namespace');
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
      toast.error('Failed to delete namespace');
    },
  });
}

export function useTestConnection() {
  return useMutation({
    mutationFn: (id: string) => namespacesApi.testConnection(id),
    onSuccess: (data) => {
      if (data.success) {
        toast.success(data.message || 'Connection successful');
      } else {
        toast.error(data.message || 'Connection failed');
      }
    },
    onError: () => {
      toast.error('Failed to test connection');
    },
  });
}
