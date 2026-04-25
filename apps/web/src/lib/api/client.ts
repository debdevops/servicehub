import axios, { AxiosError } from 'axios';
import toast from 'react-hot-toast';

// Extend AxiosRequestConfig so hooks can mark background-polling requests
// as silent — the error interceptor skips toast notifications for these.
declare module 'axios' {
  interface AxiosRequestConfig {
    _silent?: boolean;
  }
}

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
 * Retries with exponential backoff to handle transient failures and multi-instance
 * Azure App Service routing mismatches.
 * Returns true if a new token was successfully obtained.
 */
async function refreshSpaToken(maxRetries = 3): Promise<boolean> {
  for (let attempt = 0; attempt < maxRetries; attempt++) {
    try {
      const response = await fetch('/internal/spa-token', { cache: 'no-store' });
      if (!response.ok) {
        if (attempt < maxRetries - 1) {
          await new Promise(r => setTimeout(r, 200 * Math.pow(2, attempt)));
          continue;
        }
        return false;
      }
      const newToken = await response.text();
      if (!newToken?.trim()) {
        if (attempt < maxRetries - 1) {
          await new Promise(r => setTimeout(r, 200 * Math.pow(2, attempt)));
          continue;
        }
        return false;
      }

      // Update (or create) the meta tag so subsequent requests use the fresh token
      let meta = document.querySelector<HTMLMetaElement>('meta[name="spa-token"]');
      if (!meta) {
        meta = document.createElement('meta');
        meta.setAttribute('name', 'spa-token');
        document.head.appendChild(meta);
      }
      meta.setAttribute('content', newToken.trim());
      scheduleProactiveRefresh();
      return true;
    } catch {
      if (attempt < maxRetries - 1) {
        await new Promise(r => setTimeout(r, 200 * Math.pow(2, attempt)));
        continue;
      }
      return false;
    }
  }
  return false;
}

/**
 * Proactively refresh the SPA token before it expires.
 * The server token lifetime is 2 hours; we refresh every 90 minutes
 * so the token never expires during active use.
 */
let proactiveRefreshTimer: ReturnType<typeof setTimeout> | null = null;
const PROACTIVE_REFRESH_MS = 90 * 60 * 1000; // 90 minutes

function scheduleProactiveRefresh() {
  if (proactiveRefreshTimer) clearTimeout(proactiveRefreshTimer);
  proactiveRefreshTimer = setTimeout(async () => {
    await refreshSpaToken();
  }, PROACTIVE_REFRESH_MS);
}

// Kick off the proactive refresh cycle if a SPA token is already present
if (getSpaToken()) {
  scheduleProactiveRefresh();
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
  
  return silentPatterns.some(pattern => url.includes(pattern));
}

// Response interceptor: Handle errors with recovery guidance
apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError<{ message?: string; errors?: Record<string, string[]> }>) => {
    // Silent mode: skip all toast notifications.
    // Used by background-polling hooks (useQueues, useTopics, useSubscriptions,
    // useMessages) so Service Bus connectivity failures don't spam the user.
    if (error.config?._silent) {
      return Promise.reject(error);
    }

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
        // Auto-refresh the SPA token and retry the request.
        // Handles two production failure modes:
        //   1. Multi-instance Azure App Service where each instance has a different
        //      ephemeral HMAC key (Security:SpaToken:Secret not set) — the retry
        //      hits the same instance as the token-refresh call, giving a matching key.
        //   2. Token expiry — the stale token in the HTML <meta> tag is replaced
        //      with a fresh one before the request is retried.
        // We allow up to 2 refresh+retry cycles to handle transient instance mismatches.
        const originalConfig = error.config as (typeof error.config & { _spaRetryCount?: number });
        const retryCount = originalConfig?._spaRetryCount ?? 0;
        if (retryCount < 2 && getSpaToken() !== null && originalConfig) {
          originalConfig._spaRetryCount = retryCount + 1;
          const refreshed = await refreshSpaToken();
          if (refreshed) {
            originalConfig.headers = originalConfig.headers ?? {};
            originalConfig.headers['X-SPA-Token'] = getSpaToken();
            return apiClient(originalConfig);
          }
        }

        const errorKey = `${status}-${url}`;
        if (shouldShowError(errorKey)) {
          toast.error('Session expired. Please refresh the page to continue.', {
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
      case 429: {
        // Rate limit hit — show a single debounced toast, never for silent calls (already exited above)
        const retryAfter = (error.response.headers as Record<string, string>)?.['retry-after'] ?? '60';
        const errorKey = '429-rate-limit';
        if (shouldShowError(errorKey)) {
          toast.error(`Too many requests. Retry in ${retryAfter}s — try refreshing less frequently.`, {
            duration: 5000,
          });
        }
        break;
      }
      case 404: {
        if (!isSilent404(url)) {
          // Show feedback for message operation 404s (replay, dead-letter, cancel)
          const isMessageOp = url.match(/\/messages\/[a-f0-9-]+/i);
          const errorKey = `404-${url}`;
          if (shouldShowError(errorKey)) {
            toast.error(
              isMessageOp
                ? 'Message not found — it may have been consumed, expired, or already replayed.'
                : 'Resource not found.',
              { duration: 4000 }
            );
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
