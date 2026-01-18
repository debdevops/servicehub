import { useState, useCallback, useEffect } from 'react';

export type DetailTab = 'properties' | 'body' | 'ai' | 'headers';

const STORAGE_KEY = 'servicehub:detail-tab';
const VALID_TABS: DetailTab[] = ['properties', 'body', 'ai', 'headers'];
const DEFAULT_TAB: DetailTab = 'properties';

/**
 * Hook to persist the active detail panel tab across message selections.
 * Stores the selected tab in localStorage and restores it on mount.
 * 
 * @returns [activeTab, setActiveTab] tuple
 */
export function useTabPersistence(): [DetailTab, (tab: DetailTab) => void] {
  const [activeTab, setActiveTabState] = useState<DetailTab>(() => {
    // Initialize from localStorage if available
    if (typeof window === 'undefined') return DEFAULT_TAB;
    
    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      if (stored && VALID_TABS.includes(stored as DetailTab)) {
        return stored as DetailTab;
      }
    } catch {
      // localStorage might be unavailable or throw
      console.warn('Failed to read tab persistence from localStorage');
    }
    
    return DEFAULT_TAB;
  });

  // Persist to localStorage when tab changes
  const setActiveTab = useCallback((tab: DetailTab) => {
    if (!VALID_TABS.includes(tab)) {
      console.warn(`Invalid tab "${tab}", falling back to "${DEFAULT_TAB}"`);
      tab = DEFAULT_TAB;
    }
    
    setActiveTabState(tab);
    
    try {
      localStorage.setItem(STORAGE_KEY, tab);
    } catch {
      console.warn('Failed to persist tab to localStorage');
    }
  }, []);

  // Sync with localStorage changes from other tabs/windows
  useEffect(() => {
    const handleStorageChange = (e: StorageEvent) => {
      if (e.key === STORAGE_KEY && e.newValue) {
        if (VALID_TABS.includes(e.newValue as DetailTab)) {
          setActiveTabState(e.newValue as DetailTab);
        }
      }
    };

    window.addEventListener('storage', handleStorageChange);
    return () => window.removeEventListener('storage', handleStorageChange);
  }, []);

  return [activeTab, setActiveTab];
}
