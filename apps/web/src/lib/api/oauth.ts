import { apiClient } from './client';
import { AzureAuthStatus, AzureNamespaceInfo } from './types';

export const oauthApi = {
  /**
   * Returns the current Azure sign-in status for this browser session.
   * Safe to call on page load to check if the user is already signed in.
   */
  getStatus: async (): Promise<AzureAuthStatus> => {
    const response = await apiClient.get<AzureAuthStatus>('/auth/azure/status');
    return response.data;
  },

  /**
   * Returns the Azure OAuth 2.0 authorization URL.
   * Redirect the browser to this URL (window.location.href = url) to start sign-in.
   */
  getSignInUrl: async (): Promise<string> => {
    const response = await apiClient.get<{ authorizationUrl: string }>('/auth/azure/sign-in');
    return response.data.authorizationUrl;
  },

  /**
   * Lists the signed-in user's Azure Service Bus namespaces via ARM.
   * Requires an active session (user must be signed in).
   */
  listNamespaces: async (): Promise<AzureNamespaceInfo[]> => {
    const response = await apiClient.get<AzureNamespaceInfo[]>('/auth/azure/namespaces');
    return response.data;
  },

  /**
   * Signs out the current Azure session and clears the session cookie.
   */
  signOut: async (): Promise<void> => {
    await apiClient.delete('/auth/azure/session');
  },
};
