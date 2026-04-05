import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { oauthApi } from '@/lib/api/oauth';
import { AzureAuthStatus, AzureNamespaceInfo } from '@/lib/api/types';

const AZURE_AUTH_QUERY_KEY = ['azure', 'auth', 'status'] as const;
const AZURE_NAMESPACES_QUERY_KEY = ['azure', 'namespaces'] as const;

/**
 * Returns the current Azure sign-in status.
 * Polls every 30 seconds so session expiry is detected automatically.
 */
export function useAzureAuthStatus() {
  return useQuery<AzureAuthStatus>({
    queryKey: AZURE_AUTH_QUERY_KEY,
    queryFn: oauthApi.getStatus,
    staleTime: 30_000,
    refetchInterval: 60_000,
    refetchIntervalInBackground: false,
    retry: false,
  });
}

/**
 * Returns the signed-in user's Azure Service Bus namespaces via ARM.
 * Only runs if the user is signed in.
 */
export function useAzureNamespaces(isSignedIn: boolean) {
  return useQuery<AzureNamespaceInfo[]>({
    queryKey: AZURE_NAMESPACES_QUERY_KEY,
    queryFn: oauthApi.listNamespaces,
    enabled: isSignedIn,
    staleTime: 60_000,
    retry: false,
  });
}

/**
 * Redirects the browser to the Azure sign-in page.
 * The backend generates the PKCE-protected authorization URL.
 */
export function useAzureSignIn() {
  return useMutation({
    mutationFn: async () => {
      const url = await oauthApi.getSignInUrl();
      // Full page navigation — Azure redirects back to the backend callback
      window.location.href = url;
    },
  });
}

/**
 * Signs out the current Azure session.
 * Invalidates the auth status and namespace queries after sign-out.
 */
export function useAzureSignOut() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: oauthApi.signOut,
    onSuccess: () => {
      queryClient.removeQueries({ queryKey: AZURE_AUTH_QUERY_KEY });
      queryClient.removeQueries({ queryKey: AZURE_NAMESPACES_QUERY_KEY });
    },
  });
}
