import axios, { AxiosError } from 'axios';
import toast from 'react-hot-toast';

// When VITE_API_BASE_URL is not set, use a relative path (/api/v1).
// With the Vite proxy configured in vite.config.ts, /api requests are
// automatically forwarded to http://localhost:5153 on the same server.
// This means the browser always calls the same host it loaded the UI from —
// no CORS issues and no hardcoded hostnames.
//
// For remote server access, the Vite proxy handles routing automatically.
// You do NOT need to set VITE_API_BASE_URL when using the proxy.
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || '/api/v1';

// Read the SPA token injected into <meta name="spa-token"> by the server.
// This token proves the request originates from the co-hosted SPA (loaded
// from the same server) and cannot be obtained by Postman/curl since they
// never load the HTML page.
function getSpaToken(): string | null {
  const meta = document.querySelector('meta[name="spa-token"]');
  return meta?.getAttribute('content') ?? null;
}

export const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
  timeout: 30000, // 30 seconds
});

// Request interceptor: attach SPA token if available
apiClient.interceptors.request.use((config) => {
  const token = getSpaToken();
  if (token) {
    config.headers['X-SPA-Token'] = token;
  }
  return config;
});

/**
 * Fetch a fresh SPA token from the server and update the <meta> tag.
 * Called automatically when a 401 indicates an expired or instance-mismatched token.
 * Returns true if a new token was successfully obtained.
 */
async function refreshSpaToken(): Promise<boolean> {
  try {
    const response = await fetch('/internal/spa-token', { cache: 'no-store' });
    if (!response.ok) return false;
    const newToken = await response.text();
    if (!newToken?.trim()) return false;

    // Update (or create) the meta tag so subsequent requests use the fresh token
    let meta = document.querySelector<HTMLMetaElement>('meta[name="spa-token"]');
    if (!meta) {
      meta = document.createElement('meta');
      meta.setAttribute('name', 'spa-token');
      document.head.appendChild(meta);
    }
    meta.setAttribute('content', newToken.trim());
    return true;
  } catch {
    return false;
  }
}

// Debounce mechanism for error toasts to prevent duplicates
const recentErrors = new Map<string, number>();
const ERROR_DEBOUNCE_MS = 2000; // Show same error only once every 2 seconds
const MAX_ERROR_ENTRIES = 50; // Limit map size to prevent memory leak

function shouldShowError(errorKey: string): boolean {
  const now = Date.now();
  
  // Clean up old entries periodically to prevent memory leak
  if (recentErrors.size > MAX_ERROR_ENTRIES) {
    for (const [key, timestamp] of recentErrors) {
      if (now - timestamp > ERROR_DEBOUNCE_MS * 5) {
        recentErrors.delete(key);
      }
    }
  }
  
  const lastShown = recentErrors.get(errorKey);
  
  if (!lastShown || now - lastShown > ERROR_DEBOUNCE_MS) {
    recentErrors.set(errorKey, now);
    return true;
  }
  
  return false;
}

/**
 * Check if the URL matches any of the silent 404 patterns
 * These endpoints may not exist in the API yet
 */
function isSilent404(url: string): boolean {
  const silentPatterns = [
    '/insights',           // AI insights endpoints not implemented
    '/$deadletterqueue',   // Malformed DLQ URL (should use queueType param instead)
    '/%24deadletterqueue', // URL-encoded version
  ];
  
  // Special check for message-specific operations (DELETE /messages/{id})
  if (url.match(/\/messages\/[a-f0-9-]+$/i)) {
    return true;
  }
  
  return silentPatterns.some(pattern => url.includes(pattern));
}

// Response interceptor: Handle errors with recovery guidance
apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError<{ message?: string; errors?: Record<string, string[]> }>) => {
    // Network error
    if (!error.response) {
      const errorKey = 'network-error';
      if (shouldShowError(errorKey)) {
        toast.error('Cannot reach the API. If running on a remote server, ensure port 5153 is accessible.', {
          duration: 5000,
        });
      }
      return Promise.reject(error);
    }

    // Handle specific status codes with recovery guidance
    const status = error.response.status;
    const url = error.config?.url || 'unknown';
    
    switch (status) {
      case 401: {
        // Auto-refresh the SPA token and retry the request once.
        // This recovers two production failure modes:
        //   1. Multi-instance Azure App Service where each instance has a different
        //      ephemeral HMAC key (Security:SpaToken:Secret not set) — the retry
        //      hits the same instance as the token-refresh call, giving a matching key.
        //   2. 30-minute token expiry — the stale token in the HTML <meta> tag is
        //      replaced with a fresh one before the request is retried.
        const originalConfig = error.config as (typeof error.config & { _spaTokenRetried?: boolean });
        if (!originalConfig?._spaTokenRetried && getSpaToken() !== null) {
          originalConfig._spaTokenRetried = true;
          const refreshed = await refreshSpaToken();
          if (refreshed && originalConfig) {
            originalConfig.headers = originalConfig.headers ?? {};
            originalConfig.headers['X-SPA-Token'] = getSpaToken();
            return apiClient(originalConfig);
          }
        }

        const errorKey = `${status}-${url}`;
        if (shouldShowError(errorKey)) {
          toast.error('Unauthorized. Check your API key in settings or reconnect to the namespace.', {
            duration: 5000,
          });
        }
        break;
      }
      case 403: {
        const errorKey = `${status}-${url}`;
        if (shouldShowError(errorKey)) {
          toast.error('Access denied. Verify your connection string has the required permissions.', {
            duration: 5000,
          });
        }
        break;
      }
      case 404: {
        // Silently handle 404s for known missing endpoints
        if (!isSilent404(url)) {
          // Only log warnings for unexpected 404s in development
          if (import.meta.env.DEV) {
            console.warn(`Resource not found: ${url}`);
          }
        }
        break;
      }
      case 422: {
        // Validation errors
        const validationErrors = error.response.data.errors;
        if (validationErrors) {
          const errorKey = `${status}-validation`;
          if (shouldShowError(errorKey)) {
            Object.values(validationErrors).flat().forEach(msg => toast.error(msg, { duration: 5000 }));
          }
        }
        break;
      }
      case 500:
      case 502:
      case 503: {
        const errorKey = `${status}-server`;
        if (shouldShowError(errorKey)) {
          toast.error('Server error. Try refreshing or restart the API server.', {
            duration: 5000,
          });
        }
        break;
      }
    }

    return Promise.reject(error);
  }
);
