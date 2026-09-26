import { isRouteErrorResponse, useRouteError } from 'react-router-dom'
import { TriangleAlert } from 'lucide-react'

/**
 * What a person sees if a screen crashes while drawing. Without it React Router shows its developer page.
 * It stands alone (no shell), so it cannot itself fail for the same reason, and it never prints a stack trace.
 */
export function RouteError() {
  const error = useRouteError()
  const missing = isRouteErrorResponse(error) && error.status === 404
  return (
    <main className="mx-auto max-w-xl px-6 py-24 text-center">
      <TriangleAlert className="mx-auto mb-4 h-10 w-10 text-[var(--color-warning)]" aria-hidden="true" />
      <h1 className="text-2xl font-semibold text-[var(--color-text)]">{missing ? 'That page doesn’t exist' : 'Something went wrong on this screen'}</h1>
      <p className="mt-2 text-[var(--color-text-muted)]">
        {missing ? 'The address may be mistyped.' : 'Nothing was changed. Reloading usually fixes it; if it keeps happening, the browser console has the details.'}
      </p>
      <p className="mt-6 flex justify-center gap-3 text-sm font-semibold">
        <button type="button" onClick={() => window.location.reload()} className="rounded-lg border border-[var(--color-border)] px-4 py-2 hover:bg-[var(--color-surface-muted)]">
          Reload
        </button>
        <a href="/" className="rounded-lg bg-[var(--color-primary-600)] px-4 py-2 text-white hover:bg-[var(--color-primary-700)]">
          Go to Home
        </a>
      </p>
    </main>
  )
}
