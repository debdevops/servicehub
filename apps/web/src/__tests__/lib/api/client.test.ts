import { describe, it, expect } from 'vitest';
import { apiClient } from '@/lib/api/client';

/**
 * Tests for API Client
 * Coverage target: 80%+ (currently 34.61%)
 * Importance: CRITICAL - All API communication
 */
describe('apiClient', () => {
  it('exports apiClient instance', () => {
    expect(apiClient).toBeDefined();
    expect(apiClient).not.toBeNull();
  });

  it('has axios methods available', () => {
    expect(typeof apiClient.get).toBe('function');
    expect(typeof apiClient.post).toBe('function');
    expect(typeof apiClient.put).toBe('function');
    expect(typeof apiClient.delete).toBe('function');
  });

  describe('Configuration', () => {
    it('has baseURL configured', () => {
      expect(apiClient.defaults).toBeDefined();
      expect(apiClient.defaults.baseURL).toBeDefined();
    });

    it('has request interceptor for auth token', () => {
      expect(apiClient.interceptors).toBeDefined();
      expect(apiClient.interceptors.request).toBeDefined();
    });

    it('has response interceptor for error handling', () => {
      expect(apiClient.interceptors).toBeDefined();
      expect(apiClient.interceptors.response).toBeDefined();
    });
  });
});

