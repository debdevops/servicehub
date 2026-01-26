import axios, { AxiosError } from 'axios';
import toast from 'react-hot-toast';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5153/api/v1';

export const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
  timeout: 30000, // 30 seconds
});

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

// Request interceptor: Add API key
apiClient.interceptors.request.use(
  (config) => {
    const apiKey = localStorage.getItem('servicehub:api-key');
    if (apiKey) {
      config.headers['X-API-Key'] = apiKey;
    }
    return config;
  },
  (error) => Promise.reject(error)
);

// Response interceptor: Handle errors
apiClient.interceptors.response.use(
  (response) => response,
  (error: AxiosError<{ message?: string; errors?: Record<string, string[]> }>) => {
    // Network error
    if (!error.response) {
      const errorKey = 'network-error';
      if (shouldShowError(errorKey)) {
        toast.error('Network error. Check if API is running.');
      }
      return Promise.reject(error);
    }

    // Handle specific status codes
    const status = error.response.status;
    const url = error.config?.url || 'unknown';
    
    switch (status) {
      case 401: {
        const errorKey = `${status}-${url}`;
        if (shouldShowError(errorKey)) {
          toast.error('Unauthorized. Check your API key.');
        }
        break;
      }
      case 403: {
        const errorKey = `${status}-${url}`;
        if (shouldShowError(errorKey)) {
          toast.error('Access denied.');
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
            Object.values(validationErrors).flat().forEach(msg => toast.error(msg));
          }
        }
        break;
      }
      case 500:
      case 502:
      case 503: {
        const errorKey = `${status}-server`;
        if (shouldShowError(errorKey)) {
          toast.error('Server error. Please try again later.');
        }
        break;
      }
    }

    return Promise.reject(error);
  }
);
