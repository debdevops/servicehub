import { Link } from 'react-router-dom'
import { Compass } from 'lucide-react'

/** A URL that names no screen. It keeps the shell (the sidebar still works) and says where to go, rather than showing a framework error. */
export function NotFoundPage() {
  return (
    <section className="mx-auto max-w-xl px-6 py-16 text-center">
      <Compass className="mx-auto mb-4 h-10 w-10 text-[var(--color-text-muted)]" aria-hidden="true" />
      <h1 className="text-2xl font-semibold text-[var(--color-text)]">That page doesn’t exist</h1>
      <p className="mt-2 text-[var(--color-text-muted)]">The address may be mistyped, or from an older version of ServiceHub.</p>
      <Link to="/" className="mt-6 inline-block rounded-lg bg-[var(--color-primary-600)] px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--color-primary-700)]">
        Go to Home
      </Link>
    </section>
  )
}
