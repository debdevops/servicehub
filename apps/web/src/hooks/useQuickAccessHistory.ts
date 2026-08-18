import { useCallback, useEffect, useRef, useState } from 'react';
import { useLocation, useNavigate, useNavigationType } from 'react-router-dom';

interface HistoryEntry {
  key: string;
}

interface QuickAccessHistory {
  canGoBack: boolean;
  canGoForward: boolean;
  goBack: () => void;
  goForward: () => void;
}

/**
 * Tracks the router's own session history (PUSH/REPLACE/POP) so in-panel
 * Back/Forward controls know whether a previous/next entry exists — the
 * browser History API itself exposes no way to query that. Movement itself
 * is delegated to the router's real history via navigate(-1)/navigate(1),
 * not a parallel navigation mechanism.
 */
export function useQuickAccessHistory(): QuickAccessHistory {
  const location = useLocation();
  const navigationType = useNavigationType();
  const navigate = useNavigate();

  const entriesRef = useRef<HistoryEntry[]>([]);
  const indexRef = useRef(0);
  const [canGoBack, setCanGoBack] = useState(false);
  const [canGoForward, setCanGoForward] = useState(false);

  useEffect(() => {
    const entries = entriesRef.current;

    if (entries.length === 0) {
      // Fresh mount (page load/refresh) — never inherit a synthetic history.
      entriesRef.current = [{ key: location.key }];
      indexRef.current = 0;
    } else if (navigationType === 'PUSH') {
      entries.splice(indexRef.current + 1);
      entries.push({ key: location.key });
      indexRef.current = entries.length - 1;
    } else if (navigationType === 'REPLACE') {
      entries[indexRef.current] = { key: location.key };
    } else {
      // POP — our own goBack/goForward, or the user's real browser controls.
      const foundIndex = entries.findIndex((entry) => entry.key === location.key);
      if (foundIndex !== -1) {
        indexRef.current = foundIndex;
      } else {
        entriesRef.current = [{ key: location.key }];
        indexRef.current = 0;
      }
    }

    setCanGoBack(indexRef.current > 0);
    setCanGoForward(indexRef.current < entriesRef.current.length - 1);
  }, [location.key, navigationType]);

  const goBack = useCallback(() => {
    if (indexRef.current > 0) navigate(-1);
  }, [navigate]);

  const goForward = useCallback(() => {
    if (indexRef.current < entriesRef.current.length - 1) navigate(1);
  }, [navigate]);

  return { canGoBack, canGoForward, goBack, goForward };
}
