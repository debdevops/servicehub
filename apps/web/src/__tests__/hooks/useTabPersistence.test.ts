import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useTabPersistence } from '@/hooks/useTabPersistence';

const STORAGE_KEY = 'servicehub:detail-tab';

describe('useTabPersistence', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(() => {
    localStorage.clear();
  });

  // ── Initial state ────────────────────────────────────────────────────────

  it('returns "properties" as the default tab when localStorage is empty', () => {
    const { result } = renderHook(() => useTabPersistence());
    expect(result.current[0]).toBe('properties');
  });

  it('restores a valid tab from localStorage on mount', () => {
    localStorage.setItem(STORAGE_KEY, 'body');
    const { result } = renderHook(() => useTabPersistence());
    expect(result.current[0]).toBe('body');
  });

  it('falls back to default when stored value is invalid', () => {
    localStorage.setItem(STORAGE_KEY, 'not-a-valid-tab');
    const { result } = renderHook(() => useTabPersistence());
    expect(result.current[0]).toBe('properties');
  });

  // ── setActiveTab ─────────────────────────────────────────────────────────

  it('updates the active tab state when setActiveTab is called', () => {
    const { result } = renderHook(() => useTabPersistence());
    act(() => {
      result.current[1]('ai');
    });
    expect(result.current[0]).toBe('ai');
  });

  it('persists the new tab to localStorage', () => {
    const { result } = renderHook(() => useTabPersistence());
    act(() => {
      result.current[1]('headers');
    });
    expect(localStorage.getItem(STORAGE_KEY)).toBe('headers');
  });

  it('accepts all valid tab values', () => {
    const validTabs = ['properties', 'body', 'ai', 'headers'] as const;
    const { result } = renderHook(() => useTabPersistence());

    for (const tab of validTabs) {
      act(() => {
        result.current[1](tab);
      });
      expect(result.current[0]).toBe(tab);
      expect(localStorage.getItem(STORAGE_KEY)).toBe(tab);
    }
  });

  // ── StorageEvent cross-tab sync ──────────────────────────────────────────

  it('syncs tab when a valid StorageEvent arrives for the storage key', () => {
    const { result } = renderHook(() => useTabPersistence());

    act(() => {
      window.dispatchEvent(
        new StorageEvent('storage', {
          key: STORAGE_KEY,
          newValue: 'headers',
          storageArea: localStorage,
        })
      );
    });

    expect(result.current[0]).toBe('headers');
  });

  it('ignores a StorageEvent with an invalid tab value', () => {
    const { result } = renderHook(() => useTabPersistence());

    act(() => {
      window.dispatchEvent(
        new StorageEvent('storage', {
          key: STORAGE_KEY,
          newValue: 'garbage-value',
          storageArea: localStorage,
        })
      );
    });

    expect(result.current[0]).toBe('properties');
  });

  it('ignores a StorageEvent for a different key', () => {
    localStorage.setItem(STORAGE_KEY, 'ai');
    const { result } = renderHook(() => useTabPersistence());

    act(() => {
      window.dispatchEvent(
        new StorageEvent('storage', {
          key: 'some-other-key',
          newValue: 'body',
          storageArea: localStorage,
        })
      );
    });

    expect(result.current[0]).toBe('ai');
  });

  it('ignores a StorageEvent with a null newValue', () => {
    localStorage.setItem(STORAGE_KEY, 'ai');
    const { result } = renderHook(() => useTabPersistence());

    act(() => {
      window.dispatchEvent(
        new StorageEvent('storage', {
          key: STORAGE_KEY,
          newValue: null,
          storageArea: localStorage,
        })
      );
    });

    expect(result.current[0]).toBe('ai');
  });

  // ── Cleanup ──────────────────────────────────────────────────────────────

  it('removes the storage event listener on unmount', () => {
    const removeEventListenerSpy = vi.spyOn(window, 'removeEventListener');
    const { unmount } = renderHook(() => useTabPersistence());

    unmount();

    expect(removeEventListenerSpy).toHaveBeenCalledWith('storage', expect.any(Function));
    removeEventListenerSpy.mockRestore();
  });
});
