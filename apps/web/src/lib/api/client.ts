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
      toast.error('Network error. Check if API is running.');
      return Promise.reject(error);
    }

    // Handle specific status codes
    switch (error.response.status) {
      case 401:
        toast.error('Unauthorized. Check your API key.');
        break;
      case 403:
        toast.error('Access denied.');
        break;
      case 404:
        toast.error('Resource not found.');
        break;
      case 422:
        // Validation errors
        const validationErrors = error.response.data.errors;
        if (validationErrors) {
          Object.values(validationErrors).flat().forEach(msg => toast.error(msg));
        }
        break;
      case 500:
      case 502:
      case 503:
        toast.error('Server error. Please try again later.');
        break;
    }

    return Promise.reject(error);
  }
);
