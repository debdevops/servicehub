import { useEffect, useMemo } from 'react';
import { Link, useRouteError } from 'react-router-dom';
import { RefreshCw, Github, Home } from 'lucide-react';
import { appInsights } from '@/lib/telemetry';
import { getLastCorrelationId } from '@/lib/api/client';

const GITHUB_ISSUES_URL = 'https://github.com/debdevops/servicehub/issues/new';

// Browser-specific phrasing for a failed dynamic import (stale deployment: the chunk
// hash in the loaded HTML no longer matches what the server serves).
const CHUNK_LOAD_ERROR_PATTERN = /dynamically imported module|importing a module script failed|failed to fetch/i;

function toErrorMessage(error: unknown): string {
  if (error instanceof Error) return error.message;
  if (typeof error === 'string') return error;
  if (error == null) return 'Unknown error';
  try {
    return JSON.stringify(error);
  } catch {
    return 'Unknown error';
  }
}

function generateClientErrorId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  return `client-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
}

/**
 * Route-level errorElement. Replaces the silent "redirect to /welcome" that previously
 * swallowed every render error with no message, no identifier, and no telemetry.
 */
export function RouteErrorPage() {
  const error = useRouteError();
  const message = toErrorMessage(error);
  const isChunkLoadError = CHUNK_LOAD_ERROR_PATTERN.test(message);
  // Prefer the server's own correlation id (ties this report to backend logs);
  // fall back to a client-generated one so the user always has something to quote.
  const errorId = useMemo(() => getLastCorrelationId() ?? generateClientErrorId(), []);

  useEffect(() => {
    // Non-blocking: a telemetry failure must not itself blank this page.
    try {
      appInsights.trackException({
        exception: error instanceof Error ? error : new Error(message),
        properties: { routeError: true, chunkLoadError: isChunkLoadError, errorId },
      });
    } catch {
      // Swallow — reporting failure is not the user's problem.
    }
    // Report once per mount; re-running on every render would spam telemetry.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const issueUrl = `${GITHUB_ISSUES_URL}?title=${encodeURIComponent(`Error: ${message.slice(0, 100)}`)}&body=${encodeURIComponent(`Error ID: ${errorId}\n\n${message}`)}`;

  return (
    <div className="flex flex-col items-center justify-center h-screen bg-gray-50 px-4">
      <div className="text-center max-w-md p-8 bg-white rounded-lg shadow-lg">
        <div className="text-6xl mb-4">{isChunkLoadError ? '🔄' : '⚠️'}</div>
        <h1 className="text-2xl font-bold text-red-600 mb-4">
          {isChunkLoadError ? 'Update available' : 'Something went wrong'}
        </h1>
        <p className="text-gray-600 mb-6">
          {isChunkLoadError
            ? 'This page was updated since it was loaded. Reload to get the latest version.'
            : 'An unexpected error occurred. Sharing the error identifier below helps us track it down.'}
        </p>

        {!isChunkLoadError && (
          <p className="mb-6 text-sm text-gray-500">
            Error ID: <code className="px-2 py-1 bg-gray-100 rounded">{errorId}</code>
          </p>
        )}

        <div className="flex flex-wrap gap-3 justify-center">
          <button
            onClick={() => window.location.reload()}
            className="inline-flex items-center gap-2 px-6 py-3 bg-sky-500 text-white rounded-lg hover:bg-sky-600 transition-colors"
          >
            <RefreshCw className="w-4 h-4" />
            Reload Page
          </button>
          {!isChunkLoadError && (
            <a
              href={issueUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-2 px-6 py-3 bg-gray-200 text-gray-700 rounded-lg hover:bg-gray-300 transition-colors"
            >
              <Github className="w-4 h-4" />
              Report Issue
            </a>
          )}
          <Link
            to="/welcome"
            className="inline-flex items-center gap-2 px-6 py-3 bg-gray-200 text-gray-700 rounded-lg hover:bg-gray-300 transition-colors"
          >
            <Home className="w-4 h-4" />
            Go Home
          </Link>
        </div>
      </div>
    </div>
  );
}
